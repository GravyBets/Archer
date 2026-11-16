# Archer — Mobile, Messaging, Voice & Safe Business Operations Roadmap

Working plan: 2026-11-16. Target: one Archer across Windows, iPhone, text, phone, and (if approved) CarPlay.
Current baseline: Archer v1.4.0 is LOCALHOST-ONLY. It supports guarded AI, source review/code drafts, site checks, opt-in outbound SMS/one-way calls and manually selected coarse presence. Native iOS, real push, inbound voice/text conversation and cloud hosting DO NOT exist yet.

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
- [x] Document local/public threat model, token risks, abuse, CSRF, rate limits, provider leakage and hard production blockers in [mobile security review](ARCHER_MOBILE_SECURITY_REVIEW.md).
- [x] **Local gateway prototype:** separate project and process bound to 127.0.0.1:5091; limited authenticated inbox, independent storage and no admin/AI/GitHub/Twilio routes (v1.2).
- [ ] Harden and deploy a **separate authenticated public/mobile gateway** behind TLS without exposing today's unauthenticated dashboard or port 5087.
- [x] Initial owner-enrolled random device tokens with hash-only storage, read/ack scopes, 1–7 day expiry and immediate admin revocation (v1.2 local).
- [x] Local admin-only device token rotation invalidating old tokens, read-only versus read/ack scope selection, per-device rate limiting, redacted device event audit and revoke tests (v1.4).
- [ ] Secure **remote** pairing, owner identity/login, production refresh/rotation protocol, device audit export, stolen-device recovery and physical-device keychain integration.
- [x] Local gateway enforces loopback, restrictive Host/Origin, scoped read/ack tokens and layered global/per-device rate limits (v1.4).
- [ ] Production HTTPS, trusted ingress, secure secret management, outbound caps, input validation review and per-identity distributed rate limits.
- [ ] Provision encrypted backups and complete a restore drill; add operational health and cost alerts.
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

- [ ] Define durable conversations, message IDs, devices, ordering, retention, privacy/export/delete and cross-channel session lifecycle.
- [ ] Connect Windows and phone chat to the same authenticated session backend.
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

1. **Completed:** v1.1 local inbox; v1.2 separate loopback gateway; v1.3 provider-free notification sandbox; v1.4 local device rotation, least privilege, redacted audits and layered rate limits. No cloud exposure.
2. **Next:** independent security architecture review and a separately authenticated **TLS-ready public gateway design**, provisioning, retention and APNs-device enrollment; no remote access until those gates pass.
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
