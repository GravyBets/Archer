#!/usr/bin/env python3
"""Offline local session/cursor smoke: multi-device reads, stable per-session
sequences, replay safety, archive protection and encrypted restart.

This starts a TEST-ONLY loopback gateway, never uses phone, APNs, AI or any
live business record and does not disable auth or rate limiting.
"""
import json
import os
import secrets
import tempfile
from datetime import datetime, timedelta, timezone
from pathlib import Path
from uuid import uuid4

from SmokeGatewayEncryption import run_gateway, stop, call, ADMIN


def request(path, method="GET", body=None, header=None, expected=200):
    status, raw = call(path, method, body, header)
    assert status == expected, (method, path, expected, status, raw[:150])
    return json.loads(raw) if raw else {}


def main():
    os.environ["ARCHER_GATEWAY_BRIDGE_KEY"] = secrets.token_urlsafe(48)
    key = secrets.token_hex(32)
    with tempfile.TemporaryDirectory(prefix="archer-sessions-ci-") as location:
        directory = Path(location)
        process = run_gateway(directory, key)
        try:
            admin = {"X-Archer-Admin-Key": ADMIN}
            request("/v1/conversation/sessions", expected=401)
            request("/v1/conversation/messages?after=-1", expected=401)

            writer = request("/internal/devices/enroll", "POST", {
                "label": "Local writer", "scopes": ["conversation:read", "conversation:write"]
            }, admin)
            reader = request("/internal/devices/enroll", "POST", {
                "label": "Local reader", "scopes": ["conversation:read"]
            }, admin)
            auth = {"Authorization": "Bearer " + writer["deviceToken"]}
            view = {"Authorization": "Bearer " + reader["deviceToken"]}
            sessions = request("/v1/conversation/sessions", header=view)
            assert len(sessions["sessions"]) == 1
            main_id = sessions["defaultSessionId"]
            assert sessions["sessions"][0]["id"] == main_id
            assert request("/v1/conversation/messages", header=view)["messages"] == []
            request("/v1/conversation/sessions", "POST", {"title": "Denied"},
                    view, expected=401)
            request("/v1/conversation/sessions", "POST", {"title": "x"},
                    auth, expected=400)

            project = request("/v1/conversation/sessions", "POST",
                              {"title": "GeoShift launch"}, auth)["id"]
            personal = request("/v1/conversation/sessions", "POST",
                               {"title": "Personal plans"}, auth)["id"]
            assert len(request("/v1/conversation/sessions", header=view)["sessions"]) == 3

            def add(session_id, content, client_id=None):
                return request("/v1/conversation/messages", "POST", {
                    "sessionId": session_id,
                    "clientMessageId": client_id or str(uuid4()),
                    "content": content
                }, auth)

            base = add(main_id, "Main stays separate")
            assert base["outcome"] == "saved"
            first_id = str(uuid4())
            first = add(project, "Launch milestone one", first_id)
            again = add(project, "Launch milestone one", first_id)
            assert first["id"] == again["id"] and again["outcome"] == "duplicate"
            request("/v1/conversation/messages", "POST", {
                "sessionId": personal, "clientMessageId": first_id,
                "content": "Launch milestone one"
            }, auth, expected=409)  # Reuse of a client ID across sessions forbidden.

            second = add(project, "Launch milestone two")
            third = add(project, "Launch milestone three")
            assert all(x["outcome"] == "saved" for x in (second, third))
            route = "/v1/conversation/messages?sessionId=" + project
            page1 = request(route + "&after=0&limit=2", header=view)
            assert [x["sequence"] for x in page1["messages"]] == [1, 2]
            assert page1["hasMore"] and page1["nextAfter"] == 2
            assert page1["highWatermark"] == 3
            page2 = request(route + "&after=2&limit=2", header=view)
            assert [x["sequence"] for x in page2["messages"]] == [3]
            assert not page2["hasMore"] and page2["nextAfter"] == 3
            assert all(x["sessionId"] == project for x in page1["messages"]+page2["messages"])
            assert request("/v1/conversation/messages", header=view)["messages"][0]["content"] == "Main stays separate"
            request("/v1/conversation/messages?limit=101", header=view, expected=400)
            request("/v1/conversation/messages?after=-1", header=view, expected=400)
            request("/v1/conversation/messages?sessionId=" + str(uuid4()),
                    header=view, expected=404)

            reply = {
                "inReplyToId": first["id"], "expectedOwnerText": "Launch milestone one",
                "content": "Synthetic Archer answer in the matching session"
            }
            response = request("/internal/conversation/reply", "POST", reply,
                               {"X-Archer-Bridge-Key": os.environ["ARCHER_GATEWAY_BRIDGE_KEY"]})
            assert response["outcome"] == "saved"
            merged = request(route + "&after=3&limit=10", header=view)
            assert len(merged["messages"]) == 1
            assert merged["messages"][0]["sequence"] == 4
            assert merged["messages"][0]["replyToMessageId"] == first["id"]
            assert merged["messages"][0]["role"] == "assistant"
            assert merged["highWatermark"] == 4

            request("/v1/conversation/sessions/" + project + "/archive", "POST",
                    {}, view, expected=401)
            request("/v1/conversation/sessions/" + main_id + "/archive", "POST",
                    {}, auth, expected=400)
            request("/v1/conversation/sessions/" + project + "/archive", "POST",
                    {}, auth)
            request("/v1/conversation/messages", "POST", {
                "sessionId": project, "clientMessageId": str(uuid4()),
                "content": "Should be blocked"
            }, auth, expected=409)
            assert request(route, header=view)["archived"] is True
            add(personal, "Another session is still open")
            assert request("/v1/conversation/messages?sessionId=" + personal,
                           header=view)["messages"][0]["sequence"] == 1

            persisted = (directory / "mobile-gateway-state.json").read_text()
            assert "Launch milestone one" not in persisted
            assert "conversationSessions" not in persisted
            old_cursor = request(route, header=view)["highWatermark"]
        finally:
            stop(process)

        process = run_gateway(directory, key)
        try:
            reader_result = request("/v1/conversation/sessions", header=view)
            assert len(reader_result["sessions"]) == 3
            restored = request("/v1/conversation/messages?sessionId=" + project,
                               header=view)
            assert restored["highWatermark"] == old_cursor == 4
            assert [x["sequence"] for x in restored["messages"]] == [1,2,3,4]
            assert restored["archived"]
        finally:
            stop(process)


    # Simulate a phone reconnecting after two messages have expired.
    # Seed a recognizable synthetic legacy file and let the actual gateway
    # migrate/encrypt it: no model, phone, APNs, or live user data.
    with tempfile.TemporaryDirectory(prefix="archer-retention-gap-ci-") as location:
        directory = Path(location)
        now = datetime.now(timezone.utc)
        old = (now - timedelta(days=10)).isoformat()
        recent = (now - timedelta(minutes=2)).isoformat()
        entries = []
        for position, (content, timestamp) in enumerate([
            ("Expired 1", old), ("Expired 2", old),
            ("Retained 3", recent), ("Retained 4", recent)
        ], start=1):
            entries.append({
                "id": str(uuid4()),
                "clientMessageId": str(uuid4()),
                "deviceId": str(uuid4()),
                "role": "owner",
                "content": content,
                "createdAtUtc": timestamp,
                "sequence": position
            })
        expired_session = str(uuid4())
        for position in (1, 2):
            entries.append({
                "id": str(uuid4()),
                "clientMessageId": str(uuid4()),
                "deviceId": str(uuid4()),
                "role": "owner",
                "sessionId": expired_session,
                "content": "Expired-only synthetic message",
                "createdAtUtc": old,
                "sequence": position
            })
        state_path = directory / "mobile-gateway-state.json"
        state_path.write_text(json.dumps({
            "devices": [], "events": [],
            "conversationSessions": [{
                "id": expired_session,
                "title": "Expired synthetic session",
                "createdAtUtc": recent,
                "lastSequence": 2
            }],
            "conversationMessages": entries
        }), encoding="utf-8")
        process = run_gateway(directory, secrets.token_hex(32))
        try:
            admin = {"X-Archer-Admin-Key": ADMIN}
            enrolled = request("/internal/devices/enroll", "POST", {
                "label": "Reconnecting test iPhone",
                "scopes": ["conversation:read"]
            }, admin)
            viewer = {"Authorization": "Bearer " + enrolled["deviceToken"]}
            full = request("/v1/conversation/messages?after=0&limit=2", header=viewer)
            assert full["oldestAvailableSequence"] == 3
            assert full["historyGapDetected"] is True
            assert full["missingMessageCountBeforePage"] == 2
            assert [x["sequence"] for x in full["messages"]] == [3, 4]
            assert full["highWatermark"] == 4
            assert "no longer available" in full["note"]

            resumed = request("/v1/conversation/messages?after=2", header=viewer)
            assert resumed["historyGapDetected"] is False
            assert resumed["missingMessageCountBeforePage"] == 0
            assert [x["sequence"] for x in resumed["messages"]] == [3, 4]
            last = request("/v1/conversation/messages?after=3", header=viewer)
            assert [x["sequence"] for x in last["messages"]] == [4]
            current = request("/v1/conversation/messages?after=4", header=viewer)
            assert current["messages"] == []
            assert not current["historyGapDetected"]
            page = request("/v1/conversation/messages?after=0&limit=1", header=viewer)
            assert page["hasMore"] is True and page["nextAfter"] == 3
            assert page["historyGapDetected"] is True

            # Wholly expired session needs a gap warning AND forward
            # progress: clients must not poll the same empty page forever.
            expired_page = request(
                "/v1/conversation/messages?sessionId=" + expired_session +
                "&after=0&limit=5", header=viewer
            )
            assert expired_page["messages"] == []
            assert expired_page["historyGapDetected"] is True
            assert expired_page["oldestAvailableSequence"] == 3
            assert expired_page["missingMessageCountBeforePage"] == 2
            assert expired_page["highWatermark"] == 2
            assert expired_page["nextAfter"] == 2
            assert not expired_page["hasMore"]
            caught_up = request(
                "/v1/conversation/messages?sessionId=" + expired_session +
                "&after=2&limit=5", header=viewer
            )
            assert not caught_up["historyGapDetected"]
            assert caught_up["nextAfter"] == 2
            assert caught_up["messages"] == []

            # In-place migration was encrypted and preserved IDs/cursors.
            encrypted = state_path.read_text(encoding="utf-8")
            assert "Expired 1" not in encrypted
            assert "Retained 4" not in encrypted
            assert "archer-gateway-aes256gcm-v1" in encrypted
        finally:
            stop(process)

    print("PASS: session segregation, monotonic cursors, paged sync, replay, archive and encrypted restart and explicit retention gap reporting")


if __name__ == "__main__":
    main()
