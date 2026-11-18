#!/usr/bin/env python3
"""Auth0 selection smoke: local configuration, disabled public routes, no IdP calls.

Creates only synthetic temporary test data; never registers a real tenant,
never handles an actual Auth0 credential and never reaches the internet.
"""
import os
from pathlib import Path
import subprocess
import tempfile

from SmokeRemoteGateway import ROOT, request
from SmokeRemoteSignedIdentity import assert_bad_startup, generate_keys, run_server

REGISTRY_PROJECT = ROOT / "tests" / "RemoteDeviceRegistryChecks" / "RemoteDeviceRegistryChecks.csproj"
OWNER = "auth0|synthetic-owner"


def main():
    assertions = 0

    def check(ok, message):
        nonlocal assertions
        assertions += 1
        assert ok, message

    with tempfile.TemporaryDirectory(prefix="archer-auth0-local-") as folder_name:
        folder = Path(folder_name)
        registry = folder / "approved-synthetic-devices.sqlite"
        seeded = subprocess.run(
            ["dotnet", "run", "--no-build", "--configuration", "Release",
             "--project", str(REGISTRY_PROJECT), "--", "seed", str(registry)],
            cwd=ROOT, capture_output=True, text=True, timeout=25, check=True
        )
        check("ENROLLED_DEVICE_ID=" in seeded.stdout,
              "synthetic registry seeded without any Auth0 account")

        env = os.environ.copy()
        for name in list(env):
            if name.startswith(("ARCHER_REMOTE_TEST_", "ARCHER_REMOTE_OIDC_", "ARCHER_REMOTE_AUTH0_")):
                env.pop(name)
        env.pop("ARCHER_REMOTE_PUBLIC_ENABLED", None)
        env.pop("ARCHER_REMOTE_BIND_ADDRESS", None)
        env.update({
            "ASPNETCORE_ENVIRONMENT": "Development",
            "ARCHER_REMOTE_GATEWAY_MODE": "local-test",
            "ASPNETCORE_URLS": "http://0.0.0.0:5093",
            "ARCHER_REMOTE_AUTH0_ENABLED": "local-validation",
            "ARCHER_REMOTE_AUTH0_DOMAIN": "dev-archer-example.us.auth0.com",
            "ARCHER_REMOTE_AUTH0_API_AUDIENCE": "https://api.archer.example/remote",
            "ARCHER_REMOTE_AUTH0_NATIVE_CLIENT_ID": "ExampleNativeClient12345678",
            "ARCHER_REMOTE_AUTH0_OWNER_SUBJECT": OWNER,
            "ARCHER_REMOTE_AUTH0_DEVICE_REGISTRY_FILE": str(registry),
            "ARCHER_REMOTE_AUTH0_DEVICE_CLAIM_TYPE":
                "https://controlled.example/archer/device_id"
        })

        def verify():
            code, live, _ = request("/health/live")
            check(code == 200 and live["auth0LocalValidationProbeEnabled"] is True,
                  "Auth0 validation fixture activates only local-only probe")
            check(live["remoteAuthenticationEnabled"] is False,
                  "public auth remains disabled")
            check(live["publicAccessEnabled"] is False,
                  "public access remains disabled")
            check(live["signedSyntheticIdentityProbeEnabled"] is False,
                  "other identity mode disabled")
            check(live["oidcLocalValidationProbeEnabled"] is False,
                  "generic OIDC probe disabled")
            check(live["conversationEndpointsEnabled"] is False,
                  "no message or chat routes")
            code, body, _ = request("/health/ready")
            check(code == 503 and body["status"] == "blocked",
                  "production readiness still blocked")
            for path in (
                "/", "/v1/me/devices", "/v1/login",
                "/v1/conversations", "/v1/conversations/123/messages",
                "/_local/oidc-probe/" + OWNER,
                "/_local/identity-probe/" + OWNER
            ):
                check(request(path)[0] == 404, "unregistered route: " + path)
            check(request("/_local/auth0-probe/" + OWNER)[0] == 401,
                  "missing signed Auth0 JWT cannot authenticate")
            check(request("/_local/auth0-probe/" + OWNER,
                          method="POST")[0] == 401,
                  "missing bearer cannot write even to no-op probe")
            check(request("/_local/auth0-probe/" + OWNER,
                          headers={"Host": "evil.example:5093"})[0] == 403,
                  "host confusion denied")
            check(request("/_local/auth0-probe/" + OWNER,
                          headers={"Origin": "https://evil.example"})[0] == 403,
                  "cross-origin requests blocked")
            check(request("/_local/auth0-probe/" + OWNER,
                          headers={"X-Forwarded-Host": "evil.example"})[0] == 403,
                  "spoofed proxy host blocked")

        run_server(env, verify)

        def refuse(changes, error):
            bad = env.copy()
            bad.update(changes)
            assert_bad_startup(bad, error)
            check(True, "unsafe startup rejected: " + str(changes))

        refuse({"ARCHER_REMOTE_AUTH0_ENABLED": "production"},
               "explicit Development + local-test + local-validation")
        refuse({"ASPNETCORE_ENVIRONMENT": "Production"},
               "explicit Development + local-test + local-validation")
        refuse({"ARCHER_REMOTE_GATEWAY_MODE": "production"},
               "supports only local-test mode")
        refuse({"ARCHER_REMOTE_AUTH0_DOMAIN": "localhost"},
               "Auth0 Domain must be")
        refuse({"ARCHER_REMOTE_AUTH0_DOMAIN": "https://bad.evil.net"},
               "Auth0 Domain must be")
        refuse({"ARCHER_REMOTE_AUTH0_DEVICE_CLAIM_TYPE": "device_id"},
               "device claim must be a namespaced HTTPS URI")
        refuse({"ARCHER_REMOTE_AUTH0_DEVICE_REGISTRY_FILE":
                str(folder / "missing.sqlite")},
               "existing absolute synthetic enrollment registry")

        # All identity modes must remain exclusive. The fixture has no
        # credentials or real Auth0 token, and no external discovery runs.
        both = env.copy()
        both.update({
            "ARCHER_REMOTE_OIDC_ENABLED": "local-validation",
            "ARCHER_REMOTE_OIDC_ISSUER": "https://id.example.com/",
            "ARCHER_REMOTE_OIDC_AUDIENCE": "api://second-verifier",
            "ARCHER_REMOTE_OIDC_OWNER_SUBJECT": OWNER,
            "ARCHER_REMOTE_OIDC_DEVICE_REGISTRY_FILE": str(registry)
        })
        assert_bad_startup(both, "Local identity modes are mutually exclusive")
        check(True, "Auth0 and general OIDC verifier cannot both register")

        _, public = generate_keys(folder, "synthetic")
        conflicting = env.copy()
        conflicting.update({
            "ARCHER_REMOTE_TEST_JWT_PUBLIC_KEY_FILE": str(public),
            "ARCHER_REMOTE_TEST_JWT_ISSUER": "https://test-id.issuer.invalid",
            "ARCHER_REMOTE_TEST_JWT_AUDIENCE": "test-api",
            "ARCHER_REMOTE_TEST_OWNER_SUBJECT": OWNER,
            "ARCHER_REMOTE_TEST_DEVICE_ID": "A" * 48
        })
        assert_bad_startup(conflicting, "Local identity modes are mutually exclusive")
        check(True, "Auth0 and synthetic signing fixture cannot both register")
        print(f"PASS: {assertions} Auth0 local-only safety checks; no Auth0 account, keys or external calls.")


if __name__ == "__main__":
    main()
