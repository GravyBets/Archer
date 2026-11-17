# Archer Remote Hosting — Initial Staging Choice (planning only)

**Status: hosting candidate chosen for evaluation; NO resources purchased,
public service deployed, database created or authorization granted.**
Owner mobile Auth0 login is working on an iPhone through the separately
hosted public static site at `https://gravybets.github.io/Archer/index.html`.
This document is not permission to turn on a public API.

## Decision

**Recommended first private beta: Render, managed ASP.NET Core 10 web
service + managed PostgreSQL.** Keep GitHub Pages as the static UI.
This avoids buying a Mac and avoids maintaining a VPS operating system.
It also separates the public mobile API from the private Windows desktop.

The cloud server's job is **not to run a local language model**. It stores
enrollment and messages, validates owner-issued requests, queues explicitly
approved missions and eventually relays bounded results from a separate
operator worker. AI/model APIs remain distinct services with explicit caps.

As of November 17, 2026, Render lists roughly $7/month for a paid 512MB
web service and $6/month for an entry 256MB PostgreSQL instance. Thus
**about $13/month before storage, backup, bandwidth, workers, taxes or AI
usage**, and only if those entry plans are available when provisioning.
A 256MB PostgreSQL tier is a *prototype floor*, not a recommended production
capacity. Check Render's actual checkout and plan backup entitlement.

Sources:
- https://render.com/pricing
- https://render.com/docs/compute-plans
- https://render.com/docs/web-services
- https://render.com/docs/postgresql
- https://render.com/docs/regions
- https://render.com/docs/postgresql-backups

## Initial capacity and region

| Component | Initial candidate | Cost and operating caveat |
|---|---|---|
| iPhone web app | Existing GitHub Pages | Already deployed; no new server needed |
| Auth0 | Existing dev tenant and SPA | Maintain exact issuer/audience/app allowlist; verify account's plan limits |
| Mobile API | 1 x Render paid web service (512 MB RAM prototype; allow 1GB upgrade) | Starts near $7/month; must be *new* reviewed public gateway, NOT localhost MissionControl |
| Persistent store | 1 x managed PostgreSQL | Starts near $6/month for very small tier; use larger tier if tests/metrics require |
| Region | Ohio candidate | Use same region for API and database, choose based on latency and actual availability |
| Storage | Start small (single owner; low text volume), track actual usage | No audio/blob uploads in first beta; do not treat ephemeral app disk as database |
| Backup | Encrypted off-service backup + verified restore | Separate cost, retention and operational burden; confirm features before go-live |
| Worker | Later isolated outbound-only mission/notification worker | Separate budget and approval, not bundled into the $13 estimate |
| Voice/SMS | Later separate provider budget | Do not connect Gmail/phone/provider credentials during beta |
| AI usage | Existing approved model API only | Explicit spending caps; not included in hosting estimate |

**Do not choose Render Free for a 24/7 operator:** free web services may
sleep after inactivity, and free instances/filesystems are not a durable
message store. Render service filesystems are ephemeral unless a deliberate
persistent disk is attached; PostgreSQL is the planned authoritative DB.

## Deployment architecture

```text
iPhone Safari / Home Screen PWA (GitHub Pages; static, public)
        |
        | Auth0 Authorization Code + PKCE
        v
Auth0 production-grade signed access token (existing tenant)
        |
        | HTTPS + CORS exact https://gravybets.github.io
        v
NEW Archer public Cloud Gateway (Render, dedicated service)
        |       validates token signature, issuer, audience, owner
        |       validates enrolled key possession per request + replay
        |       checks scopes, revocation, rate and cost caps
        |       NO desktop admin API or direct GitHub write keys
        v
Private PostgreSQL (same Render region; owner/device/messages/outbox/audit)
        |
        v
Restricted worker queue for separately authorized missions
        |
        v
Personal Archer operator (later outbound, authenticated connector only)
```

**A logged-in PWA is not a trusted enrolled device.** A non-extractable
WebCrypto signing key is not guaranteed hardware-backed on iOS and browser
storage can be lost. An approved device requires a server-origin challenge,
verified P-256 proof, authenticated owner approval, persistence, and fresh
revocation checks. The browser cannot grant itself approval.

