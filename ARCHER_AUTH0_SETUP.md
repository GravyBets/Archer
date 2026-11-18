# Archer + Auth0: owner-controlled setup and safe integration runbook

Status: **AUTH0 SELECTED AS IDENTITY PROVIDER; NOT CONNECTED OR LIVE**. Prepared 2026-11-17. This file is not a production authorization or permission to expose any service. The existing RemoteGateway remains explicitly bound to **127.0.0.1:5093** and **/health/ready = 503**.

## Decision

- Identity service: **Auth0 (Okta Customer Identity Cloud)**.
- Initial deployment: **one owner / one experimental iPhone**. Native iOS App via Auth0.swift, system browser, Authorization Code + PKCE, tokens stored in iOS Keychain with the official CredentialsManager.
- A distinct **Auth0 API** issues *audience-specific access tokens* for Archer. ID tokens are **not** API bearer credentials.
- Auth0 signs and validates login credentials; **Archer separately** owns device enrollment, revocation, message retention, action approvals and spending policy.
- Avoid paid Auth0 features/add-ons, real user content and tenant provisioning until the owner decides to start the tenant. Auth0's Free plan currently advertises up to 25,000 MAU but current account-level limits and feature availability must be checked before signup. No recurring infrastructure spending is authorized by this document.

Official setup references: [Native iOS quickstart](https://auth0.com/docs/quickstart/native/ios-swift), [Native PKCE](https://auth0.com/docs/get-started/authentication-and-authorization-flow/authorization-code-flow-with-pkce), [APIs](https://auth0.com/docs/get-started/apis), [namespaced custom claims](https://auth0.com/docs/secure/tokens/json-web-tokens/create-custom-claims), [refresh-token rotation](https://auth0.com/docs/secure/tokens/refresh-tokens/refresh-token-rotation), [pricing](https://auth0.com/pricing).

## Owner actions in Auth0 Dashboard — no CLI secrets required

**Only the owner can make these changes** in their Auth0 tenant. These are instructions, not completed operations.

1. Visit [Auth0 Dashboard](https://manage.auth0.com/), create/sign into a **personal** Auth0 account, choose the **Free** subscription if offered, and create a *development* tenant. Prefer a region consistent with the intended data residency; do not use corporate/work credentials or a company tenant.
2. Under **Applications > Applications**, create **Archer iPhone Dev** with **Native** application type (not Machine to Machine, SPA or Regular Web App). Record **Domain** and **Client ID** for local setup. Do NOT share/commit a Client Secret. Native clients are public and must not ship any client secret.
3. Under **Applications > APIs**, register **Archer Remote Gateway Dev**. Assign an unambiguous, stable **API Identifier / Audience**, e.g. a URI you control. Choose **RS256** for access-token signing. Define minimal permissions such as `conversation:read`, `conversation:write`, `receipts:write`, and `notifications:receive` only if applicable. Avoid authorizing users for anything else. Ensure tokens for the Archer API are **access tokens** with exactly this audience.
4. In the Native application's settings, enable **Authorization Code + PKCE**, set **Allowed Callback URLs** and **Allowed Logout URLs** to the exact formats shown by the Auth0 iOS quickstart using the **final Xcode Bundle Identifier** and the tenant's Auth0 Domain; never allow wildcards or unreviewed HTTP redirect hosts. Auth0's iOS quickstart currently documents both the HTTPS universal-link form and the app-scheme fallback. Don't add placeholder production URLs.
5. Configure **rotating refresh tokens**, appropriate refresh-token expiration and replay detection, and the MFA options available for your tenant. Require reauthentication for high-risk changes such as device enrollment/revocation; the ability to login once is not automatic permission to perform business tasks.
6. Restrict enabled Auth0 login connections to the intended owner and disable self-signup if the configured connection allows it. Do not use an arbitrary client-supplied email address or nickname as the owner identity. Once a real test login exists, capture the **exact verified `sub`** from an authenticated server-side validation result; do not copy an email or personal user profile into GitHub.
7. **Do not implement an Auth0 Post-Login Action that blindly copies a client-provided `device_id` into a token.** Auth0 supports namespaced custom access-token claims via Actions, but the claim must be based on **server-controlled enrollment** and ideally device-bound proof. We have not implemented the secure association of the one-time device public key, Auth0 login session and authenticated browser callback. Until that is designed and tested, real Auth0 access tokens without a trusted device claim are intentionally rejected by Archer's current local-only probe.

## Configuration contract (local tests only, do not commit tenant-specific values)

The following variables are **names only**. All real values belong in local development environment settings or a secure secret store, never source control:

```text
ASPNETCORE_ENVIRONMENT=Development
ARCHER_REMOTE_GATEWAY_MODE=local-test
ARCHER_REMOTE_AUTH0_ENABLED=local-validation
ARCHER_REMOTE_AUTH0_DOMAIN=<YOUR_TENANT>.<REGION>.auth0.com
ARCHER_REMOTE_AUTH0_API_AUDIENCE=<YOUR_AUTH0_API_IDENTIFIER>
ARCHER_REMOTE_AUTH0_NATIVE_CLIENT_ID=<YOUR_NATIVE_APP_CLIENT_ID>
ARCHER_REMOTE_AUTH0_OWNER_SUBJECT=<EXACT_SIGNED_OWNER_SUB>
ARCHER_REMOTE_AUTH0_DEVICE_REGISTRY_FILE=<EXISTING_ABSOLUTE_SYNTHETIC_SQLITE_FILE>
ARCHER_REMOTE_AUTH0_DEVICE_CLAIM_TYPE=https://<DOMAIN_YOU_CONTROL>/archer/device_id
```

The canonical tenant domain above is a hostname, **not** an `https://` URL. Archer constructs the slash-terminated issuer `https://<domain>/` because Auth0 JWTs use that exact issuer string. The namespaced claim URI is a unique identifier; no web endpoint is needed at the URI. Auth0 advises using a URL namespace controlled by you, not an Auth0-owned domain. **No real account or signing keys are stored by these settings.**

When enabled, this test-only profile creates only `GET/POST /_local/auth0-probe/{ownerSubject}`. It checks signed Auth0 JWTs against the chosen HTTPS issuer's discovery/JWKS (when a token is actually presented), exact API audience, RS256, the registered Native client via signed `azp`, exact signed owner `sub`, a configured namespaced device claim and live persisted device scopes. POST returns 204 **without saving or executing anything**. It accepts no client-supplied issuer, JWKS URL or unverified device identifier and cannot coexist with the other local test identity modes.

**Security limitation:** Even a correctly signed Auth0 device claim does **not** cryptographically bind a stolen bearer token to the actual phone. This test profile must not become a public API until we add a reviewed sender-constrained, per-request device-key proof model and persistent, owner-authenticated enrollment/approval path. The current SQLite fixture is unencrypted test metadata, not a production data store.

## iOS design when the owner is ready

Use the official **Auth0.swift** SDK with an Xcode SwiftUI app. Configure the **Native** client ID, Auth0 domain and exact Bundle Identifier; use system-browser Web Authentication with PKCE (the SDK manages verifier/state/nonce), request the Archer API audience, and store/rotate credentials only with the SDK's **Keychain-backed CredentialsManager**. Keep access and refresh tokens out of GitHub, mobile logs, UserDefaults, query-string analytics and push payloads. Logout must clear Keychain credentials, and explicit device revocation must be enforced by Archer on **every API request**, even with an unexpired access token.

The real device approval flow must separately bind a locally generated Secure Enclave P-256 public key to the owner-authenticated pairing ticket and implement replay-resistant signed requests or another reviewed sender-constraining mechanism. Native app development will require access to a Mac/Xcode and may require Apple Developer enrollment for distribution/TestFlight; neither has been set up by this runbook.

## First native iPhone login prototype (review branch)

An iOS SwiftUI source target now lives under [`ios/ArcherMobile`](ios/ArcherMobile). It uses the official Auth0.swift **3.3.1** SDK (not the 4.0 beta) for the first real browser-based Authorization Code + PKCE login, the Archer API audience, Keychain-backed refresh and logout. See [the iOS README](ios/ArcherMobile/README.md).

**No iPhone app has been installed, no live Auth0 owner login from the new app has been tested, and no remote gateway connection has been permitted.** The Xcode project is generated by XcodeGen from `ios/ArcherMobile/project.yml`. The Native Client ID is intentionally a placeholder in the tracked `Auth0.plist`; the owner must enter it locally before launching. Do not commit real account identifiers or any credential.

Native compilation requires macOS/Xcode, with a GitHub macOS Simulator compilation workflow for review. A physical device requires legitimate personal Apple signing, and TestFlight distribution generally requires paid Apple Developer membership.

## Local owner-token verification without pretending a phone is enrolled

An additional **development-only** `Auth0OwnerSignInProbe` mode allows a future real Auth0 access token to prove the configured owner logged in even before secure per-device claims are implemented. It must run in isolation and NEVER grants device access or exposes conversations. Variables (all values must stay private in personal developer environment, never in public GitHub):

```text
ASPNETCORE_ENVIRONMENT=Development
ARCHER_REMOTE_GATEWAY_MODE=local-test
ARCHER_REMOTE_AUTH0_SIGNIN_ENABLED=local-proof
ARCHER_REMOTE_AUTH0_SIGNIN_DOMAIN=<TENANT>.<REGION>.auth0.com
ARCHER_REMOTE_AUTH0_SIGNIN_API_AUDIENCE=https://archer.example/api/dev
ARCHER_REMOTE_AUTH0_SIGNIN_NATIVE_CLIENT_ID=<NATIVE_CLIENT_ID>
ARCHER_REMOTE_AUTH0_SIGNIN_OWNER_SUBJECT=<AUTH0|EXACT_OWNER_SUB>
```

Only signed, unexpired RS256 access tokens from the exact tenant, intended for the exact API and Native client, belonging to the exact configured owner, with a minimal `conversation:read` API scope, can get `200` on `GET /_local/auth0-owner-proof`. No JSON `sub`, email or access-token value is returned. There is deliberately **no live device or message route, nor any public listener**. An iPhone cannot reach this localhost endpoint; a future private staging connection, audited TLS and sender-constrained device proof remain required for end-to-end tests. Do not post tokens into ChatGPT, issue trackers, screenshots or curl shell history.

## Acceptance gates before cloud staging

- [x] Auth0 chosen; no paid tenant or provider requests from Archer's CI
- [x] Local Auth0 configuration parser and strict single-verifier, no-data probe
- [x] Test-only issuer/client/audience/namespaced-claim settings and startup safety checks
- [ ] Owner creates development Auth0 tenant, Native app and custom API; verifies Free-tier/feature terms
- [ ] User-provided **non-secret** domain, Native client ID and API identifier loaded into private local settings
- [ ] Independent review of Auth0 signing algorithm, issuer, discovery/JWKS and token dialect
- [ ] Real Auth0 login with browser PKCE, correct API audience, Keychain token storage and rotation verified on a physical iPhone
- [ ] Design and verify trusted namespaced device-claim issuance without trusting app-supplied identifiers
- [ ] Bind each enrolled device to a per-request possession proof and verified owner session
- [ ] Repeat stolen-token/revocation/offline/recovery tests with real Auth0 credentials
- [ ] Encrypted cloud database, backups, cost limits, TLS ingress, and independent security review
- [ ] Owner explicitly approves any cloud hosting charges and public exposure

**Not yet done:** Auth0 account/tenant creation, live OIDC discovery, login, real device enrollment, iPhone app, private staging or production launch.
