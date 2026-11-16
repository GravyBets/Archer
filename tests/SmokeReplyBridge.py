#!/usr/bin/env python3
"""Verify manually initiated desktop -> encrypted gateway reply sharing.

Both services are local and CI data is synthetic. No model key, Twilio,
APNs account or actual phone is used. The desktop state is pre-seeded
with a representative, not genuinely AI-generated, conversation pair.
"""
import json
import os
import time
from urllib.error import HTTPError
from urllib.request import Request, urlopen


def req(url, method="GET", data=None, headers=None, expected=200):
    payload = json.dumps(data).encode("utf-8") if data is not None else None
    request = Request(url, method=method, data=payload,
                      headers=dict({"Content-Type": "application/json"}, **(headers or {})))
    # The prior gateway security smoke deliberately exercises many API calls,
    # and the app correctly enforces 90 loopback requests/minute. Preserve the
    # production limiter: let its window reset instead of disabling protection.
    for attempt in range(2):
        try:
            with urlopen(request, timeout=8) as response:
                status, body = response.status, response.read()
        except HTTPError as error:
            status, body = error.code, error.read()
        if status == 429 and attempt == 0:
            time.sleep(61)
            continue
        assert status == expected, (url, expected, status, body[:250])
        return json.loads(body) if body else {}
    raise AssertionError("Gateway rate limit did not recover")


DESKTOP = "http://127.0.0.1:5087"
GATEWAY = "http://127.0.0.1:5091"
key = os.environ["ARCHER_GATEWAY_BRIDGE_KEY"]
admin = os.environ["ARCHER_GATEWAY_ADMIN_KEY"]
header = {"X-Archer-Bridge-Key": key}

preview = req(DESKTOP + "/api/mobile/conversation-preview")
assert preview["connected"] and preview["enabled"], preview
assert len(preview["messages"]) == 1, preview
draft = preview["messages"][0]
assert draft["content"] == "CI_PHONE_DRAFT_READY_FOR_DESKTOP_REVIEW"
data = {"gatewayMessageId": draft["id"], "question": draft["content"]}

# Both desktop and gateway session inspection require the separate bridge key.
req(GATEWAY + "/internal/conversation/sessions", expected=403)
req(GATEWAY + "/internal/conversation/history", expected=403)
catalog = req(DESKTOP + "/api/mobile/sessions")
assert catalog["enabled"] and catalog["connected"], catalog
assert len(catalog["sessions"]) == 1, catalog
main_id = catalog["defaultSessionId"]
history_path = (DESKTOP + "/api/mobile/session-history?sessionId=" +
                main_id + "&after=0&limit=20")
direct_history = req(GATEWAY + "/internal/conversation/history?sessionId=" +
    main_id + "&after=0&limit=20", headers=header)
assert len(direct_history["messages"]) == 1
# Earlier synthetic tests delete older records but preserve server cursor
# high-water marks. A retained first message is NOT necessarily sequence 1.
first_sequence = direct_history["messages"][0]["sequence"]
assert first_sequence >= 1
before = req(history_path)
assert before["enabled"] and before["connected"], before
assert len(before["messages"]) == 1, before
assert before["messages"][0]["content"] == draft["content"]
assert before["messages"][0]["role"] == "owner"
assert before["nextAfter"] == before["messages"][0]["sequence"]
assert before["highWatermark"] == before["nextAfter"]
req(DESKTOP + "/api/mobile/session-history?sessionId=" + main_id +
    "&after=-1", expected=400)


# No arbitrary question or guessed source ID can publish a reply.
wrong = dict(data)
wrong["question"] = "Unrelated desktop text"
denied = req(DESKTOP + "/api/mobile/reply-publish", "POST", wrong, expected=409)
assert denied["shared"] is False
assert len(req(GATEWAY + "/internal/conversation/bridge", headers=header)["messages"]) == 1

# The desktop's last saved user + assistant pair is the only candidate.
shared = req(DESKTOP + "/api/mobile/reply-publish", "POST", data)
assert shared["shared"] is True
assert "No push" in shared["message"]

# Same request is idempotent; it must not make a new AI call or reply.
repeated = req(DESKTOP + "/api/mobile/reply-publish", "POST", data)
assert repeated["shared"] is True
assert req(GATEWAY + "/internal/conversation/bridge", headers=header)["messages"] == []

after = req(history_path)
assert after["connected"] and after["enabled"], after
assert len(after["messages"]) == 2, after
assert [x["sequence"] for x in after["messages"]] == [first_sequence, first_sequence + 1], after
assert after["messages"][1]["role"] == "assistant"
assert after["messages"][1]["replyToMessageId"] == draft["id"]
assert "Synthetic Archer reply" in after["messages"][1]["content"]
assert after["highWatermark"] == first_sequence + 1 and not after["hasMore"]
cursor_path = (DESKTOP + "/api/mobile/session-history?sessionId=" + main_id +
               "&after=" + str(first_sequence) + "&limit=1")
only_reply = req(cursor_path)
assert len(only_reply["messages"]) == 1
assert only_reply["messages"][0]["sequence"] == first_sequence + 1
assert not only_reply["hasMore"]

audit = req(GATEWAY + "/internal/audit", headers={"X-Archer-Admin-Key": admin})
# The gateway's preceding security smoke explicitly published one synthetic
# assistant reply. This separate desktop test must add exactly one more.
assert sum(x["action"] == "conversation.assistant_reply_shared" for x in audit["entries"]) == 2
assert "Synthetic Archer reply" not in json.dumps(audit)
print("PASS: owner-initiated desktop-to-mobile reply stored once; no auto AI, phone or push")
