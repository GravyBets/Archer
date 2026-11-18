#!/usr/bin/env python3
"""Static smoke test for Archer's browser UI; requires only the Python standard library."""
from html.parser import HTMLParser
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1] / "Archer.MissionControl" / "wwwroot"
HTML = (ROOT / "index.html").read_text(encoding="utf-8")
JS = (ROOT / "js" / "app.js").read_text(encoding="utf-8")
HOME_JS = (ROOT / "js" / "archer-home.js").read_text(encoding="utf-8")
DEVELOPER_JS = (ROOT / "js" / "developer.js").read_text(encoding="utf-8")
OPERATOR_JS = (ROOT / "js" / "operator-context.js").read_text(encoding="utf-8")
SPEND_JS = (ROOT / "js" / "spending.js").read_text(encoding="utf-8")
CODING_JS = (ROOT / "js" / "coding.js").read_text(encoding="utf-8")
COMMUNICATIONS_JS = (ROOT / "js" / "communications.js").read_text(encoding="utf-8")
INBOX_JS = (ROOT / "js" / "inbox.js").read_text(encoding="utf-8")
MOBILE_DRAFTS_JS = (ROOT / "js" / "mobile-drafts.js").read_text(encoding="utf-8")
MOBILE_SESSIONS_JS = (ROOT / "js" / "mobile-sessions.js").read_text(encoding="utf-8")
FIELD_JS = (ROOT / "js" / "orb-field.js").read_text(encoding="utf-8")
CSS = (ROOT / "css" / "app.css").read_text(encoding="utf-8")


class Inventory(HTMLParser):
    def __init__(self):
        super().__init__()
        self.ids = []
        self.views = []
        self.scripts = []

    def handle_starttag(self, tag, attrs):
        props = dict(attrs)
        if props.get("id"):
            self.ids.append(props["id"])
        if props.get("data-view"):
            self.views.append(props["data-view"])
        if tag == "script" and props.get("src"):
            self.scripts.append(props["src"])


page = Inventory()
page.feed(HTML)
assert len(page.ids) == len(set(page.ids)), "Duplicate HTML element IDs detected"

expected_views = {"archer", "missions", "developer", "operations", "inbox", "attention", "activity"}
assert expected_views == set(page.views), f"Unexpected navigation: {page.views}"
for view in expected_views:
    assert f"{view}View" in page.ids, f"Missing {view} view"
assert "archerView" in HTML and 'id="archerView" class="view archer-home"' in HTML
assert 'id="missionsView" class="view hidden"' in HTML
assert 'data-state="idle"' in HTML

required = {
    "archerPresence", "orbButton", "orbParticleField", "presenceStatus", "presenceActiveMissions",
    "presenceTasksToday", "presenceAttentionCount", "chatMessages", "chatForm",
    "chatText", "voiceInputButton", "voiceReadToggle", "voiceReplayButton",
    "realtimeVoiceStart", "realtimeVoiceEnd", "realtimeVoiceStatus",
    "voiceStopButton", "voiceSupportNote", "voiceChoiceSelect", "voiceRateSlider", "voiceRateValue",
    "voiceHandsFreeToggle", "voicePauseSlider", "voicePauseValue", "voiceEndConversation",
    "voiceAccentStatus", "missionList", "missionDetail",
    "usageMonthCost", "usageLifetimeCost", "attentionFailures", "attentionIssueCount",
    "developerRepository", "developerLoad", "developerFiles", "developerFilter",
    "developerSelectedCount", "developerPreviewButton", "developerReviewButton",
    "developerBranch", "developerTree", "developerPreview", "developerPreviewMeta", "developerStatus",
    "ownerPresenceStatus", "ownerPresenceActivity", "ownerPresencePlace",
    "ownerPresenceDuration", "ownerPresenceShareAi", "ownerPresenceSaveButton",
    "ownerPresenceClearButton", "ownerPresenceLocationButton", "ownerPresenceLocationStatus",
    "operationsMonitorList", "operationsIncidentList", "operationsAddForm",
    "operationsSiteName", "operationsSiteUrl", "operationsStatus", "operationsIncidentCount",
    "briefingHeading", "briefingRefreshButton", "briefingGeneratedAt", "briefingCopyButton",
    "briefingBody", "briefingMetricNotes", "briefingQuietStatus", "briefingAlertList",
    "briefingPreferencesForm", "briefingTimezone", "briefingHour", "briefingQuietStart",
    "briefingQuietEnd", "briefingCriticalOverride", "briefingPreferenceStatus",
    "spendStatus", "spendCounted", "spendRemaining", "spendUncertain",
    "spendPaused", "spendMonthly", "spendPerRequest", "spendSettingsForm",
    "spendSaveButton", "spendPauseIndicator",
    "codingHeading", "codingInstruction", "codingGenerateButton",
    "codingStatus", "codingProposalPreview", "codingPublishStatus",
    "codingPublishButton",
    "communicationHeading", "communicationsProviderStatus", "communicationsDestination",
    "communicationsSmsTest", "communicationsCallTest", "communicationsTestStatus",
    "communicationsPreferencesForm", "communicationsDailySms",
    "communicationsUrgentCalls", "communicationsSaveButton",
    "communicationsSaveStatus", "communicationsAttempts", "mobileHeading",
    "mobileIngestStatus", "mobileIngestDescription",
    "inboxNavCount", "inboxTitle", "inboxFilter", "inboxTotals", "inboxItems", "inboxRefreshButton",
    "mobileDraftHeading", "mobileDraftConnection", "mobileDraftNote",
    "mobileDraftRefresh", "mobileDraftItems", "mobileDraftShareReply",
    "mobileSessionHeading", "mobileSessionSelect", "mobileSessionRefresh",
    "mobileSessionStatus", "mobileSessionMessages", "mobileSessionMore"
}
assert required.issubset(page.ids), f"Missing expected IDs: {required.difference(page.ids)}"

