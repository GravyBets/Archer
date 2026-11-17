# Archer — Mobile, Messaging, Voice & Safe Business Operations Roadmap

Working plan: 2026-11-16. Target: one Archer across Windows, iPhone, text, phone, and (if approved) CarPlay.
Current baseline: Archer v1.13.0 is LOCALHOST-ONLY. It supports guarded AI, source review/code drafts, site checks, opt-in outbound SMS/one-way calls and manually selected coarse presence. Native iOS, real push, inbound voice/text conversation and cloud hosting DO NOT exist yet.

## Product decisions

- App first, phone number as backup. iPhone is the primary chat, voice, business dashboard, alerts and approval surface. Phone/SMS cover hands-free or fallback access.
- One consistent Archer personality, conversation history, identity and audit trail across Windows, phone and iOS.
- Conversational style: concise, natural, grounded, British male voice after an actual listening test; optional longer technical explanations.
- Driving safety: brief spoken alerts for truly urgent, verified issues; no complex approval or code diff while driving. CarPlay is entitlement-dependent, not guaranteed.
- Privacy and permission: no passive GPS tracking, background listening or unsolicited messages; user control over device permissions, retention, quiet hours, deletion and contacts.
- GeoShift launch remains the income-generating priority. We do not expand Archer autonomy at the expense of sellable, reliable GeoShift.

## Milestones and actual to-do checklist

### P0: Security architecture (HARD GATE for public/mobile exposure)

- [x] Local-only dashboard with disabled-by-default GitHub publishing, Twilio and mobile ingress.
- [x] Local AI API spending reservation, per-request limit and pause control.
- [x] Inventory all 37 existing MissionControl routes, mutating endpoints and credential risks in [mobile security review](ARCHER_MOBILE_SECURITY_REVIEW.md).
- [ ] Independently verify admin endpoint hardening and broader dependencies before production.
- [x] Draft a separate [remote gateway security architecture](ARCHER_REMOTE_GATEWAY_SECURITY_DESIGN.md) with boundary, identity, proxy, retention, queue and deployment acceptance gates. **Draft only; no independent approval or public deployment.**
- [x] Document local/public threat model, token risks, abuse, CSRF, rate limits, provider leakage and hard production blockers in [mobile security review](ARCHER_MOBILE_SECURITY_REVIEW.md).
- [x] **Local gateway prototype:** separate project and process bound to 127.0.0.1:5091; limited authenticated inbox, independent storage and no admin/AI/GitHub/Twilio routes (v1.2).
- [ ] Harden and deploy a **separate authenticated public/mobile gateway** behind TLS without exposing today's unauthenticated dashboard or port 5087.
- [x] Initial owner-enrolled random device tokens with hash-only storage, read/ack scopes, 1–7 day expiry and immediate admin revocation (v1.2 local).
- [x] Local admin-only device token rotation invalidating old tokens, read-only versus read/ack scope selection, per-device rate limiting, redacted device event audit and revoke tests (v1.4).
- [ ] Secure **remote** pairing, owner identity/login, production refresh/rotation protocol, device audit export, stolen-device recovery and physical-device keychain integration.
- [x] Local gateway enforces loopback, restrictive Host/Origin, scoped read/ack tokens and layered global/per-device rate limits (v1.4).
- [ ] Production HTTPS, trusted ingress, secure secret management, outbound caps, input validation review and per-identity distributed rate limits.
- [x] AES-256-GCM authenticated encryption for the entire local mobile gateway state (inbox, conversation journal, device hashes and audit); migration and wrong/missing key protection in CI (v1.6).
- [x] v1.11 offline local **AES-GCM verified backup/verify/restore-new utility**, tamper/key-loss detection and synthetic restore drill in CI
- [x] v1.12 safe offline **rotate-new** command re-encrypts a separate verified file with a distinct key; refuses gateway-online rotation, overwrite, missing/equal/wrong keys and tampered state in CI.; existing files cannot be overwritten.
- [ ] Configure real encrypted **off-host** backups for your personal home installation, retain your secret key separately, document key rotation, complete a real Windows restore drill and add health/cost alerts. Automated CI is not production backup coverage.
- [x] Add isolated gateway CI rejecting unauthenticated access, enforcing device scopes, revocation and bounded inbox payloads; gateway has no mission/call/code/GPS endpoint.
- [ ] Independent penetration test, HTTPS/reverse proxy, mobile client threat review and production audit.

