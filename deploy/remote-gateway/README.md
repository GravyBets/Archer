# Remote HTTPS gateway — protected ingress preparation

**Engineering status: foundation prepared, NOT hosted, NOT internet accessible.**

Archer Pocket already loads on GitHub Pages through HTTPS with Auth0 and
local P-256 signing. Its login/token does not authorize access to the
separate desktop `Archer.MissionControl` (127.0.0.1:5087) or the
remote identity skeleton `Archer.RemoteGateway` (127.0.0.1:5093).

## Security boundary and release order

1. Keep `Archer.RemoteGateway` bound to loopback. `/health/ready`
   MUST continue to answer 503; unsupported `/v1/*` routes are 404.
   Do **not** forward router ports, tunnel HTTP traffic, or point Caddy
   to `5093` while the public remote gateway is not released.
2. The `Caddyfile.review-only` is intentionally a **fail-closed 503
   ingress mock**. It has no `reverse_proxy`, no API access, no token
   forwarding and no secrets. Its `.invalid` hostname is reserved.
3. Before any real ingress, require a dedicated owned DNS name,
   certified HTTPS termination, modern TLS, strict host/origin policy,
   trusted proxy IPs and forwarded-header validation, rate/body limits,
   logging without credentials, provider JWT issuer/audience/JWKS/expiry
   validation, device-bound proof and revocation **on each request**.
4. Also require authenticated operator approval and recovery, persistent
   and encrypted data at rest, backups with restore tests, key rotation,
   bounded replay protections across restarts/instances, security review
   and explicit owner go-live authorization.
5. Archer MobileGateway (5091) and MissionControl (5087) are independent,
   with their own sensitive state. Never make either a public upstream.
   Future messaging must use a separate, narrowly scoped ingress service.
6. This mock should **not** be deployed just to get a green indicator.
   Green CI means the configuration remains blocked, not that remote
   connections work. Public service spend and exposure require explicit
   approval at release time.

## Automated regression

```bash
python3 tests/ValidateRemoteIngress.py
python3 tests/SmokeRemoteGateway.py
```

These tests assert the review-only HTTPS template has no proxy upstream and
that the server's localhost and readiness boundaries have not been removed.

## Acceptance

Stage 1 is complete when this trust-boundary specification and automated
preflight are reviewed and merged, while actual public exposure remains
**off**. Stage 2 implements owner-bound pairing behind the local-only gateway.
Stage 3 adds a scoped message read path behind device proof; **production
deployment is a later, separately approved milestone**, not a side effect.
