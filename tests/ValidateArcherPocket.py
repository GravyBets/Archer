#!/usr/bin/env python3
"""Static fail-closed validation for Archer's iPhone web app.

No live Auth0 login, keys, phones, remote APIs or deployments are used.
"""
from pathlib import Path
from html.parser import HTMLParser
import json, re

site = Path(__file__).resolve().parents[1] / "web" / "archer-pocket"
html = (site / "index.html").read_text()
js = (site / "app.js").read_text()
sw = (site / "sw.js").read_text()
config = (site / "config.js").read_text()
readme = (site / "README.md").read_text()
manifest = json.loads((site / "manifest.webmanifest").read_text())

class Elements(HTMLParser):
    def __init__(self):
        super().__init__()
        self.ids = []
        self.scripts = []
    def handle_starttag(self, tag, attrs):
        d = dict(attrs)
        if "id" in d:
            self.ids.append(d["id"])
        if tag == "script" and "src" in d:
            self.scripts.append(d["src"])

page = Elements()
page.feed(html)
checks = 0

def ensure(value, description):
    global checks
    checks += 1
    assert value, description

ensure(len(page.ids) == len(set(page.ids)), "unique HTML IDs")
ensure(set(["identityMessage","signInButton","signOutButton","authCapability",
            "authTag","connectionBadge","disabledMessage",
            "deviceKeyStatus","deviceFingerprint","devicePrepareButton",
            "deviceSelfTestButton","deviceEraseButton","deviceCapability",
            "deviceTag"]).issubset(page.ids),
       "identity and locked state affordances exist")
ensure('disabledMessage' in html and 'placeholder="Messaging is not connected" disabled' in html,
       "messaging is visibly unavailable")
ensure(manifest["display"] == "standalone", "installable PWA")
ensure(manifest["scope"] == "./" and manifest["start_url"] == "./index.html",
       "Home Screen must launch working direct index.html, not GitHub Pages directory 404")
ensure(manifest["icons"][0]["src"] == "./assets/archer-icon.svg", "same-origin icon")
ensure((site / "assets" / "archer-icon.svg").is_file(), "icon exists")
ensure("apple-mobile-web-app-capable" in html, "iPhone Home Screen support")
ensure(page.scripts == ["./config.js",
       "https://cdn.auth0.com/js/auth0-spa-js/2.30/auth0-spa-js.production.js",
       "./device-keys.js", "./app.js"],
       "official pinned SPA browser SDK and local device key before application")
ensure("Content-Security-Policy" in html, "CSP restricts cross-site resources")
ensure("window.ArcherPocketSettings" in config, "public-only config present")
ensure("clientId: 'Q25ebj5IT14zwo63SP792OS25m21Rjgm'" in config,
       "public Auth0 SPA Client ID matches the owner-provided development app")
ensure("Q25ebj5IT14zwo63SP7920S25m21Rjgm" not in config,
       "reject numeral zero in place of Auth0 client ID capital O")
ensure("dev-1l3ic41kxj7zpeb8.us.auth0.com" in config and
       "https://archer.example/api/dev" in config, "dev tenant and audience are exact")
# Prevent the original numeral-one versus lowercase-ell regression: a correct
# config alone is insufficient if login JS or the page CSP still points at
# an invalid Auth0 host.
AUTH0_DOMAIN = "dev-1l3ic41kxj7zpeb8.us.auth0.com"
ensure(AUTH0_DOMAIN in js, "Auth0 login SDK guard uses the true tenant")
ensure(AUTH0_DOMAIN in html, "Auth0 page CSP permits the true tenant")
ensure("dev-113ic41kxj7zpeb8.us.auth0.com" not in
       (config + js + html), "old typo must not ship in any site asset")

ensure("validSettings()" in js and "window.isSecureContext" in js,
       "non-HTTPS remote access rejects initialization")
ensure("!client" in js, "unconfigured login button disabled")
ensure("createAuth0Client" in js and "loginWithRedirect" in js and
       "handleRedirectCallback" in js, "SDK performs Auth Code+PKCE and callback")
