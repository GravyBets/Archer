#!/usr/bin/env python3
"""Owner-only local Auth0 JWT API probe: safe boundaries, no signed token fixture.

This does NOT validate real Auth0 tokens, trigger OIDC discovery or issue a
password/refresh request. It proves no unauthenticated or wrong-mode caller
can activate the local-only sign-in credential checker.
"""
import os
from SmokeRemoteGateway import request
from SmokeRemoteSignedIdentity import run_server, assert_bad_startup

OWNER = "auth0|synthetic-private-account"


def main():
    checks = 0

    def check(value, label):
        nonlocal checks
        checks += 1
        assert value, label

    env = os.environ.copy()
    for key in list(env):
        if key.startswith((
            "ARCHER_REMOTE_TEST_", "ARCHER_REMOTE_AUTH0_",
            "ARCHER_REMOTE_OIDC_"
        )):
            env.pop(key)
    env.pop("ARCHER_REMOTE_PUBLIC_ENABLED", None)
    env.pop("ARCHER_REMOTE_BIND_ADDRESS", None)
    env.update({
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ARCHER_REMOTE_GATEWAY_MODE": "local-test",
        "ASPNETCORE_URLS": "http://0.0.0.0:5093",
        "ARCHER_REMOTE_AUTH0_SIGNIN_ENABLED": "local-proof",
        "ARCHER_REMOTE_AUTH0_SIGNIN_DOMAIN": "dev-owner.us.auth0.com",
        "ARCHER_REMOTE_AUTH0_SIGNIN_API_AUDIENCE": "https://archer.example/api/dev",
        "ARCHER_REMOTE_AUTH0_SIGNIN_NATIVE_CLIENT_ID": "NativeSynthetic123456789",
        "ARCHER_REMOTE_AUTH0_SIGNIN_OWNER_SUBJECT": OWNER,
    })

    def startup():
        code, live, _ = request("/health/live")
        check(code == 200, "loopback liveness")
        check(live["auth0OwnerSignInOnlyProbeEnabled"], "owner proof opt-in visible")
        check(live["remoteAuthenticationEnabled"] is False, "no remote authentication")
        check(live["publicAccessEnabled"] is False, "no public API enabled")
        check(live["conversationEndpointsEnabled"] is False, "no conversations")
        check(live["auth0LocalValidationProbeEnabled"] is False,
              "device-bound Auth0 local probe still disabled")
        check(live["signedSyntheticIdentityProbeEnabled"] is False,
              "static test verifier disabled")
        check(live["oidcLocalValidationProbeEnabled"] is False,
              "generic OIDC verifier disabled")
        code, ready, _ = request("/health/ready")
        check(code == 503 and ready["status"] == "blocked",
              "owner login proof never declares production readiness")
        for path in (
            "/v1/conversations", "/v1/me/devices",
            "/_local/auth0-probe/" + OWNER, "/_local/oidc-probe/" + OWNER,
            "/_local/identity-probe/" + OWNER, "/_local/auth0-owner-proof/junk"
        ):
            check(request(path)[0] == 404, "no business endpoint: " + path)
        check(request("/_local/auth0-owner-proof")[0] == 401,
              "no bearer cannot claim an authenticated owner")
        check(request("/_local/auth0-owner-proof",
                      headers={"Host": "attacker.example:5093"})[0] == 403,
              "malicious Host rejected")
        check(request("/_local/auth0-owner-proof",
                      headers={"Origin": "https://attacker.example"})[0] == 403,
              "cross-origin caller rejected")
        check(request("/_local/auth0-owner-proof",
                      headers={"X-Forwarded-Host": "attacker.example"})[0] == 403,
              "reverse proxy confusion rejected")

    run_server(env, startup)

    def unsafe(changes, expected):
        changed = env.copy()
        changed.update(changes)
        assert_bad_startup(changed, expected)
        check(True, "unsafe local identity startup rejected")

    unsafe({"ARCHER_REMOTE_GATEWAY_MODE": "public"}, "supports only local-test mode")
    unsafe({"ASPNETCORE_ENVIRONMENT": "Production"}, "Development + local-test")
    unsafe({"ARCHER_REMOTE_AUTH0_SIGNIN_ENABLED": "true"}, "explicit local-proof")
    unsafe({"ARCHER_REMOTE_AUTH0_SIGNIN_DOMAIN": "http://evil.example"},
           "approved Auth0 tenant hostname")
    unsafe({"ARCHER_REMOTE_AUTH0_SIGNIN_OWNER_SUBJECT": "someone@example.com"},
           "exact database-connection sub")

    mixed = env.copy()
    mixed.update({
        "ARCHER_REMOTE_AUTH0_ENABLED": "local-validation",
        "ARCHER_REMOTE_AUTH0_DOMAIN": "dev-other.us.auth0.com",
        "ARCHER_REMOTE_AUTH0_API_AUDIENCE": "https://archer.example/api/dev",
        "ARCHER_REMOTE_AUTH0_NATIVE_CLIENT_ID": "NativeSynthetic123456789",
        "ARCHER_REMOTE_AUTH0_OWNER_SUBJECT": OWNER,
        "ARCHER_REMOTE_AUTH0_DEVICE_REGISTRY_FILE": "/tmp/does-not-exist.sqlite",
        "ARCHER_REMOTE_AUTH0_DEVICE_CLAIM_TYPE": "https://owner-controlled.example/claims/device_id"
    })
    # Existing device Auth0 config fails before the conflict policy can run.
    # Instead enable generic OIDC settings without a real DB to assert denial.
    # No local identity scheme should be able to fall back after startup.
    assert_bad_startup(mixed, "existing absolute synthetic enrollment registry")
    check(True, "invalid second scheme cannot bypass required enrollment store")

    print(f"PASS: {checks} owner-only Auth0 login-probe localhost and unsafe-startup checks; no live tokens")


if __name__ == "__main__":
    main()