DONE WHEN: independent security review/tests pass, remote endpoint is separate from admin, and deleted/revoked devices fail closed.

### P1: Shared operator inbox / alerts (STARTED IN v1.1)

- [x] Define source-grounded alert with stable source ID, category, priority, time, evidence and read/acknowledge status.
- [x] Create local inbox from existing website incidents and failed mission tasks; deduplicate across reopens/restarts.
- [x] Add local inbox API and PC dashboard for read / acknowledge with bounded history, zero paid provider calls.
- [ ] Connect verified GeoShift app errors, payments, subscription billing, deployments, security and customer support via explicitly authorised adapters.
- [ ] Distinguish observed data from inference, stale checks, suspected outage and verified incident.
- [x] Initial opt-in loopback-only source-event sync into mobile gateway, with idempotent source keys and no paid delivery (v1.2).
- [ ] Create durable **remote delivery** outbox with idempotency, retry budget, receipt tracking, dead-letter state and owner override. The v1.3 simulator is **not** a production outbox.
- [x] Add explicit, idempotent notification-route sandbox previews with zero real push/SMS/calls, including device-scoped preview API and CI (v1.3).
- [ ] Test duplicate, recovery, reordering, reconnect, stale checks and shutdown cases.

DONE WHEN: each issue has one durable actionable item, no duplicate paid alert, reviewable evidence and no unapproved action.

### P2: Unified conversation and natural British voice

- [x] v1.5 local mobile-gateway **conversation journal prototype** with device-scoped read/write permissions, stable per-device client message IDs, idempotent writes, seven-day read retention, bounded storage and admin-only clear. **Not connected to Archer desktop chat or the AI.**
- [x] v1.7 separately keyed, read-only **localhost** phone-draft bridge to Windows Archer, with explicit owner enable flag, preview UI, manual copy-to-composer and no automatic model calls.
- [x] v1.9 encrypted local gateway **session IDs**, monotonically assigned per-session sequence cursors, 1–100-message incremental pages, archive/read-only policy and migration of v1.8 messages.
- [ ] Define the full production multi-channel conversation service (account identity, remote sessions, encrypted backups, export/deletion, cross-channel lifecycle, message consistency and desktop/phone history integration).
- [x] v1.8 manual owner-approved assistant-reply publication from desktop to encrypted gateway, idempotent and linked to a phone-authored draft; NO automatic model call or public access.
- [x] v1.10 opt-in desktop **read-only session browser**: lists local encrypted gateway sessions, previews owner/Archer messages and pages using server cursors; no AI or phone activity.
- [x] v1.13 per-session incremental reads disclose expired-history gaps, earliest retained sequence and a safe continuation cursor when all older messages are gone; encrypted synthetic CI validates reconnect behavior.
- [ ] Connect Windows and phone chat to the same authenticated production session backend with proper multi-device ordering, privacy, reply streaming and authenticated internet access.
- [ ] Stream short responses by default, allow detailed mode when requested, and retain clear action boundaries.
- [ ] Audition British male neural voices for naturalness, latency and price; choose a voice only after a live test.
- [ ] Implement push-to-talk, optional hands-free, barge-in/interrupt, speech playback stop, dropped-call recovery and transcript boundaries.
- [ ] Never make irreversible decisions from an ambiguous spoken confirmation, especially during driving.

DONE WHEN: phone and app feel like one Archer, with stable session history and interruptible natural speech.

### P3: Native iPhone app MVP

- [ ] Confirm Mac/Xcode access and Apple developer account/team and bundle ID.
- [ ] Build SwiftUI app: Archer orb, typed chat, streaming replies, push-to-talk and spoken responses.
- [ ] Secure account sign-in, token storage in Keychain, device enrollment/revocation and local biometric unlock when appropriate.
- [ ] APNs permission screen; sandbox/production token registration and rotation; no tokens in logs.
- [ ] Add push notification inbox, read/ack controls, mission/GeoShift summary, and non-sensitive lock-screen preview.
- [ ] Add offline snapshots, safe reconnect, error states and sync conflict handling.
- [ ] Support VoiceOver, Dynamic Type, dark mode, reduced motion and battery limits.
- [ ] TestFlight trial, privacy disclosure, security review and device testing.

