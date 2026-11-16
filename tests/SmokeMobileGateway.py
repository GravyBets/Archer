#!/usr/bin/env python3
"""Isolated gateway API integration test. Uses only loopback and synthetic events.

Device bearer and internal secrets are read from CI environment variables.
Neither values nor a real device phone/location are ever printed.
"""
import json
import os
import sys
from datetime import datetime, timezone, timedelta
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from uuid import uuid4


BASE = "http://127.0.0.1:5091"
ADMIN = os.environ["ARCHER_GATEWAY_ADMIN_KEY"]
INGEST = os.environ["ARCHER_GATEWAY_INGEST_KEY"]


def request(path, method="GET", body=None, headers=None, expected=200):
    hdr = dict(headers or {})
    payload = None if body is None else json.dumps(body).encode("utf-8")
    if payload is not None:
        hdr["Content-Type"] = "application/json"
    req = Request(BASE + path, method=method, data=payload, headers=hdr)
    try:
        with urlopen(req, timeout=10) as response:
            actual, raw = response.status, response.read()
    except HTTPError as error:
        actual, raw = error.code, error.read()
    assert actual == expected, (method, path, expected, actual, raw[:150])
    return json.loads(raw) if raw else {}


def run(state_path):
    admin = {"X-Archer-Admin-Key": ADMIN}
    ingest = {"X-Archer-Ingest-Key": INGEST}
    current = datetime.now(timezone.utc)

    health = request("/health/live")
    assert health["publicAccessEnabled"] is False
    assert health["deliveryEnabled"] is False
    ready = request("/health/ready")
    assert ready["encryptedState"] is True
    request("/health/live", headers={"Origin": "https://malicious.invalid"}, expected=403)
    request("/health/live", headers={"Host": "malicious.invalid:5091"}, expected=403)
    request("/internal/audit", expected=403)
    request("/internal/devices/" + str(uuid4()) + "/rotate", "POST",
            {"lifetimeDays": 1}, expected=403)
    request("/v1/inbox", expected=401)
    request("/v1/inbox", headers={"Authorization": "Bearer totally-fake"}, expected=401)
    request("/internal/devices", expected=403)
    request("/internal/devices/enroll", method="POST",
            body={"label": "Synthetic iPhone", "lifetimeDays": 1}, expected=403)
    request("/internal/events", method="POST", body={"events": []}, expected=403)
    request("/internal/devices/enroll", "POST",
            {"label": "Invalid scope", "lifetimeDays": 1, "scopes": ["admin:write"]},
            admin, expected=400)
    request("/internal/devices/enroll", "POST",
            {"label": "Invalid scope", "lifetimeDays": 1, "scopes": ["inbox:ack"]},
            admin, expected=400)

    enrolled = request("/internal/devices/enroll", method="POST",
                       body={"label": "Synthetic iPhone", "lifetimeDays": 1},
                       headers=admin)
    token = enrolled["deviceToken"]
    assert token.startswith("archer_v1_") and len(token) == 74
    assert enrolled["scopes"] == ["inbox:read", "inbox:ack"]
    assert "tokenHash" not in json.dumps(enrolled)
    device_id = enrolled["deviceId"]
    bearer = {"Authorization": "Bearer " + token}

    inventory = request("/internal/devices", headers=admin)
    assert len(inventory) == 1
    assert "tokenHash" not in json.dumps(inventory)
    assert "deviceToken" not in json.dumps(inventory)
    assert request("/v1/inbox", headers=bearer)["count"] == 0
    assert request("/v1/inbox", headers={"Authorization": "Bearer " + ADMIN},
                   expected=401) == {}

    key = "website:" + uuid4().hex
    time = (current - timedelta(minutes=2)).isoformat()
    source = {
        "sourceKey": key, "category": "Website",
        "priority": "UrgentReview",
        "title": "Example site needs review",
        "summary": "Three failed HTTPS checks recorded, not a confirmed checkout outage.",
        "occurredAtUtc": time, "resolvedAtUtc": None
    }
    request("/internal/events", "POST", {"events": [source]}, expected=403)
    result = request("/internal/events", "POST", {"events": [source]}, ingest)
    assert result["accepted"] == 1 and result["newlyInserted"] == 1
    repeated = request("/internal/events", "POST", {"events": [source]}, ingest)
    assert repeated["newlyInserted"] == 0
    listed = request("/v1/inbox", headers=bearer)
    assert listed["count"] == 1 and not listed["deliveryEnabled"]
    item = listed["items"][0]
    assert item["priority"] == "UrgentReview"
    assert item["title"] == "Example site needs review"
    assert "sourceKey" not in item  # Only minimal mobile-facing fields.
    event_id = item["id"]
    sandbox = request("/v1/delivery-previews", headers=bearer)
    assert sandbox["actualNotificationsSent"] == 0
    assert sandbox["previews"] == []
    request("/v1/delivery-previews", expected=401)
    request("/internal/delivery/simulate", "POST",
            {"confirm": True, "maxEvents": 10}, expected=403)
    request("/internal/delivery/simulate", "POST",
            {"confirm": False, "maxEvents": 10}, admin, expected=400)
    planned = request("/internal/delivery/simulate", "POST",
                      {"confirm": True, "maxEvents": 10}, admin)
    assert planned["newPreviews"] == 1 and planned["actualNotificationsSent"] == 0
    assert planned["providerCharges"] is False
    planned_twice = request("/internal/delivery/simulate", "POST",
                            {"confirm": True, "maxEvents": 10}, admin)
    assert planned_twice["newPreviews"] == 0
    assert planned_twice["duplicatePreviewsSuppressed"] == 1
    previews = request("/v1/delivery-previews", headers=bearer)["previews"]
    assert len(previews) == 1
    assert previews[0]["channel"] == "push-preview"
    assert previews[0]["interruptOwner"] is False
    assert previews[0]["requiresIncidentVerification"] is True
    request("/v1/inbox/" + event_id + "/read", "POST", {}, expected=401)
    request("/v1/inbox/" + event_id + "/read", "POST", {}, bearer)
    request("/v1/inbox/" + event_id + "/acknowledge", "POST", {}, bearer)
    listed = request("/v1/inbox", headers=bearer)
    assert listed["items"][0]["readAtUtc"]
    assert listed["items"][0]["acknowledgedAtUtc"]

    fixed = dict(source)
    fixed["resolvedAtUtc"] = current.isoformat()
    result = request("/internal/events", "POST", {"events": [fixed]}, ingest)
    assert result["newlyInserted"] == 0
    request("/internal/events", "POST", {"events": [source]}, ingest)
    listed = request("/v1/inbox", headers=bearer)
    assert listed["count"] == 1
    assert listed["items"][0]["resolvedAtUtc"]
    assert listed["items"][0]["acknowledgedAtUtc"]

    malformed = dict(source)
    malformed["priority"] = "MoneyMoved"
    request("/internal/events", "POST", {"events": [malformed]}, ingest, expected=400)

    # A least-privileged device may VIEW but cannot acknowledge.
    reader = request("/internal/devices/enroll", "POST",
        {"label": "Read only", "lifetimeDays": 1, "scopes": ["inbox:read"]}, admin)
    readonly_bearer = {"Authorization": "Bearer " + reader["deviceToken"]}
    assert reader["scopes"] == ["inbox:read"]
    assert request("/v1/inbox", headers=readonly_bearer)["count"] == 1
    request("/v1/inbox/" + event_id + "/read", "POST", {}, readonly_bearer,
            expected=401)
    request("/v1/inbox/" + event_id + "/acknowledge", "POST", {},
            readonly_bearer, expected=401)

    # Shared device-authorized conversation journal: no AI, no cross-service sync.
    request("/v1/conversation/messages", expected=401)
    request("/v1/conversation/messages", headers=bearer, expected=401)
    request("/internal/conversation", "DELETE", expected=403)
    talker = request("/internal/devices/enroll", "POST", {
        "label": "Local chat writer", "lifetimeDays": 1,
        "scopes": ["conversation:read", "conversation:write"]
    }, admin)
    talk_bearer = {"Authorization": "Bearer " + talker["deviceToken"]}
    watcher = request("/internal/devices/enroll", "POST", {
        "label": "Local chat reader", "lifetimeDays": 1,
        "scopes": ["conversation:read"]
    }, admin)
    watcher_bearer = {"Authorization": "Bearer " + watcher["deviceToken"]}
    assert request("/v1/conversation/messages", headers=talk_bearer)["messages"] == []
    assert request("/v1/conversation/messages", headers=watcher_bearer)["savedOnly"] is True
    assert request("/v1/conversation/messages", headers=watcher_bearer)["assistantConnected"] is False
    request("/v1/inbox", headers=talk_bearer, expected=401)
    message_id = str(uuid4())
    message = {"clientMessageId": message_id, "content": "Hello, Archer. Any updates?"}
    request("/v1/conversation/messages", "POST", message,
            watcher_bearer, expected=401)
    request("/v1/conversation/messages", "POST",
            {"clientMessageId": str(uuid4()), "content": ""},
            talk_bearer, expected=400)
    created = request("/v1/conversation/messages", "POST", message, talk_bearer)
    assert created["outcome"] == "saved"
    assert "No AI reply" in created["note"]
    again_message = request("/v1/conversation/messages", "POST", message, talk_bearer)
    assert again_message["outcome"] == "duplicate"
    assert again_message["id"] == created["id"]
    changed = dict(message)
    changed["content"] = "Different content on the same client message ID"
    request("/v1/conversation/messages", "POST", changed,
            talk_bearer, expected=409)
    journal = request("/v1/conversation/messages", headers=watcher_bearer)
    assert len(journal["messages"]) == 1
    assert journal["messages"][0]["role"] == "owner"
    assert journal["messages"][0]["content"] == message["content"]
    assert journal["retentionDays"] == 7
    request("/internal/conversation", "DELETE", expected=403)
    deleted = request("/internal/conversation", "DELETE", headers=admin)
    assert deleted["deleted"] == 1
    assert request("/v1/conversation/messages", headers=talk_bearer)["messages"] == []

    # Rotate can only be done by admin, old bearer dies immediately.
    request("/internal/devices/" + device_id + "/rotate", "POST",
            {"lifetimeDays": 1}, expected=403)
    request("/internal/devices/" + device_id + "/rotate", "POST",
            {"lifetimeDays": 99}, admin, expected=400)
    replacement = request("/internal/devices/" + device_id + "/rotate",
                          "POST", {"lifetimeDays": 1}, admin)
    assert replacement["deviceId"] == device_id
    new_token = replacement["deviceToken"]
    assert new_token != token
    rotated_bearer = {"Authorization": "Bearer " + new_token}
    request("/v1/inbox", headers=bearer, expected=401)
    assert request("/v1/inbox", headers=rotated_bearer)["count"] == 1
    inventory = request("/internal/devices", headers=admin)
    current_device = next(x for x in inventory if x["id"] == device_id)
    assert current_device["rotatedAtUtc"]
    assert "deviceToken" not in json.dumps(inventory)

    audit = request("/internal/audit", headers=admin)
    actions = {x["action"] for x in audit["entries"]}
    required = {"device.enrolled", "device.rotated",
                "event.ingested", "event.resolved",
                "inbox.read", "inbox.acknowledged"}
    assert required.issubset(actions), actions
    assert token not in json.dumps(audit)
    assert new_token not in json.dumps(audit)
    assert "Example site needs review" not in json.dumps(audit)

    request("/internal/devices/" + device_id + "/revoke", "POST", {}, admin)
    request("/v1/inbox", headers=rotated_bearer, expected=401)
    request("/internal/devices/" + device_id + "/rotate", "POST",
            {"lifetimeDays": 1}, admin, expected=404)
    audit = request("/internal/audit", headers=admin)
    assert "device.revoked" in {x["action"] for x in audit["entries"]}
    assert "conversation.message_saved" in {x["action"] for x in audit["entries"]}
    assert "conversation.cleared" in {x["action"] for x in audit["entries"]}
    assert message["content"] not in json.dumps(audit)
    assert len(audit["entries"]) < 300
    request("/v1/inbox", headers=bearer, expected=401)
    stored_text = state_path.read_text(encoding="utf-8")
    encrypted = json.loads(stored_text)
    assert encrypted["format"] == "archer-gateway-aes256gcm-v1"
    assert all(encrypted.get(name) for name in ("nonce", "ciphertext", "tag"))
    assert set(encrypted) == {"format", "nonce", "ciphertext", "tag"}
    # No sensitive fields, messages, device hashes, tokens or audit trail appear on disk.
    for secret in (
        token, new_token, reader["deviceToken"], talker["deviceToken"],
        watcher["deviceToken"], message["content"], "tokenHashHex",
        "device.rotated", "device.revoked", "Example site needs review",
        "deliveryPreviews", "sandbox-policy-1",
        "Twilio", "OPENAI_API_KEY"
    ):
        assert secret not in stored_text, "State leaked a plaintext field: " + secret[:16]
    print("PASS: local gateway scoped journal, shared read, idempotent messages, private deletion, rotation, audit and zero AI sends")


if __name__ == "__main__":
    assert len(sys.argv) == 2, "Expected isolated gateway state file path."
    run(Path(sys.argv[1]))
