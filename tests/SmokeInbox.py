#!/usr/bin/env python3
"""Offline CI smoke for Archer's local, source-derived operator inbox.

Adds synthetic records ONLY to the isolated CI state file. No production
monitoring, Twilio, APNs, AI model, account or remote GitHub operation is used.
"""
import json
import os
import sys
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from urllib.request import Request, urlopen
from uuid import uuid4


API = "http://127.0.0.1:5087"


def get_json(path, method="GET"):
    request = Request(API + path, method=method)
    if method == "POST":
        request.add_header("Content-Type", "application/json")
        request.data = b"{}"
    with urlopen(request, timeout=15) as result:
        return json.load(result)


def write_state(path, data):
    fd, name = tempfile.mkstemp(dir=path.parent, prefix="inbox-ci-", suffix=".json")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as output:
            json.dump(data, output, indent=2)
        os.replace(name, path)
    finally:
        if os.path.exists(name):
            os.remove(name)


def main(path):
    state = json.loads(path.read_text(encoding="utf-8"))
    now = datetime.now(timezone.utc).isoformat()
    mission_id, task_id, monitor_id, incident_id = [str(uuid4()) for _ in range(4)]
    state.setdefault("missions", []).append({
        "id": mission_id, "title": "Synthetic CI mission",
        "status": "Paused", "updatedAtUtc": now,
        "tasks": [{"id": task_id, "title": "CI task for inbox",
                   "status": "Failed", "lastError": "Synthetic test only"}],
        "artifacts": []
    })
    state.setdefault("websiteMonitors", []).append({
        "id": monitor_id, "name": "CI example monitoring",
        "url": "https://example.com/", "enabled": False,
        "status": "Down", "consecutiveFailures": 3,
        "lastCheckedAtUtc": now
    })
    state.setdefault("websiteIncidents", []).append({
        "id": incident_id, "websiteMonitorId": monitor_id,
        "websiteName": "CI example monitoring", "kind": "Outage",
        "message": "Synthetic three-check failure, not a real outage",
        "occurredAtUtc": now
    })
    original_attempts = len(state.get("communicationAttempts", []))
    write_state(path, state)

    first = get_json("/api/inbox")
    assert first["delivered"] is False
    assert first["needsReviewCount"] >= 2, first
    mine = [i for i in first["items"] if i["category"] in ("Website", "Mission")]
    expected = {
        "website:" + incident_id.replace("-", ""),
        "task:" + mission_id.replace("-", "") + ":" + task_id.replace("-", "")
    }
    found = {x["sourceKey"]: x for x in mine}
    assert expected.issubset(found), found
    assert found["website:" + incident_id.replace("-", "")]["priority"] == "UrgentReview"

    again = get_json("/api/inbox")
    again_map = {x["sourceKey"]: x["id"] for x in again["items"]}
    for key in expected:
        assert again_map[key] == found[key]["id"], "Duplicate/mutated inbox event"

    website_item = found["website:" + incident_id.replace("-", "")]
    get_json("/api/inbox/" + website_item["id"] + "/read", "POST")
    get_json("/api/inbox/" + website_item["id"] + "/acknowledge", "POST")
    marked = get_json("/api/inbox")
    marked_item = next(x for x in marked["items"] if x["id"] == website_item["id"])
    assert marked_item["readAtUtc"]
    assert marked_item["acknowledgedAtUtc"]

    state = json.loads(path.read_text(encoding="utf-8"))
    for mission in state["missions"]:
        if mission["id"] == mission_id:
            mission["tasks"][0]["status"] = "Pending"
    for monitor in state["websiteMonitors"]:
        if monitor["id"] == monitor_id:
            monitor["status"] = "Up"
            monitor["lastCheckedAtUtc"] = datetime.now(timezone.utc).isoformat()
    write_state(path, state)
    final = get_json("/api/inbox")
    final_by_key = {x["sourceKey"]: x for x in final["items"]}
    for key in expected:
        assert final_by_key[key]["resolvedAtUtc"], key

    state = json.loads(path.read_text(encoding="utf-8"))
    assert len(state.get("communicationAttempts", [])) == original_attempts
    print("PASS: inbox source grounding, stable dedup IDs, read/ack, recovery and zero provider sends")


if __name__ == "__main__":
    assert len(sys.argv) == 2, "Pass isolated CI state file path"
    main(Path(sys.argv[1]))