DONE WHEN: an authorized iPhone can securely receive a test notification, read and acknowledge an item, and chat with Archer.

### P4: Driving / work / shopping awareness

- [ ] Start with user-selected state: Driving, Working, Shopping, Busy, Available, with short TTL.
- [ ] Optionally add consented iOS activity / location signals as permitted by OS; no hidden history or raw GPS trail.
- [ ] Never assume Focus mode status is readable or notifications bypass it; offer explicit manual fallback.
- [ ] Alerts: Information -> digest; Attention -> push/brief; Urgent verified -> approved hands-free call.
- [ ] Integrate local time zone, quiet hours, call preference, maximum daily interruptions and owner acknowledgment.
- [ ] While driving, defer code merges, deployments, money transfers and complex approvals until safely stopped.
- [ ] Verify Bluetooth call fallback in an actual vehicle.

DONE WHEN: the system safely adapts to declared/authorized context without demanding screen interaction while driving.

### P5: Dedicated phone number and two-way calls/text

- [x] v1.0 supports one-way, opt-in, explicitly configured Twilio test SMS and British synthetic alert calls.
- [ ] Get owner-approved dedicated number, regulatory messaging registration, rate/cost limits and provider security.
- [ ] Validate signatures for Twilio inbound SMS webhooks, reject unknown senders, rate-limit and de-duplicate.
- [ ] Connect inbound/outbound real-time voice using ConversationRelay or equivalent HTTPS/WSS backend.
- [ ] Validate Twilio signature on websocket handshake; implement session lifecycle, interruption, transcript and user consent.
- [ ] Reuse Archer chat memory; clearly distinguish conversation from commands and from approvals.
- [ ] Add provider receipts, missed call/voicemail handling, retry cancellation and auditing.
- [ ] Test live on the owner's approved phone with a strict low-cost limit.

DONE WHEN: owner can text or call Archer naturally, with one session, accurate receipts and no runaway charges.

### P6: Always-on cloud readiness

- [ ] Separate mobile gateway, background workers, credentialed admin, database and GeoShift production failure domains.
- [ ] Choose secure hosting and database/backup plans; do not migrate before P0 security gate.
- [ ] Deployment pipeline with staged rollout, health checks, rollback and owner-approved production deploy.
- [ ] Durable queues and scheduled jobs for delivered daily briefing and urgent escalation.
- [ ] Monitor voice, push, SMS, job queues, uptime, spend and missed incidents independently.

DONE WHEN: app restart/network interruption doesn't lose messages or duplicate paid calls, and safe rollback works.

### P7: CarPlay, later

- [ ] Apply for CarPlay voice-based conversation entitlement; eligibility/approval is not guaranteed.
- [ ] Test approved CarPlay app with simulator and real compatible head unit.
- [ ] Make interaction hands-free and distraction-limited: talk, hear a brief status, stop or defer.
- [ ] No code diffs, payment authorization or complex action approvals on CarPlay.
- [ ] Provide ordinary Bluetooth-call fallback for unsupported cars.

DONE WHEN: CarPlay use is compliant and safe; no need to touch the screen while driving.

### P8: GeoShift launch and measurable business operations

- [ ] Finish GeoShift KMZ/KML conversion quality, regression tests, onboarding, pricing and production sign-up.
- [ ] Add authorized billing/analytics/support event streams (actual data, timestamps, evidence).
- [ ] Send accurate owner briefings: uptime, subscriptions, revenue, refunds, conversions, support, AI/voice spend.
- [ ] Research -> source-specific change -> reviewable PR -> CI -> owner approval; no silent merge/deploy.
- [ ] Validate paying customers, retention, marketing and support before building the second SaaS.

DONE WHEN: GeoShift converts real paying customers and Archer reports genuine business facts, not guesses.

## Execution order

