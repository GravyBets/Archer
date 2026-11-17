#!/usr/bin/env python3
"""Offline static safety contract for Archer's Auth0 iPhone login prototype.

This does not claim to validate a signed token or execute OAuth on a device.
"""
from pathlib import Path
import plistlib
import re

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "ios" / "ArcherMobile" / "App"
SPEC = (APP.parent / "project.yml").read_text()
INFO = plistlib.loads((APP / "Info.plist").read_bytes())
AUTH = plistlib.loads((APP / "Auth0.plist").read_bytes())
CONFIG = (APP / "ArcherAuthConfiguration.swift").read_text()
SESSION = (APP / "ArcherAuthSession.swift").read_text()
VIEW = (APP / "ArcherWelcomeView.swift").read_text()
README = (APP.parent / "README.md").read_text()

EXPECTED_BUNDLE = "com.gravybets.archer.dev"
EXPECTED_AUDIENCE = "https://archer.example/api/dev"
EXPECTED_DOMAIN = "dev-113ic41kxj7zpeb8.us.auth0.com"
checks = 0


def ensure(condition, explanation):
    global checks
    checks += 1
    assert condition, explanation


ensure("exactVersion: 3.3.1" in SPEC, "stable Auth0.swift version pinned")
ensure("https://github.com/auth0/Auth0.swift" in SPEC, "official SDK dependency")
ensure("PRODUCT_BUNDLE_IDENTIFIER: " + EXPECTED_BUNDLE in SPEC, "bundle ID pinned")
ensure("iOS: \"17.0\"" in SPEC, "iOS minimum version pinned")
scheme = INFO["CFBundleURLTypes"][0]["CFBundleURLSchemes"]
ensure(scheme == [EXPECTED_BUNDLE], "only authorized callback URL scheme registered")
ensure("REPLACE_WITH_AUTH0_NATIVE_APP_CLIENT_ID" == AUTH["ClientId"],
       "real client ID not inadvertently published")
ensure(AUTH["Domain"] == EXPECTED_DOMAIN, "tenant domain matches configured local Auth0")
ensure(EXPECTED_BUNDLE in CONFIG, "bundle is checked at runtime")
ensure(EXPECTED_AUDIENCE in CONFIG, "Auth0 custom API audience is explicitly requested")
ensure(EXPECTED_DOMAIN in CONFIG, "app and tenant config must match")
ensure("offline_access conversation:read conversation:write" in CONFIG,
       "expected minimal user delegated scope request")
ensure("Auth0.webAuth()" in SESSION, "system browser Auth0 flow used")
ensure(".audience(ArcherAuthConfiguration.apiAudience)" in SESSION,
       "Native authorization request includes audience")
ensure(".scope(ArcherAuthConfiguration.requestedScope)" in SESSION,
       "scopes passed to Auth0")
ensure(".start()" in SESSION, "official Auth0 PKCE WebAuth flow")
ensure("CredentialsManager(authentication: Auth0.authentication())" in SESSION,
       "Auth0 Keychain CredentialsManager used")
ensure("credentials.store(credentials: result)" in SESSION,
       "credentials stored via official Keychain manager")
ensure("credentials.credentials(minTTL: 60)" in SESSION,
       "session renewal uses credentials manager")
ensure("credentials.clear()" in SESSION, "logout attempts Keychain clear")
ensure("Auth0.webAuth().logout()" in SESSION, "browser session logout attempted")
ensure("ArcherAuthConfiguration.configured" in SESSION,
       "sign-in disabled when local Client ID unset")
ensure(".disabled(!ArcherAuthConfiguration.configured)" in VIEW,
       "button disabled when Client ID is placeholder")
ensure("Gateway access still awaits" in SESSION or "gateway access still" in SESSION,
       "server authorization never inferred from client sign-in")
ensure("Device enrollment" in VIEW, "device gate clearly disclosed")
ensure("Remote conversations: unavailable" in VIEW, "chat routes not falsely represented")
ensure("no token" in README.lower(), "readme documents token-redaction policy")
ensure("client secret" in README.lower(), "README rejects client secret")

swift_source = "\n".join(x.read_text() for x in APP.glob("*.swift"))
for prohibited in (
    "print(credentials", "print(result", "print(error", "UserDefaults",
    "UIPasteboard", "WKWebView", "client_secret", "grant_type=password",
    "clientCredentials", "http://", "localhost:5093"
):
    ensure(prohibited not in swift_source,
           "iPhone source must not expose tokens or unsupported auth: " + prohibited)
ensure(re.search(r"scope.*=\s*\n?\s*\"openid", CONFIG) is not None,
       "request a real ID token with openid")
print(f"PASS: {checks} offline iOS owner-login, callback, secret-handling and permission gates")