ensure("cacheLocation: 'memory'" in js and "useRefreshTokens: false" in js,
       "no persistent browser token storage")
ensure("window.history.replaceState" in js, "OAuth authorization code removed from URL")
ensure("window.location.origin + window.location.pathname" in js,
       "exact same-origin return URI")
ensure("conversation:read" in js and "conversation:write" not in js,
       "login preview requests read-only scope, not write actions")
ensure("device is not approved" in js.lower(), "browser sign-in not presented as device approval")
ensure("remote access remains disabled" in js.lower(), "remote access disabled")
ensure("url.search || url.hash" in sw, "OAuth callback query never intercepted")
ensure("event.request.mode === 'navigate'" in sw, "offline shell navigation handling")
ensure("const FILES = ['./index.html'" in sw and
       "const FILES = ['./', " not in sw,
       "install must never pre-cache GitHub Pages 404 directory and block PWA startup")
ensure("cache.addAll(FILES)" in sw,
       "precache uses only known-good page and static assets")

ensure("FILES.slice(1)" in sw, "service worker allowlists only public assets")
ensure("config.js" not in sw and "authorization" not in sw.lower() and
       "FILES.slice(1)" in sw and "if (url.search || url.hash) return;" in sw,
       "service worker caches only allowlisted assets, never OAuth redirects")
ensure("archer-pocket-shell-v4" in sw and
       "fetch(event.request)" in sw and
       ".catch(() => caches.match(event.request))" in sw,
       "mobile app fetches fresh login code after publishing fixes")
ensure("device-keys.js" in sw and "./device-keys.js" in html,
       "worker caches public device-key code and page loads it before app")
ensure("devicePrepare.addEventListener('click'" in js and
       "deviceKeys.create()" in js and
       "deviceKeys.selfTest" in js and
       "deviceKeys.erase()" in js,
       "local key creation, self-test and erase require explicit user action")
ensure("deviceTag.textContent = prepared && signedIn ? 'NOT ENROLLED'" in js,
       "key creation never declares a device approved")
ensure("readyDevice = null" in js and "if (!value) readyDevice = null" in js,
       "local browser sign-out clears volatile ready state")
device_code = (site / "device-keys.js").read_text()
for fragment in (
    "crypto?.subtle", "indexedDB.open(KEY_DB, 1)",
    "extractable !== false", "namedCurve: 'P-256'",
    "ARCHER_DEVICE_PAIR_P256_V1",
    "ARCHER_LOCAL_KEY_SELFTEST_V1",
    "const proof = await SUBTLE().sign(",
    "store.add(item, SLOT)", "store.delete(SLOT)"
):
    ensure(fragment in device_code, "must preserve device proof boundary: " + fragment)
for forbidden in ("fetch(", "XMLHttpRequest", "localStorage",
                  "clientSecret", "getAccessTokenSilently", "access_token",
                  "https://gravybets.github.io/Archer/api"):
    ensure(forbidden not in device_code,
           "local device key module must never access network, OAuth token or admin API")
ensure("No tokens" in readme or "tokens" in readme.lower(), "docs explain secrets policy")
ensure("Single Page Application" in readme, "requires distinct SPA app registration")
ensure("Archer iPhone Dev" in readme, "do not reuse Native app")
ensure("127.0.0.1" in readme and "0.0.0.0" in readme,
       "docs prohibit exposing desktop admin localhost")

for name in ("app.js","config.js","sw.js"):
    source = (site / name).read_text()
    for forbidden in (
        "localStorage.setItem(", "indexedDB.open(", "window.fetch(",
        "127.0.0.1:5087", "127.0.0.1:5093", "ARCHER_GITHUB_WRITE_TOKEN",
        "ARCHER_GITHUB_READ_TOKEN", "SMTP_PASSWORD", "clientSecret",
        "OPENAI_API_KEY", "X-Forwarded-Host"
    ):
        ensure(forbidden not in source, name + ": must never contain " + forbidden)

ensure(not re.search(r"src=\"http://",html), "no insecure script resources")
print(f"PASS: {checks} Archer Pocket static PWA, Auth0 PKCE and no-data safety checks")
