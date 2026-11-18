# Archer Pocket — iPhone Home Screen web app (no Mac needed)

**Status:** Static owner-login preview deployed on GitHub Pages at
`https://gravybets.github.io/Archer/index.html`. Real Auth0 browser sign-in
was confirmed from the owner's iPhone on November 17, 2026. **This is still
not a connected or approved Archer phone client.**
This deliberately does **not** expose the ASP.NET Core desktop API or the
remote local-only gateway. There is no chat, device enrollment, money movement,
AI, SMS, calling, monitoring or mission-execution connection in this web app.

The source in this folder is suitable for static HTTPS hosting. It uses
Auth0's official `auth0-spa-js` CDN build **2.30**, Authorization Code with
PKCE, an exact same-origin HTTPS callback and `cacheLocation: 'memory'`.
An offline service worker caches **only public static UI assets**, not OAuth
callbacks, tokens, profile information, or any API response.

The user's `Archer iPhone Dev` **Native** client cannot be reused by this
browser-only PWA: create a separate **Single Page Application** called
`Archer Pocket Dev` in the existing Auth0 tenant.

## Finish configuration in the Auth0 dashboard

1. **Completed:** The owner created **Archer Pocket Dev** as a separate
   **Single Page Application** on November 17, 2026. Its *public*
   Client ID is checked into `config.js`. This is an OAuth identifier,
   never a client secret.
2. After the owner approves static public hosting, deploy this folder to an
   HTTPS static host such as GitHub Pages. Use the resulting **exact** HTTPS
   URL for callback/login and origins; do not guess URLs before hosting.
   For the planned `GravyBets/Archer` GitHub Pages project URL, configure:
   - Allowed Callback URLs: `https://gravybets.github.io/Archer/`
   - Allowed Logout URLs: `https://gravybets.github.io/Archer/`
   - Allowed Web Origins: `https://gravybets.github.io`
   Keep Cross-Origin Authentication OFF and Allowed Origins (CORS) blank.
   **As of the owner setup screenshot, GitHub Pages is NOT enabled, and
   the URL must not be represented as live until manual deployment succeeds.**
3. On **Applications → APIs → Archer Remote Gateway Dev →
   Application Access**, grant this new SPA client **User-delegated** access
   to `conversation:read` (and `conversation:write` only when messaging is
   actually implemented). Keep **Client Access** and **Anonymous Access**
   disabled. Application user sign-up stays disabled.
4. **Completed:** `config.js` now includes only the new SPA public **Client
   ID**, not the Native app Client ID. No client secret, Google App Password,
   Auth0 access/refresh
   tokens, exact personal owner `sub`, Apple ID, API keys or private
   certificates should ever be in that file. Public app Client IDs are
   intentionally readable by browsers.
5. Test a login using your own iPhone through Safari. If your account is
   allowed to create a passkey, complete enrollment on that personal iPhone,
   not on a workplace machine.

**Domain and audience are already pinned** to the owner-created dev Auth0
tenant and `https://archer.example/api/dev`. That audience is an API
identifier, not a functional HTTPS API destination.

## iPhone install, once a real HTTPS URL exists

1. Open the **direct working** HTTPS page
   `https://gravybets.github.io/Archer/index.html` in Safari on iPhone.
   The bare `/Archer/` address was observed returning GitHub Pages 404.
   The PWA manifest now explicitly launches `./index.html` to avoid it.
   Also ensure Auth0 **Allowed Callback URLs** and **Allowed Logout URLs**
   include the exact `https://gravybets.github.io/Archer/index.html` URL.
2. Tap **Share → Add to Home Screen** (or **Open as Web App**, when offered).
   Do this **after verifying the latest GitHub Pages deployment** includes the corrected manifest:
   earlier versions attempted to pre-cache `/Archer/` (404), preventing service
   worker installation, and launched Home Screen icons at the broken directory.
3. Launch Archer from the Home Screen.
4. Tap **Sign in with Auth0** to authenticate. A browser login might not
   survive being moved from Safari to the installed Home Screen app because
   the storage partitions can differ. Sign in again in the installed app.
5. The UI should say **Auth0 signed in** but **Approved device locked** and
   **Private messages locked**. No data or remote access will open.

Reference: https://support.apple.com/guide/iphone/bookmark-a-website-iph42ab2f3a7/ios

## Testing from Windows, without deploying

For shell inspection only, serve the `web/archer-pocket` directory via a
local static web server on your Windows development PC and visit
`http://127.0.0.1:<port>/` in its browser. The login button stays
disabled with the checked-in empty Client ID. Any real Auth0 browser callback
must match an allowlisted localhost URL and be tested separately. A browser
preview on Windows will **not** make the site reachable on iPhone.