# Only check literal calls. Dynamically generated IDs are intentionally excluded.
literal_id_pattern = re.compile(r"(?:\$\(|\bel\()\s*'([A-Za-z][A-Za-z0-9-]+)'\s*\)")
referred_ids = set(literal_id_pattern.findall(JS + "\n" + HOME_JS + "\n" + FIELD_JS + "\n" + DEVELOPER_JS + "\n" + OPERATOR_JS + "\n" + SPEND_JS + "\n" + CODING_JS + "\n" + COMMUNICATIONS_JS + "\n" + INBOX_JS + "\n" + MOBILE_DRAFTS_JS + "\n" + MOBILE_SESSIONS_JS))
# Created dynamically in the mission-list empty state.
dynamic_ids = {"starterMission"}
missing = referred_ids.difference(page.ids).difference(dynamic_ids)
assert not missing, f"JavaScript references missing DOM IDs: {missing}"

assert page.scripts[-12:] == ["/js/app.js", "/js/archer-home.js", "/js/realtime-voice.js", "/js/orb-field.js", "/js/developer.js", "/js/operator-context.js", "/js/spending.js", "/js/coding.js", "/js/communications.js", "/js/inbox.js", "/js/mobile-drafts.js", "/js/mobile-sessions.js"]
assert "/api/mobile/sessions" in MOBILE_SESSIONS_JS
assert "/api/mobile/session-history" in MOBILE_SESSIONS_JS
assert "textContent" in MOBILE_SESSIONS_JS
assert "api('/api/chat'" not in MOBILE_SESSIONS_JS
assert "/api/mobile/conversation-preview" in MOBILE_DRAFTS_JS
assert "/api/mobile/reply-publish" in MOBILE_DRAFTS_JS
assert "window.confirm(" in MOBILE_DRAFTS_JS
assert "window.ArcherMobileDrafts?.chatCompleted(mobileSource)" in JS
assert "pendingShare" in MOBILE_DRAFTS_JS
assert "composer.value = message.content" in MOBILE_DRAFTS_JS
assert "api('/api/chat'" not in MOBILE_DRAFTS_JS
assert 'api("/api/chat"' not in MOBILE_DRAFTS_JS
assert "fetch('/api/chat'" not in MOBILE_DRAFTS_JS
assert 'fetch("/api/chat"' not in MOBILE_DRAFTS_JS
assert "/api/inbox" in INBOX_JS
assert "acknowledge" in INBOX_JS
assert "textContent" in INBOX_JS
assert "/api/communications" in COMMUNICATIONS_JS
assert "/api/mobile/capabilities" in COMMUNICATIONS_JS
assert "window.confirm(" in COMMUNICATIONS_JS
assert "/api/development/code-proposals" in CODING_JS
assert "window.confirm(" in CODING_JS
assert "publish" in CODING_JS
assert "/api/spending" in SPEND_JS
assert "/api/presence" in OPERATOR_JS
assert "/api/operations" in OPERATOR_JS
assert "/api/briefing" in OPERATOR_JS
assert "/api/briefing/preferences" in OPERATOR_JS
assert "briefingText" in OPERATOR_JS
assert "getCurrentPosition(" in OPERATOR_JS
assert "watchPosition(" not in OPERATOR_JS
assert "/api/development/repository" in DEVELOPER_JS
assert "/api/development/preview" in DEVELOPER_JS
assert "SOURCE FILES:" in DEVELOPER_JS
assert "showModal()" in DEVELOPER_JS
assert "showView('archer');" in JS
assert "window.ArcherPresence?.update(appState.data)" in JS
assert "window.ArcherPresence?.reply(result.answer)" in JS
assert (ROOT / "js" / "realtime-voice.js").is_file()
REALTIME_JS = (ROOT / "js" / "realtime-voice.js").read_text(encoding="utf-8")
assert "getUserMedia" in REALTIME_JS and "RTCPeerConnection" in REALTIME_JS
assert "Start realtime conversation" in HTML
assert "window.confirm(" in REALTIME_JS
assert "stopSession(" in REALTIME_JS
assert "getTracks().forEach(track => track.stop())" in REALTIME_JS
assert "document.addEventListener('visibilitychange'" in REALTIME_JS
assert "/api/realtime-voice/call" in REALTIME_JS
assert "const SESSION_LIMIT_MS = 4 * 60 * 1000" in REALTIME_JS
assert "Authorization:" not in REALTIME_JS and "Bearer " not in REALTIME_JS