## Security gates before purchasing or exposing the gateway

1. **Consolidate/review the open RemoteGateway PR stack (#25–#32)** and
   its design (#24). Those are local synthetic and owner-login proof stages.
   Do not simply set an environment flag, change a port or deploy them:
   the code currently hard-binds localhost and is intentionally NOT a
   production remote service.
2. Introduce a *separate* cloud startup policy. All sensitive routes
   require signed Auth0 tokens from the exact tenant, audience, approved
   browser client, server-pinned owner subject, server-side scopes and device
   proof. No unauthenticated read/list/create/enroll routes.
3. Implement atomic PostgreSQL `owners`, `devices`, `enrollment_challenges`,
   `conversations`, `messages`, `receipts`, `outbox` and `audit` records.
   Enforce ownership with foreign keys and per-request policy. No secrets
   or private signing keys in public GitHub.
4. Device enrollment:
   (a) PWA creates local non-extractable key after Auth0 login;
   (b) server issues unpredictable one-use short-lived pairing nonce;
   (c) client signs server-issued challenge over exact authenticated owner
   identity; (d) backend verifies; (e) owner independently confirms pairing
   on an already trusted channel; (f) database records public key/scopes.
   **Do not permit a brand-new untrusted phone to approve itself.**
5. Every protected request must include fresh key-bound, replay-resistant
   proof covering method/path/body digest/nonce or sequence and short
   timestamp window. JWT authentication alone and a public key fingerprint
   alone are insufficient. Validate loss/revocation/rotation.
6. Production ingress: managed TLS only, exact CORS origin, limited HTTP
   methods/body sizes, no wildcard proxy forwarding, HSTS and safe response
   headers, abuse throttling, privacy-safe server logs, proper shutdown.
7. Add cross-account and invalid JWT tests, forged signatures, replays,
   device revocation while token still valid, concurrent enrollment,
   DB rollback, backup/restore and deploy rollback. No go-live with only
   synthetic unit tests.
8. Restrict GitHub → Render integration to this repository and a reviewed
   deployment branch; protect against surprise auto-deploys.
9. Cloud environment secrets set in Render, not `config.js`, GitHub,
   PR bodies, dashboard screenshots or ChatGPT messages. Pin Auth0 issuer,
   API audience, approved SPA client and owner `sub` only on the server.
10. Owner separately approves actual service creation, recurring charges,
    public endpoint, real-device pairing, and later message/AI execution.

## Owner-facing next steps

**Now (free):** create a Render account at https://dashboard.render.com/
if desired. Read the service, region and pricing screens. **Do not click
Create Web Service, deploy the existing RemoteGateway, authorize billing, or
create a Postgres instance yet.** Render's default public `onrender.com`
HTTPS address can be used for a reviewed staging API; no domain purchase is
required.

**After passing security gates:** set up a dedicated Render project,
a separately protected staging branch, one web service and same-region
database. The owner approves the plan price before creation. Ensure the
service auto-deploy setting matches the desired manual promotion and use
a staging-only account with a hard AI/telecom spend stop.

**First full end-to-end test:** the authenticated PWA asks for a pairing
challenge, signs it with the stored local P-256 key, the backend verifies
the signed owner and key, the owner separately approves enrollment, and
the PWA receives a **read-only** device status from a persisted registry.
Then test token replay, revocation and a lost/browser-cleared key. Message
send/receive is the milestone *after* these security and recovery tests.

## Alternatives

- **DigitalOcean basic VPS:** potentially low monthly price, but requires
  OS hardening, firewall, reverse-proxy TLS, patching, backups, monitoring,
  logs, database and disaster recovery. Better when we intentionally want
  direct server administration. https://www.digitalocean.com/pricing/droplets
- **Fly.io:** good lightweight Machines, but Postgres management and
  regional networking/storage choices are more intricate for a first
  owner-only beta. https://fly.io/pricing/

**No hosting estimate is a guarantee; verify plan, region, taxes, storage
and backup charges in the provider's current purchase screen.**
