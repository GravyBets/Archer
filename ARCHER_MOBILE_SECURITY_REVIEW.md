# Archer mobile gateway — security and threat review (v1.2.0)

**Review date:** 2026-11-16. This is an engineering threat model, NOT an independent penetration test or approval for internet exposure.

## Current reality and strict boundary

- Archer.MissionControl is the existing single-user, **unauthenticated**, Windows localhost admin app at 127.0.0.1:5087. It contains chat, owner presence, missions, AI operations, optional GitHub-write capability, and optional paid Twilio actions. It must remain private and never be port-forwarded or reverse-proxied publicly.
- Archer.MobileGateway is a **separate process**, listening on **127.0.0.1:5091** using its own storage under a Git-ignored Data directory. It has **no reference to the MissionControl assembly or data store**, no OpenAI, GitHub-write, phone-number, payment, or GPS API endpoints. It implements a narrow local inbox and device enrolment/revocation.
- Communication from MissionControl to the gateway is **off by default**, always targets the literal loopback URL, and submits only selected bounded inbox fields after explicitly enabling ARCHER_GATEWAY_SYNC_ENABLED with a distinct ingest key.
- **No internet-facing mobile service is operational.** A phone cannot reach 127.0.0.1 on another computer. HTTPS, public ingress, APNs, iPhone app, authentication UX and remote monitoring require subsequent hardening; do not treat this as a production gateway.

## Existing MissionControl endpoint audit

The following 37 routes are currently mapped in Archer.MissionControl/Program.cs:

Read-only/public-looking but still localhost-only:
- GET /api/health, /api/config, /api/state, /api/usage, /api/spending
- GET /api/briefing, /api/briefing/preferences, /api/communications
- GET /api/mobile/capabilities, /api/inbox, /api/presence, /api/operations
- GET /api/github/check, /api/development/repository, /api/development/code-proposals

Mutating/configuration/cost-bearing:
- POST /api/spending/settings, /api/briefing/preferences
- POST /api/communications/preferences, /api/communications/test-sms, /api/communications/test-call
- POST /api/mobile/presence, /api/inbox/{id}/read, /api/inbox/{id}/acknowledge
- POST /api/presence, DELETE /api/presence
- POST /api/operations/monitors, /api/operations/monitors/{id}/enabled
- POST /api/operations/incidents/{id}/acknowledge
- POST /api/development/code-proposals, /api/development/code-proposals/{id}/publish, /api/development/preview
- POST /api/missions, /api/missions/{id}/plan, /api/missions/{id}/autorun
- POST /api/missions/{id}/run, /api/missions/{id}/tasks/{taskId}/retry, /api/chat

**High risk if exposed:** /api/state exposes personal chat, presence, drafts and local business information; /api/chat and mission endpoints incur API cost; /api/communications/test-* may initiate billable phone activity if configured; /api/development/code-proposals/{id}/publish writes GitHub branches and PRs when permitted. Localhost restriction is the safety boundary for this legacy component, but doesn't protect against malicious local processes or browser vulnerabilities on the same PC. Never move its port to 0.0.0.0 or a public proxy as a shortcut.

## Separate v1.2 mobile gateway routes

Unauthenticated local health ONLY:
- GET /health/live — static non-sensitive liveness/version (cannot reveal keys, devices or events).

Local operator/bootstrap/ingress:
- POST /internal/devices/enroll — requires X-Archer-Admin-Key (40+ characters), creates random 256-bit bearer ONCE with 1–7 day expiry.
- GET /internal/devices — same local admin key; returns device metadata only, no hashes/tokens.
- POST /internal/devices/{id}/revoke — same key; immediately invalidates token.
- POST /internal/events — requires DIFFERENT X-Archer-Ingest-Key (40+ characters); strict shape, 40 events max, 30-day freshness, bounded text; source-key idempotent.

Scoped mobile-facing (still loopback only):
- GET /v1/inbox — needs unexpired, unrevoked Bearer token with inbox:read.
- POST /v1/inbox/{id}/read and /acknowledge — needs inbox:ack.

Generated bearer is only returned on enrollment and SHA-256 of high entropy token is persisted; comparison uses fixed-time hash comparison. Five concurrently active devices maximum. No login, self-service enrollment, refresh token, device push token or public signup. A native iOS app is not created yet. Device acknowledgment updates only the gateway copy (not the original desktop mission/incident).

## Threat matrix and mitigations