# Guard the server's SDP gate against accidentally comparing literal
# backslash-r/backslash-n text instead of WebRTC's real CRLF/LF line endings.
# This previously made every legitimate browser offer fail before the API call.
REALTIME_COORDINATOR = (ROOT.parent / "Services" / "RealtimeVoiceCoordinator.cs").read_text(encoding="utf-8")
for expected_sdp_literal in (
    r'offer.StartsWith("v=0\r\n"',
    r'offer.StartsWith("v=0\n"',
    r'offer.Contains("\nm=audio "',
    r'offer.Contains("\r\nm=audio "',
    r'body.StartsWith("v=0\r\n"',
    r'body.StartsWith("v=0\n"',
):
    assert expected_sdp_literal in REALTIME_COORDINATOR, (
        f"Expected real SDP line ending validator missing: {expected_sdp_literal}"
    )
for invalid_sdp_literal in (
    r'"v=0\\r\\n"', r'"v=0\\n"', r'"\\nm=audio "', r'"\\r\\nm=audio "'
):
    assert invalid_sdp_literal not in REALTIME_COORDINATOR, (
        f"SDP validator wrongly matches a literal backslash sequence: {invalid_sdp_literal}"
    )

assert "SpeechRecognitionCtor" in HOME_JS
assert "speechSynthesis" in HOME_JS
assert "prefers-reduced-motion" in CSS
assert "prefers-reduced-motion" in FIELD_JS
assert "requestAnimationFrame" in FIELD_JS
assert "IntersectionObserver" in FIELD_JS
assert "visibilitychange" in FIELD_JS
assert "getContext" in FIELD_JS
assert "pointer-events:none" in CSS
assert '[data-state="listening"]' in CSS
assert '[data-state="thinking"]' in CSS
assert '[data-state="speaking"]' in CSS

print(f"PASS: {len(page.ids)} unique IDs, all literal JS element references and presence states")

assert "getVoices()" in HOME_JS
assert "voicePicker.addEventListener" in HOME_JS
assert "archer.voice.rate" in HOME_JS
assert "utterance.rate = Math.min(1.65, Math.max(0.80" in HOME_JS
assert 'id="voiceRateSlider"' in HTML
assert 'value="1.25"' in HTML
assert 'id="voiceRateValue"' in HTML
assert 'id="voiceHandsFreeToggle"' in HTML
assert 'id="voicePauseSlider"' in HTML
assert 'max="30" step="1" value="7"' in HTML
assert 'id="voicePauseValue"' in HTML
assert 'id="voiceEndConversation"' in HTML
assert "voiceSessionActive = handsFreeToggle.checked" in HOME_JS
assert "recognition.continuous = voiceSessionActive" in HOME_JS
assert "recognitionRestarts >= 40" in HOME_JS
assert "sendAfterRecognitionEnd = true" in HOME_JS
assert "el('chatForm').requestSubmit()" in HOME_JS
assert "window.ArcherPresence?.prepareSend?.()" in JS
assert "JSON.stringify({ message, voiceMode })" in JS
assert "document.addEventListener('visibilitychange'" in HOME_JS

assert "en-gb" in HOME_JS.lower()
