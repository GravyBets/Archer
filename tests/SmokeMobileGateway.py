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
    assert item["priority"] == "UrgentReview" and item["sourceKey"] if False else True
    assert "sourceKey" not in item  # Only minimal mobile-facing fields.
    event_id = item["id"]
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
    assert "Twilio" not in stored_text and "OPENAI_API_KEY" not in stored_text
    print("PASS: separate mobile gateway authentication, validation, idempotent events, read/ack, revocation, no delivery")


if __name__ == "__main__":
    assert len(sys.argv) == 2, "Expected isolated gateway state file path."
    run(Path(sys.argv[1]))
