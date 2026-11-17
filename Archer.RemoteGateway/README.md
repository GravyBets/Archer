# Archer.RemoteGateway — localhost security skeleton (NOT A MOBILE SERVICE)

This is a separate .NET 10 ASP.NET Core project for **synthetic testing only**. It must not be deployed to a public server, tunneled, or connected to a phone. Existing Archer.MissionControl (5087), Archer.MobileGateway (5091), GeoShift, GitHub credentials and AI services remain entirely independent.

## Current security posture

- Hard-binds to **127.0.0.1:5093**, overriding ASPNETCORE_URLS. Arbitrary Host, Origin and forwarded proxy headers are rejected.
- Refuses ARCHER_REMOTE_GATEWAY_MODE values other than local-test, public-enablement flags and arbitrary remote bind settings.
- GET /health/live returns explicit capability flags that are **all false** except prototype liveness.
- GET /health/ready responds **503**. An operating process is not a ready remote/mobile product.
- No conversation, owner-login, credential enrollment, outbound notification, data-storage, AI, business-action or remote API endpoints are registered.
- No real user tokens or personal information are needed. No external dependencies or paid provider calls are introduced.
- AccessPolicy.cs is a **non-networked, internal policy contract** for future validated identities. It cannot validate a JWT, OIDC provider, device hardware key or revocation state by itself. The tests link the same source file and use synthetic principal snapshots.

## Safe local verification

```bash
dotnet run --project Archer.RemoteGateway/Archer.RemoteGateway.csproj
curl http://127.0.0.1:5093/health/live
curl -i http://127.0.0.1:5093/health/ready
curl -i http://127.0.0.1:5093/v1/conversations
dotnet run --project tests/RemoteGatewayPolicyChecks/RemoteGatewayPolicyChecks.csproj
python3 tests/SmokeRemoteGateway.py
```

Expect liveness=200, readiness=503 and unimplemented conversation route=404. Test scripts deliberately launch only local synthetic processes. **Do not put any credentials or personal conversations into this service.**

## What must happen before real mobile access

A separately reviewed OIDC login/PKCE design, audited token validation and rotation, strict owner-account isolation, scoped device enrollment and revocation, trusted TLS ingress/proxy, encrypted database, durable outbox, rate/cost limits, off-host backups, real device security testing, Apple/APNs integration and owner go-live approval are all still required. See the remote architecture proposal in [draft PR #24](https://github.com/GravyBets/Archer/pull/24) and the [mobile security review](../ARCHER_MOBILE_SECURITY_REVIEW.md).

**Do not change a port or a flag to publish this app: it contains no production authentication.** Future public implementation requires a separate reviewed design and implementation, not a configuration toggle.