Do **not** set ASP.NET Core `ASPNETCORE_URLS=0.0.0.0`, open port 5087/5091/
5093, port-forward your home router, or point a public reverse proxy at
Archer's desktop app. That local admin API does not have the security
properties required for Internet access.

## Publishing later

The public Archer Pocket GitHub Pages site was already approved and hosted.
The Pages workflow now runs on **verified `main` changes** to the public
`web/archer-pocket` shell or its own tests, and can still be triggered
manually. It runs Pocket security/readiness tests before uploading only
the public static site. It does **not** upload any desktop database,
API keys, chat history, provider credentials, device private keys or server
components. If the Pages environment requires reviewer approval, that
approval remains required before a deploy. Failed checks stop publishing.
Auth0 SPA credentials and callbacks must still be explicitly configured
on the actual deployed HTTPS origin.

We can also use a private deployment environment later, but a conventional
free static host is not a substitute for secure authenticated message
storage and remote API hosting.


## Device key preparation (local only; not enrollment)

After a successful Auth0 sign-in on your personal iPhone, Archer Pocket now
shows **Prepare this iPhone → Create device key**. It uses WebCrypto to generate
a P-256 ECDSA signing key. The private CryptoKey is **non-extractable** and
stored by the browser's structured-clone IndexedDB capability. A public-key
fingerprint and local-only signing self-test are available. No server calls,
push notifications, chat, actions or actual approval happen.

- A browser-generated non-extractable key is **NOT proof of a hardware-backed
  Secure Enclave key**. Apple/iOS might delete browser website storage.
  Clearing Safari data, switching Safari profiles or reinstalling the Home
  Screen app may destroy the key. Don't treat this as a long-term device ID
  until cross-session persistence has been tested on the actual phone.
- **Create device key** and **Test local proof** require being signed in to
  Auth0. They do not give Auth0 tokens or device proofs to a remote endpoint.
- **Erase local key** irreversibly clears the browser copy, but **does not
  revoke** a future server-side approval. Server revocation must remain a
  separately authenticated operation.
- For eventual pairing, an **authenticated server** must issue unpredictable
  single-use `ticketId` and `nonce` bound to the *server-validated*
  owner `sub`, send them over HTTPS, verify the public key's P-256 ECDSA
  signature using the exact `ARCHER_DEVICE_PAIR_P256_V1` payload, and
  require an explicit separately authenticated approval. The server must
  persist approved public keys and check revocation / scoped device proof
  on each protected request. Browser-provided owner strings are not trusted.
- The current remote gateway in open PRs is **localhost test-only**, not a
  deployment-ready host. There is deliberately no publicly callable pairing
  endpoint. A web-origin CryptoKey is also not sufficient against XSS; future
  staging needs audited CSP, replay-resistant requests, token lifetime checks,
  storage security, account recovery and real-device testing.

Tests: `python3 tests/ValidateArcherPocket.py`,
`node tests/PocketDeviceKeysChecks.mjs`.

## Future device-bound read request proof (not connected yet)

The Pocket key module now includes a **local-only** P-256 signer for a
server-issued, one-time `ARCHER_DEVICE_READ_PROOF_V1` challenge. The signed
payload binds the validated owner, enrolled device ID, exact GET resource,
short-lived ticket and nonce. Only the future read-only conversation and inbox
paths are allowlisted; the signer does not authorize mission execution,
GitHub changes, sending messages or payments.

The .NET remote gateway has a matching local-only verifier that checks the
stored public-key fingerprint and current revocation state before consuming a
one-time proof. This is **not a public phone service**, and the current
Pocket UI does not make network pairing requests. The owner must complete
server-side approval and secure HTTPS deployment before any private inbox
can become available. Never paste an access token or private key into chat.

## Security contract

- Auth0 login authenticates a browser session, not device possession and not
  authority to execute Archer missions.
- All authorization checks must be repeated server-side against signed
  issuer/JWKS, exact audience, exact owner `sub`, approved client, and a
  cryptographically enrolled/revocable device. Client UI badges are not
  security evidence.
- The PWA never requests, reads or displays user profile claims.
- Uses the Auth0 official SPA SDK; no custom implementation of OAuth/PKCE.
- Keeps OAuth credentials in the SDK's ephemeral in-memory cache rather than
  localStorage, cookies or IndexedDB.
- Offline UI uses an explicit public asset allowlist. The service worker
  does not intercept login callbacks containing query params.
- Signed-in PWA has no messaging or mission API endpoint. All action buttons
  stay absent/disabled.
- Without a Mac, installable web apps are realistic; native iOS distribution
  is a separate Apple signing pathway.
