# Archer remote/mobile gateway — proposed security architecture (DRAFT)

Status: DRAFT FOR INDEPENDENT REVIEW (November 17, 2026). No implementation or production approval. This file does not authorize publishing a listener, buying hosting, enrolling a real phone, or making billable provider calls.

## 1. Executive decision

**Do not expose either existing localhost service.** Archer.MissionControl at 127.0.0.1:5087 has local administrative endpoints not suitable for public users. Archer.MobileGateway at 127.0.0.1:5091 intentionally rejects non-loopback clients and relies on local static secrets and test device credentials. The v1.14 history bridge and AES-encrypted JSON prototype are valuable functional references, NOT a remotely deployable identity or messaging service.

Proposed direction: a distinct **RemoteGateway** application and separate, private database, deployed later behind a trusted HTTPS ingress. Its public routes implement mobile identity and scoped messaging only. It must never proxy a client's requests to the existing desktop application, local gateway admin endpoints, GitHub write key, raw OpenAI API key or Twilio account.

The launch gate is a signed independent security review, complete synthetic penetration and recovery drills, and the owner's explicit approval of production costs and exposure.

## 2. Trust boundaries and traffic

1. **iPhone:** the owner controls app login, notification permission, camera/mic/location permission and device revocation. No passive microphone, background location or invisible activity collection. Access/refresh material is kept in iOS Keychain, not normal preferences, browser localStorage, logs or diagnostic files.
2. **Public edge:** TLS termination, exact Host allowlist, request-body limits, HSTS where appropriate, request filtering and abuse controls. Forwarded IP/scheme is honored ONLY from explicitly enumerated proxies, never from arbitrary headers or an unrestricted proxy configuration. Public ingress terminates at the new remote service only.
3. **RemoteGateway app:** validates authenticated user, enrollment and per-resource owner permissions on every request. It runs under its own service account and has only least-privilege access to its database, queue and provider tokens needed by its dedicated workers.
4. **State and workers:** encrypted-at-rest relational message store, encrypted backup copies, durable outbox and a separate outbound worker; none is the existing local JSON gateway, desktop conversation file or privileged workstation.
5. **Mission Control:** stays private on the personal development machine. A future explicitly approved outbound service-to-service bridge may publish a narrowly scoped result through an authenticated queue; arbitrary client-to-desktop proxying is prohibited. The desktop is not made reachable via NAT/port forwarding.

**No client-supplied URL, host, filesystem path or arbitrary command is ever routed into local Archer or GeoShift.** User messages are data, not execution instructions. A separate approval artifact is required before any business write, customer notification, code merge, deployment or paid operation.

## 3. Identity, enrollment and revocation

- Use a reviewed OpenID Connect identity provider with **Authorization Code + PKCE** and system-browser authentication for iOS. Do not invent passwords or expose a static long-lived API bearer inside an app bundle. Pin the OIDC issuer/audience and validate JWKS, nonce, redirect URI, signature, token expiry and audience server-side.
- Public native-client refresh tokens must be **sender-constrained or rotated with replay detection**; access tokens should be short-lived and audience/scope-bound. The concrete provider, token lifetime and recovery model require an explicit choice and tests. Never depend on client-reported device identity alone.
- One owner account is the initial product target, but every row and every API operation must still be keyed by authenticated owner identity to avoid accidental cross-account data exposure.
- Enrollment is explicit: after owner login, approve a new device name, record random opaque device ID and enrollment time, grant only needed scopes (conversation read/write, notifications, acknowledgments), and allow owner to list, rotate and instantly revoke a lost device. A revoked device must fail all reads, writes and push registration immediately.
- APNs device tokens are separate from authentication tokens and **cannot** be treated as proof of identity. Encrypt push tokens at rest, isolate the APNs signing key, rotate device tokens when Apple changes them, remove invalid tokens after provider feedback, and omit message bodies from lock-screen push payloads by default.
- Never send credentials in URL query strings, telemetry, client error reports or GitHub artifacts. Redact device IDs where a more private correlation ID suffices.

## 4. Proposed bounded mobile API contract

These are **design proposals, not shipped routes**:

| Operation | Proposed resource | Authorization and behavior |
|---|---|---|
| Read own devices | GET /v1/me/devices | Owner login; metadata only, no refresh token |
| Revoke a device | POST /v1/me/devices/{id}/revoke | Owner reauthentication for sensitive action; audit; invalidates refresh and access policy |
| List sessions | GET /v1/conversations | Owner scoped; pagination, archived metadata |
| Read history | GET /v1/conversations/{id}/messages?after=...&limit=... | Owner scoped; 1–100 server-sequenced records; gap/retention warnings |
| Save owner message | POST /v1/conversations/{id}/messages | Device has explicit write scope; stable client idempotency ID; no implicit paid AI call |
| Acknowledge received page | POST /v1/conversations/{id}/receipts | Persist authenticated device cursor; never infer receipt from read requests |
| Register APNs token | PUT /v1/me/devices/{id}/push-token | Owner and device scoped; exact APNs environment/topic validation |
| Delete/export | owner-approved, separate endpoint set | Designed and tested before real conversations are retained |

Every route has an authorization policy, abuse budget, max payload size, idempotency contract and response retention semantics. No public routes expose gateway provisioner/admin/ingest secrets, model credentials, arbitrary GitHub repository writes or desktop debugging data.

## 5. Message durability and privacy