1. **Completed:** v1.1 inbox; v1.2 isolated gateway; v1.3 notification sandbox; v1.4 device security; v1.5 local journal; v1.6 encrypted state; v1.7 draft preview; v1.8 manual replies; v1.9 session cursors; v1.10 desktop session browser; v1.11 local encrypted backup/restore, v1.12 offline key rotation, v1.13 retention-safe cursor-gap warnings. No cloud exposure.
2. **Next:** independently review the proposed [remote gateway security design](ARCHER_REMOTE_GATEWAY_SECURITY_DESIGN.md), settle owner choices (identity, host, retention, iOS), then implement an isolated authenticated **TLS-ready gateway skeleton** and synthetic tests. No remote access until all security gates pass.
3. **Then:** shared conversation backend, push service (APNs) and native iOS MVP.
4. **In parallel after security:** Twilio inbound SMS + ConversationRelay with signature checks and strict budget.
5. **Before production:** deployment/restore drills, owner approval flows and provider receipts.
6. **After that:** driving-awareness improvements and CarPlay entitlement/application.

## Decisions for the owner when back at the PC

- [ ] Access to a Mac/Xcode for native iPhone builds and TestFlight; if unavailable, use mobile web prototype as interim.
- [ ] Apple developer account/team; iOS app ID and iPhone test device.
- [ ] Car capability (CarPlay vs regular Bluetooth, or both).
- [ ] Twilio number, provider plan, recipient allowlist and charges (no secrets in chat).
- [ ] Quiet hours, daily call/text limits, what is truly urgent, whether critical calls may interrupt.
- [ ] Location permissions off by default, data TTL and transcript retention choice.
- [ ] Combined monthly budget across hosting, model tokens, push, voice, SMS and backup.
- [ ] Production hosting timing (GeoShift beta or genuine 24/7 monitoring need).

## Provider verification (2026-11-16)

- Apple CarPlay voice-based conversation entitlement: https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.developer.carplay-voice-based-conversation
- Apple CarPlay app support / entitlement request: https://developer.apple.com/carplay/
- Apple APNs notification server: https://developer.apple.com/documentation/UserNotifications/sending-notification-requests-to-apns
- Twilio ConversationRelay: https://www.twilio.com/docs/voice/twiml/connect/conversationrelay
- Twilio signature verification for inbound ConversationRelay: https://www.twilio.com/docs/voice/conversationrelay/onboarding

The roadmap is not permission to make unauthorized GitHub changes, send paid calls/texts, track location, host publicly or deploy code.
### Implementation note — v1.2.0 (2026-11-16)

The separate Archer.MobileGateway project is a **local API prototype**, not an internet-facing mobile application. It currently authenticates scoped devices, supports revoke/read/ack and accepts opt-in deduplicated local event sync. It **does not** expose chat, personal GPS, admin settings, OpenAI tools or Twilio calls. CI tests authentication, invalid keys, revocation, ingest validation and event replay. The full security analysis and remaining blockers are in [ARCHER_MOBILE_SECURITY_REVIEW.md](ARCHER_MOBILE_SECURITY_REVIEW.md).

### Implementation note — v1.3.0 (2026-11-16)

The standalone gateway now has an admin-key-protected `POST /internal/delivery/simulate` endpoint and `GET /internal/delivery/preview`, plus scoped `GET /v1/delivery-previews` for an enrolled test device. **All routes are still localhost-only.** Simulations choose a noninterrupting push-preview or digest-preview, or suppress already acknowledged/resolved items. An unverified website outage is marked as requiring further verification and **cannot directly initiate a call**. Stable event/device/policy keys prevent repeat mock routes. CI verifies this with zero provider requests. This is intentionally not APNs, an actual SMS/call, a worker, a billing event or a remotely accessible app.

### v1.4.0 security milestone — local device lifecycle

- Administrator may enroll a read-only device (`inbox:read`) or the prior full inbox read/ack scope set. New scopes cannot grant chat, missions, GitHub, calls or any other permission.
- Admin-only token rotation immediately invalidates previous bearer, with a maximum seven-day new lifetime. Rotation of revoked or expired devices is denied; stale devices require fresh enrollment.
- New redacted audit entries identify enrolled/rotated/revoked device IDs, event ingestion/resolution, and which device read/acknowledged an inbox event, with a 300-entry cap. No raw tokens, text contents, phone numbers or locations in the audit feed.
- DNS rebinding Host headers are refused, alongside Origin restrictions and loopback-only binding. A second per-device window limit protects inbox reads and acknowledgments in addition to the existing IP limiter.
- **Still unfinished:** remotely trusted identity, secure iOS pairing, certificate-backed HTTPS, deployment, independently reviewed security, public ingress and actual pushes. No cloud expense.