| Threat | Current mitigation | Remaining gate |
|---|---|---|
| Exposed admin dashboard | Explicit 127.0.0.1:5087 binding; do not publish | Add per-owner admin authentication and local process protections; keep separate private network |
| Unauthenticated phone API | Separate 127.0.0.1:5091 listener; Bearer token required for inbox | Real production identity, device management, proof-of-origin, TLS and hardened reverse proxy |
| Stolen iPhone/token | Expiry max 7 days; device revocation; restricted inbox scopes | iOS Keychain storage, secure enrollment UX, rotation/refresh, remote revoke, account sign-in |
| Browser CSRF / web origins | No cookie auth, no CORS, Origin requests rejected, admin secret header | Production web/CORS/CSRF review if a web client is added |
| Unbounded calls / abuse | Gateway has zero Twilio or AI calls; bounded rates, body size, events | Global provider limits, distributed queues, billing reconciliation and fraud detection |
| Duplicate/stale notifications | Event sourceKey upsert, stored read/ack, resolved state monotonic; bounded age/count | Cross-system idempotent outbox, delivery receipts, multi-node ordering, stale/clock-skew policy |
| Fake website failure | Only source-backed local incidents; labels say needs review | Real independent verification, customer/business telemetry and false-positive suppressor |
| Secret leakage | Secrets in environment, no key in Git, store hashes, no token echo after creation | OS secret manager, rotation, audit/backup encryption, stronger logging controls |
| Prompt injection via event text | Gateway never feeds events to a model or executes text; strings remain data | Future AI summarization must treat imported text as untrusted and enforce command separation |
| GPS/PII leakage | No gateway location or conversation fields; only minimal title/summary/event times | Production PII classification, explicit redaction, encrypted storage, retention/deletion policy |
| LAN / non-TLS eavesdropping | HTTP traffic is loopback only, Kestrel binds 127.0.0.1:5091 | TLS plus strict trusted proxy list, not blind Forwarded headers; no public HTTP |
| Attack traffic flood | .NET global fixed-window 90 requests/minute on one loopback IP; max body 32 KB | Edge WAF/rate limiting, per-device limits, quota and multi-node controls |
| Untrusted local software | Admin/ingest secrets; hashed device bearer; loopback only | OS account isolation, hardened host, secure provisioning and system-level attack resistance |
| Persistence corruption | Serialized writes with temporary atomic replacement, 200-event bound | Encrypted backups, restore tests, database migration/versioning, crash consistency |
| Changes while driving | Gateway contains no approval/deployment/business mutation endpoints | Native app driving-mode UX, spoken confirmation limits, safe deferral |

## Local-only opt-in setup

Prerequisites: .NET 10 SDK, separate optional process. Values must be cryptographically random and never committed to Git.

- ARCHER_GATEWAY_ADMIN_KEY: distinct random 40+ character local provisioning secret.
- ARCHER_GATEWAY_INGEST_KEY: distinct random 40+ character local ingest secret.
- ARCHER_GATEWAY_DATA_DIR: optional, separate Git-ignored writable directory.
- ARCHER_GATEWAY_SYNC_ENABLED=true in the MissionControl process **only after** running the gateway and setting the same ingest key there; off by default otherwise.

Start Archer.MobileGateway **locally**. GET http://127.0.0.1:5091/health/live shows publicAccessEnabled:false. The local admin API can enroll a short-lived test device and get a bearer token exactly once. Tokens never belong in a URL. A device can call GET /v1/inbox with Authorization: Bearer <token> while on the gateway machine. That's an API integration test, not access from an iPhone.

**Never send an enrollment token, key, phone number, GPS coordinate or real business event data to third-party test tools.** No work/employer system data may be synced. When disposing of a test token, revoke it, and wipe temporary state if appropriate.

## Independent production security gate (NOT DONE)

- [ ] Decide authenticated mobile user/account identity architecture and device onboarding.
- [ ] Separate internal provisioning/ingest endpoints from public listener (different interface or authenticated service-to-service channel).
- [ ] Terminate HTTPS with a trusted certificate and strict proxy/IP handling. Never trust arbitrary Forwarded headers or bind 5087.
- [ ] Add authenticated subscription for APNs device tokens, registration/revocation and push payload minimisation.
- [ ] Add per-device rate limits, privacy/retention controls, audit log, secret manager and protection against stolen credentials.
- [ ] Add external penetration testing (including replay, origin, brute force, revoked tokens), backups, restore and disaster recovery tests.
- [ ] Build/review iPhone app and device keychain, confirm Apple developer account and App Store/TestFlight requirements.
- [ ] Design source and gateway acknowledgement reconciliation, queued delivery and provider receipts.
- [ ] Independently validate projected OpenAI/Twilio/hosting costs and cost enforcement.

Production deployment stays BLOCKED until all these gates pass.

## Reference implementation notes

- ASP.NET Core rate limiting official guidance: https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-10.0
- Kestrel explicit endpoint binding: https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0
- Kestrel proxy/forwarding security: https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/security-considerations

This document inventories the current source at v1.2.0; revisit it when endpoints or authorization logic change.