# Archer iPhone Dev — real Auth0 browser sign-in, no remote access

## Current status (2026-11-17)

The app source now exists in this repository. It uses **Auth0.swift 3.3.1** and SwiftUI to open Auth0 Universal Login with system web authentication, Authorization Code + PKCE, custom-scheme callback and owner sign-in. It requests the **Archer Remote Gateway Dev** API audience, asks for `openid profile email offline_access conversation:read conversation:write`, and uses Auth0's Keychain-backed `CredentialsManager` for storage and refresh. It supports login, refresh and logout with no token or user-profile output.

**Not yet tested on an iPhone, no app installed, no remote API, no push, no messages, no secure device approval.** An Auth0 login is NOT permission to access Archer's tasks or data. The RemoteGateway in the prerequisite branches remains **127.0.0.1:5093 only**, with readiness 503 and no `/v1/*` routes.

This source is **independent of the work computer**. It will require Xcode/macOS for iOS compilation, simulator execution and device signing. Native iOS development cannot be performed entirely in Windows Visual Studio without access to a Mac build host. Do not add Archer to a workplace computer.

## Auth0 tenant settings selected by owner

- Domain: `dev-1l3ic41kxj7zpeb8.us.auth0.com`
- App type: **Native**
- Native iOS Bundle ID: `com.gravybets.archer.dev`
- API audience: `https://archer.example/api/dev`
- Allowed callback URL: `com.gravybets.archer.dev://dev-1l3ic41kxj7zpeb8.us.auth0.com/ios/com.gravybets.archer.dev/callback`
- Allowed logout URL: the same callback URL
- Grant types: Authorization Code and Refresh Token (NOT Implicit, Password or Client Credentials)
- Owner login: verified Auth0 database user with passkeys available; the passkey must be enrolled on the owner's personal iPhone when ready.
- API scopes: `conversation:read` and `conversation:write`
- SMTP verification sender: separate personal Gmail account configured in Auth0; never store its app password here.

**Do not commit the owner's account `sub`, email address, any access/refresh token, client secret, SMTP password, signing key, Apple Team ID, or recovery code to GitHub.** The Native Client ID is a public identifier but the checked-in `Auth0.plist` deliberately uses a placeholder until the owner applies it locally.

## Build later on a personally controlled Mac

1. Install Xcode 15.3 or newer and XcodeGen; clone `GravyBets/Archer` on a personal machine only.
2. Open `ios/ArcherMobile/App/Auth0.plist`. Replace `REPLACE_WITH_AUTH0_NATIVE_APP_CLIENT_ID` with the **Client ID** shown in Auth0 **Applications → Archer iPhone Dev → Settings**. Do not include a Client Secret. Keep that local file change uncommitted until explicitly reviewed.
3. From `ios/ArcherMobile`, run `xcodegen generate` to produce `ArcherMobile.xcodeproj`; open it in Xcode and allow the package dependency to resolve.
4. Run the `ArcherMobile` scheme in an iPhone simulator first. Verify login redirects back to the app. The simulator browser flow is safe to test with the personal development account, but no real conversations or actions exist.
5. For an actual iPhone, select a legitimate personal Apple signing team in Xcode and keep the Bundle Identifier **exactly** `com.gravybets.archer.dev` (or coordinate a changed identifier and update the Auth0 URLs first). Apple ID free provisioning and distribution limitations may apply; TestFlight typically requires an Apple Developer Program membership.
6. Sign in via the Auth0 system browser, then if Auth0 offers a passkey, enroll it on the personal phone. Do not create a passkey on a workplace computer. On success the app only says the Auth0 session exists and that gateway access is disabled.
7. Test session renewal and logout. Confirm no token, callback code or private user profile is printed to logs, screenshots, git files or device preferences.

## Three iPhone milestones (source implementation, still not released)

1. **Correct Native Auth0 tenant:** the Native app domain and callback now use
   the same confirmed `dev-1l3ic41kxj7zpeb8.us.auth0.com` tenant as the
   already-tested Pocket browser client, instead of the incorrect older
   `dev-113ic...` spelling. The actual Native app Client ID still requires
   owner configuration; this does **not** connect the iPhone to Archer.
