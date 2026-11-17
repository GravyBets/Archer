# Archer Pocket — iPhone Home Screen web app (no Mac needed)

**Status:** Static owner-login preview, NOT deployed, not a connected phone client.
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

1. Create **Applications → Applications → Create Application** named
   **Archer Pocket Dev**, type **Single Page Application**.
2. After the owner approves static public hosting, deploy this folder to an
   HTTPS static host such as GitHub Pages. Use the resulting **exact** HTTPS
   URL for callback/login and origins; do not guess URLs before hosting.
   For a site available at `https://example.test/archer/`, use:
   - Allowed Callback URLs: `https://example.test/archer/`
   - Allowed Logout URLs: `https://example.test/archer/`
   - Allowed Web Origins: `https://example.test`
   This example is **not** a working site.
3. On **Applications → APIs → Archer Remote Gateway Dev →
   Application Access**, grant this new SPA client **User-delegated** access
   to `conversation:read` (and `conversation:write` only when messaging is
   actually implemented). Keep **Client Access** and **Anonymous Access**
   disabled. Application user sign-up stays disabled.
4. In `config.js`, set only the new SPA public **Client ID**. No Native
   Client ID, client secret, Google App Password, Auth0 access/refresh
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

1. Open the HTTPS page in Safari on iPhone.
2. Tap **Share → Add to Home Screen** (or **Open as Web App**, when offered).
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

A separate, **manual only**, GitHub Pages workflow is included for
deployment **after the owner explicitly accepts that public static HTML
will be accessible to anyone**. It uploads only this static site and no
desktop database, API keys, prompts, logs, personal emails or tokens.
Auth0 SPA credentials and callbacks must still be explicitly configured
on the actual deployed HTTPS origin.

We can also use a private deployment environment later, but a conventional
free static host is not a substitute for secure authenticated message
storage and remote API hosting.

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
