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
    "voiceStopButton", "voiceSupportNote", "voiceChoiceSelect", "voiceAccentStatus", "missionList", "missionDetail",
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
    "inboxNavCount", "inboxTitle", "inboxFilter", "inboxTotals", "inboxItems", "inboxRefreshButton"
}
assert required.issubset(page.ids), f"Missing expected IDs: {required.difference(page.ids)}"

# Only check literal calls. Dynamically generated IDs are intentionally excluded.
literal_id_pattern = re.compile(r"(?:\$\(|\bel\()\s*'([A-Za-z][A-Za-z0-9-]+)'\s*\)")
referred_ids = set(literal_id_pattern.findall(JS + "\n" + HOME_JS + "\n" + FIELD_JS + "\n" + DEVELOPER_JS + "\n" + OPERATOR_JS + "\n" + SPEND_JS + "\n" + CODING_JS + "\n" + COMMUNICATIONS_JS + "\n" + INBOX_JS))
# Created dynamically in the mission-list empty state.
dynamic_ids = {"starterMission"}
missing = referred_ids.difference(page.ids).difference(dynamic_ids)
assert not missing, f"JavaScript references missing DOM IDs: {missing}"

assert page.scripts[-9:] == ["/js/app.js", "/js/archer-home.js", "/js/orb-field.js", "/js/developer.js", "/js/operator-context.js", "/js/spending.js", "/js/coding.js", "/js/communications.js", "/js/inbox.js"]
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
assert "en-gb" in HOME_JS.lower()