2. **Hardware-backed local device key:** on a real compatible iPhone, the
   signed-in app can explicitly create a Secure Enclave P-256 signing key,
   persist its opaque key reference using ThisDeviceOnly Keychain accessibility,
   show the public-key fingerprint, verify one local signature, and erase its
   local record after confirmation. If Secure Enclave is unavailable, no
   software fallback is silently generated. **This is not device enrollment**;
   the remote gateway has no production endpoint or owner approval.
3. **Owner-initiated local notification:** the signed-in app requests iOS
   notification permission only when the user taps **Allow test notifications**.
   Once granted, **Send local notification test** schedules one harmless local
   notification after five seconds. It reuses one stable identifier to prevent
   building up duplicate pending alerts. **This is not APNs**, an incoming
   remote alert, or a confirmed Archer incident.

These additions run in the iOS simulator build and static safety tests in
GitHub Actions. Secure Enclave creation and real notification delivery must
still be verified on the owner's iPhone after signing/distribution. Do not
describe the native app as installed, online, receiving Archer alerts or
able to execute missions.

## Security boundaries / why this is a prototype

- The Native client never ships a client secret. Auth0's official SDK manages PKCE, state and nonce; our app does not implement OAuth manually.
- The app uses the custom callback scheme already configured in Auth0; custom-scheme interception is a known platform risk. For stronger callback ownership and real distribution, plan associated-domain **HTTPS Universal Links** after you obtain Apple Developer signing, and update allowed URLs accordingly.
- The app does not send bearer credentials to a localhost gateway on the phone. When private cloud staging is ready, use an authenticated HTTPS endpoint only after validating issuer, JWKS signature, audience, expiry, Native client, exact server-side owner subject and device possession/revocation. Tokens are NEVER sufficient to authorize arbitrary missions alone.
- No device claim is currently issued by Auth0, because copying a client-supplied device ID into a signed JWT would be insecure. Device-key proof-of-possession and an owner-approved persistent enrollment protocol remain separate work.
- `credentials.canRenew()` and `credentials.hasValid()` only reflect local Keychain material. The app must **not** label that as a server-approved Archer device.
- If browser logout fails, local credentials are cleared anyway and the user is warned that the browser session may remain active. Local logout does not revoke every remote Auth0 refresh token or enrolled device.
- Auth0 access-token audience is a logical URI, **not a currently deployed website**.
- No Microsoft/Google/Copilot/Twilio APIs, calls, SMS, APNs, expensive services, public ports, production databases or automation are enabled.

## Validation

The Linux build runs `python3 tests/ValidateArcherIosAuth.py` to validate source and configuration contracts. A separate **macOS CI simulator build** compiles the SwiftUI app with the pinned official SDK; this proves Xcode source compatibility, not on-device Auth0 login or real token issuance.

Auth0 official SDK docs: https://github.com/auth0/Auth0.swift
Native Swift/iOS setup: https://auth0.com/docs/quickstart/native/ios-swift

## Private-server companion candidate (Batch Two)

The signed-in view now includes chat, work and inbox overview connected through the separate private gateway. Configure Gateway.plist with your exact private tailnet HTTPS hostname at build time; the checked-in placeholder does not permit networking. Approve the full public Secure Enclave key fingerprint in the private gateway environment. Auth0 login alone is insufficient. Requests use one-time proof over the method, route and exact JSON body. The client rejects redirects and keeps bearer credentials out of logs. Phone and desktop share the core conversation. The phone can send an explicit registered-project assignment through the same bounded command path and displays real task states. Interrupted requests use a durable receipt lookup; no automatic resend. Optional spoken replies require a suitable enhanced British male iOS voice. This is not background wake-word or realtime iPhone voice. APNs, Apple signing, real device verification and owner deployment remain outstanding. See ../../deploy/linux/README.md for private staging. The earlier milestones above describe historical local-only builds; the separate older RemoteGateway remains unchanged.
