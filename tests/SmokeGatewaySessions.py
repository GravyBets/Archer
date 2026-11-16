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

    print("PASS: session segregation, monotonic cursors, paged sync, replay, archive and encrypted restart")


if __name__ == "__main__":
    main()
