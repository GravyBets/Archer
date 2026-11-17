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
            "authTag","connectionBadge","disabledMessage"]).issubset(page.ids),
       "identity and locked state affordances exist")
ensure('disabledMessage' in html and 'placeholder="Messaging is not connected" disabled' in html,
       "messaging is visibly unavailable")
ensure(manifest["display"] == "standalone", "installable PWA")
ensure(manifest["scope"] == "./" and manifest["start_url"] == "./", "relative static site paths")
ensure(manifest["icons"][0]["src"] == "./assets/archer-icon.svg", "same-origin icon")
ensure((site / "assets" / "archer-icon.svg").is_file(), "icon exists")
ensure("apple-mobile-web-app-capable" in html, "iPhone Home Screen support")
ensure(page.scripts == ["./config.js",
       "https://cdn.auth0.com/js/auth0-spa-js/2.30/auth0-spa-js.production.js",
       "./app.js"], "official pinned SPA browser SDK and local scripts")
ensure("Content-Security-Policy" in html, "CSP restricts cross-site resources")
ensure("window.ArcherPocketSettings" in config, "public-only config present")
ensure("clientId: 'Q25ebj5IT14zwo63SP7920S25m21Rjgm'" in config,
       "public Auth0 SPA Client ID matches the owner-provided development app")
ensure("dev-1l3ic41kxj7zpeb8.us.auth0.com" in config and
       "https://archer.example/api/dev" in config, "dev tenant and audience are exact")
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
ensure("FILES.slice(2)" in sw, "service worker allowlists only public assets")
ensure("config.js" not in sw and "caches.put" not in sw and
       "authorization" not in sw.lower(),
       "service worker cannot cache app config or credentials")
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
