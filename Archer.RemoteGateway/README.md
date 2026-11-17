# Archer.RemoteGateway — localhost security skeleton (NOT A MOBILE SERVICE)

This is a separate .NET 10 ASP.NET Core project for **synthetic testing only**. It must not be deployed to a public server, tunneled, or connected to a phone. Existing Archer.MissionControl (5087), Archer.MobileGateway (5091), GeoShift, GitHub credentials and AI services remain entirely independent.

## Current security posture

- Hard-binds to **127.0.0.1:5093**, overriding ASPNETCORE_URLS. Arbitrary Host, Origin and forwarded proxy headers are rejected.
- Refuses ARCHER_REMOTE_GATEWAY_MODE values other than local-test, public-enablement flags and arbitrary remote bind settings.
- GET /health/live returns explicit capability flags that are **all false** except prototype liveness.
- GET /health/ready responds **503**. An operating process is not a ready remote/mobile product.
- No conversation, owner-login, credential enrollment, outbound notification, data-storage, AI, business-action or remote API endpoints are registered.
- A **development-only, explicitly enabled cryptographic identity fixture** optionally verifies signed RS256 JWTs with Microsoft's JwtBearer library, strict HTTPS issuer, exact API audience, no clock skew, a 2048-bit or stronger pinned **public** RSA key, and an explicit synthetic enrolled owner/device allowlist. Signed credentials alone never prove device enrollment.
- **Local-only durable revocation test layer:** `DurableDeviceRegistry.cs` uses SQLite to persist only synthetic approved-device metadata, granted scopes, and enrollment/revocation audit events. Test-tool proof + owner approval happen **offline** before the registry insert. Atomic revocation and fresh database reads on **every signed request** allow an already-issued JWT to be denied immediately, including after gateway restart. The resulting effective scopes are always the **intersection** of the signed JWT scopes and stored enrollment; no claim may escalate beyond the registry's grants.
- `ARCHER_REMOTE_TEST_DEVICE_REGISTRY_FILE` optionally points to an **existing absolute path** for the local signed-identity fixture. Only Development + `ARCHER_REMOTE_GATEWAY_MODE=local-test` permits this feature. Missing, deleted, corrupt or unreadable registry state denies startup or access (no fallback to token-only trust). The database is created only by the synthetic CLI test harness; not by a remotely accessible endpoint. If this setting is absent, the earlier signed-JWT fixture continues to use its static test-only allowlist without persistence.
- **Not production storage:** SQLite fixture bytes, labels, fingerprints and audit events are **not encrypted at rest**, have no backup protocol, and support no owner recovery, migrations past schema v1, server-level access policy or real account lifecycle. Do not store actual customer data, personally identifiable labels or private keys. This does not implement or replace an encrypted cloud database, account login or production device binding.
- **Non-networked pairing contract:** `DevicePairingContract.cs` models P-256 device-key proof-of-possession, random one-use challenges, five-minute expiry, three failed-proof limit, authenticated-owner approval *as a caller precondition*, limited read/write/notification scopes, eight active-device cap, isolated owner records and immediate in-process revocation. It uses **no private device key** and creates **no HTTP endpoint**. Concurrent approvals are serialized; returned metadata cannot change stored grants. It is a **volatile test contract only**, not secure server persistence or a credential-vending service.
- The optional fixture exposes only GET/POST `/_local/identity-probe/{ownerSubject}`: the GET returns harmless synthetic approval flags, the POST only checks write authorization and saves nothing. Missing, forged, wrong-scope, cross-owner, expired, revoked or unapproved-device credentials are denied.
- Without explicit local-test mode **and** ASP.NET Development environment, the fixture refuses startup. With no fixture key it registers **no** authenticated endpoint. No /v1/* mobile service routes exist.
- No real user tokens or personal information are needed. NuGet's official JWT bearer verifier is used; no external OIDC server, real user, API account or paid provider calls are introduced.
- AccessPolicy.cs is a **non-networked, internal policy contract** for future validated identities. It cannot validate a JWT, OIDC provider, device hardware key or revocation state by itself. The tests link the same source file and use synthetic principal snapshots.

## Safe local verification

```bash
dotnet run --project Archer.RemoteGateway/Archer.RemoteGateway.csproj
curl http://127.0.0.1:5093/health/live
curl -i http://127.0.0.1:5093/health/ready
curl -i http://127.0.0.1:5093/v1/conversations
dotnet run --project tests/RemoteGatewayPolicyChecks/RemoteGatewayPolicyChecks.csproj
python3 tests/SmokeRemoteGateway.py
python3 tests/SmokeRemoteSignedIdentity.py
dotnet run --project tests/RemoteDevicePairingChecks/RemoteDevicePairingChecks.csproj
dotnet run --project tests/RemoteDeviceRegistryChecks/RemoteDeviceRegistryChecks.csproj
python3 tests/SmokeRemotePersistentAuth.py
```

Expect liveness=200, readiness=503 and unimplemented conversation route=404. The JWT smoke creates ephemeral RSA keys in a temporary directory and discards them; do not copy test private keys into Archer configuration. Test scripts deliberately launch only local synthetic processes. **Do not put any credentials or personal conversations into this service.**

## What must happen before real mobile access

A separately reviewed OIDC login/PKCE design, **real** identity-provider JWKS verification and key rotation, secure refresh-token lifecycle, audited token validation, persistent device enrollment and revocation, strict owner-account isolation, scoped device enrollment and revocation, trusted TLS ingress/proxy, encrypted database, durable outbox, rate/cost limits, off-host backups, real device security testing, Apple/APNs integration and owner go-live approval are all still required. See the remote architecture proposal in [draft PR #24](https://github.com/GravyBets/Archer/pull/24) and the [mobile security review](../ARCHER_MOBILE_SECURITY_REVIEW.md).

The persistent-auth smoke generates a temporary SQLite store and synthetic signed JWTs; an offline test utility first creates a proved/approved device, then revokes it while the gateway is live. Read/write requests using the previously valid JWT must immediately fail after revocation and across a process restart. It also deletes the test DB during a live session to assert fail-closed behavior. These tests do not change normal Archer data directories.

The cryptographic pairing tests use locally generated ephemeral P-256 keypairs and a synthetic owner string. **The synthetic caller string is not authenticated by the pairing class.** A real implementation must accept an identity verified by the OIDC middleware, persist and atomically revoke enrollments across every replica, enforce per-device proof/replay controls on requests, log audits, protect against brute-force and recover lost devices. Current signed JWT fixture and pairing contract are intentionally independent; do not connect either to a public route.

**Do not change a port or a flag to publish this app: it contains no production authentication.** Future public implementation requires a separate reviewed design and implementation, not a configuration toggle.