### v1.5.0 local conversation storage (NOT live Archer chat)

A device may be enrolled with `conversation:read` or both `conversation:read` and `conversation:write`. A write scope without read is denied. The gateway can now save owner-authored messages from those explicitly enabled local devices, share them across other conversation-read-enabled devices, reject idempotency-key conflicts, cap message length, and clear history via local admin authorization. Messages are never passed to an AI model or Twilio, and are not mirrored from the Windows app. The v1.5 prototype stored up to 200 messages and displayed the last seven days. v1.6 now encrypts the entire local gateway state with a separately configured key, but the remote-identity, transport security and retention requirements remain incomplete.

### v1.6.0 local gateway state encryption

- The entire Git-ignored local gateway state is now AES-256-GCM encrypted before disk writes. There is **no default encryption key**; set `ARCHER_GATEWAY_DATA_KEY_HEX` to a securely generated, independently backed-up, private 64-character hex string.
- First read of a recognizable v1.5 plaintext state performs in-place encrypted migration BEFORE returning data. Incorrect/missing key, tampering or unsupported format causes a hard failure rather than resetting or overwriting state.
- Missing key means liveness can be inspected, but `GET /health/ready` reports not ready and every state operation is denied. Encryption does not make plain HTTP safe for public access.
- CI verifies migration, recovery with the original key, wrong-key denial, missing-key denial, tamper detection and absence of plaintext in the persisted gateway file.
- **Remaining:** actual off-host encrypted backups and Windows recovery drill, legacy plaintext snapshot cleanup, automated key management/rotation activation, secret manager, identity, HTTPS, production desktop chat synchronization, APNs and an iPhone client.

### v1.7.0 bridge milestone: local phone drafts visible on Archer PC

- A **separate 40+ character** `ARCHER_GATEWAY_BRIDGE_KEY`, unrelated to admin and event-ingest keys, authorizes only `GET /internal/conversation/bridge` on the isolated local gateway.
- Desktop MissionControl exposes a bounded read-only preview only when the owner explicitly enables `ARCHER_GATEWAY_CHAT_BRIDGE_ENABLED=true`, with the same separate key configured locally. It does not transmit existing desktop conversation data to the gateway.
- The Archer home view now shows recent phone-authored drafts with a **Use in chat composer** button. This merely fills the textarea; the owner must review/edit and **press Send** before any OpenAI charges or state changes occur.
- CI exercises both processes simultaneously with an encrypted synthetic phone message; checks unauthorized bridge requests, key separation, desktop preview content and zero auto-AI submissions.
- **Not yet full shared chat:** desktop assistant replies are not written back to gateway, no live iPhone app, no public HTTPS, no background processing of mobile messages, no multiuser account identity, no automatic context merging.

### v1.8.0 two-way local journal continuation (MANUAL, NOT live mobile chat)

When an owner selects an unedited local phone-authored draft and explicitly presses the normal Send button, desktop Archer generates and persists a response under existing AI spending controls. The owner may then separately choose **Share last reply with mobile** and confirm. Desktop verifies the latest saved user/assistant pair matches that draft, and a distinct bridge-authenticated localhost route stores one linked assistant reply in the AES-GCM-encrypted gateway. The local mobile journal read API displays both owner and assistant records and their reply linkage.

CI tests explicit authorization, original-message matching, idempotency, conflict refusal and a synthetic desktop-to-gateway round trip with no paid model calls. This does **not** automatically process messages, reply when the PC is off, sync general desktop conversation history, expose the iPhone to a server or deliver push/SMS/calls. The full authenticated production conversation service, sessions, identity, transport, backup, push, real voice and mobile app remain open.

### v1.9.0 — Local conversation sessions and incremental cursors

