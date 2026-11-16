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
    request("/health/live", headers={"Origin": "https://malicious.invalid"}, expected=403)
    request("/v1/inbox", expected=401)
    request("/v1/inbox", headers={"Authorization": "Bearer totally-fake"}, expected=401)
    request("/internal/devices", expected=403)
    request("/internal/devices/enroll", method="POST",
            body={"label": "Synthetic iPhone", "lifetimeDays": 1}, expected=403)
    request("/internal/events", method="POST", body={"events": []}, expected=403)

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

    request("/internal/devices/" + device_id + "/revoke", "POST", {}, admin)
    request("/v1/inbox", headers=bearer, expected=401)
    stored_text = state_path.read_text(encoding="utf-8")
    assert token not in stored_text
    assert "tokenHashHex" in stored_text
    assert "Example site needs review" in stored_text
    assert "deliveryPreviews" in stored_text
    assert "sandbox-policy-1" in stored_text
    assert "Twilio" not in stored_text and "OPENAI_API_KEY" not in stored_text
    print("PASS: isolated gateway auth, dedup and revocation; sandbox routing decisions made zero real pushes, calls or model requests")


if __name__ == "__main__":
    assert len(sys.argv) == 2, "Expected isolated gateway state file path."
    run(Path(sys.argv[1]))