- Production data model must distinguish Owner, Device, Session, Message, DeviceReceipt, OutboxItem, DeliveryAttempt and AuditEvent. Use opaque IDs, foreign-key ownership, unique (owner, device, clientMessageId) and unique server-assigned (session, sequence). Enforce row-level ownership in application and database strategies; independent tests should attempt cross-owner read/write.
- Order is **server assigned within each session** inside the same transaction as message insertion. A retry of the same idempotency key returns the original result. A conflicting reuse returns 409 without consuming an AI credit.
- Durable sync must report earliest retained cursor, high watermark, explicit gaps, a continuation cursor and hasMore, including when no retained messages remain. A client receipt is separate from a server save and separate from a provider-delivery receipt.
- Choose an owner-controllable retention policy before launch. The current local seven-day/200-record limits are **not** an acceptable silent production guarantee. Explicitly distinguish transcript storage, backup retention, push-delivery retry retention, audit metadata and deletion propagation.
- Use envelope encryption at rest with managed keys, restricted IAM, verified off-host encrypted backups and regularly rehearsed restore; secrets must not live in source, build artifacts or client apps. Clearly state that server-side AI processing is **not end-to-end encryption**, since Archer must be able to read approved messages.
- Human-approved actions have distinct immutable approval records (actor, exact proposed action/payload hash, scope, expiry, cost estimate and target). Auto-generating a suggestion is not approval. No automatic expensive model calls merely because a mobile message was persisted.

## 6. Push, SMS and phone

APNs is a **notification transport**, not the conversation database. The worker loads a minimal payload only after a durable outbox record exists. Store provider receipt/response classification; retry transient failures with exponential backoff, jitter and hard attempt/time ceilings; deduplicate by stable notification ID and dead-letter exhausted jobs for owner review. Use generic lock-screen text by default.

SMS and phone calls are different, **opt-in** transports, with distinct destination verification, quiet hours, consent, spending caps and escalation thresholds. Twilio inbound webhooks require cryptographic request verification plus replay/rate controls and cannot issue model/code/financial actions directly. No SMS or voice integrations go live in this design milestone.

Never treat a provider-accepted push as proof that a person saw it. Provider delivery, device receipt, owner read and explicit approval are four different states.

## 7. Security and reliability acceptance checklist

Before any public binding:

- [ ] Independent threat-model review names owners for identity, TLS, backup keys, device recovery, retention and deployment.
- [ ] Assert 5087 and 5091 are unreachable from the public network; only the newly reviewed mobile service is behind the edge. No home-router port forwarding or temporary tunnel to the current dashboard.
- [ ] Reject invalid certificates, wrong Host, untrusted proxy headers, missing/mismatched OIDC issuer/audience, expired signatures, missing scopes and cross-account object IDs.
- [ ] Verify fresh enrollment, revoked device, stolen refresh token replay, concurrent writes, duplicate/conflicting idempotency keys and paging across retention gaps and fully expired sessions.
- [ ] Enforce per-account and per-device distributed rate and cost limits, distinct queue-worker authorizations and strictly bounded uploads/responses.
- [ ] Simulate offline device reconnect, service crash between enqueue/send, provider timeout, duplicate webhook, APNs invalid token, retained message deletion and dead-letter operator handling.
- [ ] Complete encrypted off-host backup and restore drills, database migration rollback, provider-secret rotation, audit export and deletion-propagation checks.
- [ ] Review iOS Keychain use, app login/logout, notification permission, APNs sandbox vs production, lock-screen privacy and lost-device recovery on physical hardware.
- [ ] Owner approves domain, hosting/provider selection, recurring and worst-case monthly costs, emergency spend stop, deployment and go-live separately.

## 8. Sequenced implementation plan

**Gate A — Design (now):** independent review of this draft and identity/provider selection. Existing localhost services remain unchanged.

**Gate B — Synthetic-only skeleton:** create new service in a separate project with a deny-by-default config; never bind publicly without a reviewed environment policy. Add contract and threat tests for authentication, owner isolation, cursor semantics and idempotency. No real user message or phone token.

**Gate C — Private staging:** configure DNS/TLS, isolated database, service secrets, backups, health probes, rate limits, audit and staging Apple push; complete external review and failure drills using fake users/messages.

**Gate D — Owner's explicit private beta:** authorize one real iPhone, run TestFlight, verify login/device revocation/push privacy with controlled limits. Do not enable autonomous business writes or spending by default.

**Gate E — Deployment:** separately approve scheduled monitoring, alert recipients, budgets and production data retention. GeoShift's customer and payment systems remain independent integrations with their own authorization.

## 9. Open decisions, not assumptions

- Hosting/provider and region, expected recurring cost and 24/7 uptime requirement.
- Identity provider and account recovery process; hardware-backed device binding design.
- Native iOS/TestFlight access (Mac/Xcode, Apple Developer membership).
- Transcript retention/export/deletion and backup retention. User should be able to see and change relevant privacy settings.
- Whether a specific urgent incident may override quiet hours; whether a phone call is allowed and whether failed calls fall back to SMS.
- Which actions Archer may ever perform without approval, versus require owner confirmation.
- Whether a future mobile web interim prototype is preferable before native iOS.

## 10. Reference standards (verify again at implementation time)

- OAuth 2.0 Security Best Current Practice, RFC 9700: https://datatracker.ietf.org/doc/html/rfc9700
- ASP.NET Core proxy and load-balancer guidance: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0
- ASP.NET Core Kestrel security considerations: https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/security-considerations
- Apple, registering the app with APNs: https://developer.apple.com/documentation/usernotifications/registering-your-app-with-apns
- Apple, connection to APNs: https://developer.apple.com/documentation/usernotifications/establishing-a-connection-to-apns

**No production security box above is checked by writing this document.** Independent testing, real secrets custody, incident response and owner approval remain mandatory.