MobileGateway now stores up to ten **single-owner, local-only** conversation sessions with a fixed Main conversation ID. Explicitly `conversation:write`-authorized devices can create or archive non-Main sessions; read-only devices can list and fetch. Each saved owner message and explicitly shared assistant reply receives a **server-assigned monotonically increasing sequence** scoped to its session. `GET /v1/conversation/messages?sessionId=<guid>&after=<sequence>&limit=<1-100>` returns a bounded page, `nextAfter`, `hasMore`, and `highWatermark`; callers can catch up without relying on device clock order. Legacy v1.8 journal messages automatically migrate in place (under encryption) to Main with deterministic cursors.

Archived sessions remain readable but cannot accept owner messages or desktop replies. Replaying a device clientMessageId in another session is a conflict. Record expiration and the 200-message cap can create sequence gaps, so cursors indicate **position, not delivery guarantees**; a later production design must provide retention-safe sync, explicit gaps/snapshots, owner-controlled export/deletion, cross-device concurrent consistency, authenticated Internet transport and full AI context.

### v1.10.0 — Owner-controlled desktop session browser

The existing Archer home interface now shows an opt-in **Shared local conversation history** panel with a session selector, read-only owner/Archer messages and incremental **Load next messages** control backed by v1.9's server-assigned cursors. The desktop calls two fixed, separate-key-protected localhost gateway routes via bounded, proxy-free HTTP. The existing `ARCHER_GATEWAY_CHAT_BRIDGE_ENABLED` flag still defaults off. When a desktop reply is explicitly shared, the session browser can refresh. There are no new AI model requests, Twilio calls, pushes or writes in the session browser.

CI tests disabled-default behavior, wrong bridge-key rejection, local session catalog, history ordering before/after a simulated manually shared reply and safe UI rendering. The browser does NOT create or archive sessions, synchronize desktop AI context, allow arbitrary mobile device actions, handle public authentication or make an iPhone remotely accessible. Those are separate production milestones.

### v1.11.0 — Verified offline encrypted backup utility

A new standalone `Archer.GatewayBackup` .NET 10 CLI can `verify` a saved gateway state, `backup` it to an entirely new immutable encrypted file, and `restore-new` a verified backup to a fresh state file only when the local gateway port is not listening. It reuses the EXACT gateway AES-GCM implementation, requires the owner's private 256-bit key, refuses absent/wrong keys and authenticated tag tampering, never creates plaintext output, prints no message/credential content and never overwrites an existing destination.

CI builds the utility, stores a synthetic local encrypted conversation, validates a backup made while the gateway is running, rejects a live restore and overwritten targets, tests incorrect/missing keys and damaged ciphertext, restores to a new file and starts a new gateway process to confirm the same device and session message survive. No external storage, automatic backup schedule, cloud deployment, key rotation or operating-system hardening is provided. A real Windows test and off-host backup plan are still necessary.

### v1.12.0 — Offline key rotation (new-file only)

The standalone backup tool now supports rotate-new with an OLD authenticated AES-256-GCM key and a separately supplied NEW random 256-bit key, reading and verifying source state before producing a **new** ciphertext file. The source and all prior backups remain unchanged; old archives still require their original key. Rotation is forbidden while gateway port 5091 is serving, with any missing/equal/malformed keys, invalid ciphertext or pre-existing output. The rotated output is authenticated with the new key before the write, then can be verified and mounted as a fresh local gateway data directory after explicitly changing the key. Automated synthetic CI verifies state continuity (session sequence, message, device token), rejection cases and unchanged source.

This completes a **local development utility**, not production key-vault integration, automatic backup rekeying, old-key revocation, Windows recovery testing or public/cloud access.

### v1.13.0 — Explicit message retention gaps during device reconnect

MobileGateway's encrypted per-session sync now returns oldestAvailableSequence, historyGapDetected and missingMessageCountBeforePage alongside the existing cursor/highWatermark. A future phone app can warn honestly when seven-day/200-message retention has removed earlier messages. If all messages are expired, nextAfter advances to highWatermark rather than looping over an empty page. Tests simulate partially and fully expired sessions, prove proper warning and forward progress, and validate encrypted local migration.

This is a **local protocol improvement only**. It does not restore expired messages or supply an internet/mobile client. Genuine durable push delivery, server retention policy, backups, owner export/deletion and production phone reconnection remain outstanding.
