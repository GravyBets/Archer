#!/usr/bin/env python3
"""Provider-independent OIDC *configuration* and localhost probe smoke.

No real provider is contacted, no bearer token is supplied, and no
identity/phone/cloud service is activated. This tests safe wiring and disabled
routes, not end-to-end validation of a real provider's JWKS or PKCE flow.
"""
import os
from pathlib import Path
import subprocess
import tempfile

from SmokeRemoteGateway import ROOT, request
from SmokeRemoteSignedIdentity import (
    assert_bad_startup, generate_keys, run_server
)

REGISTRY_PROJECT = ROOT / "tests/RemoteDeviceRegistryChecks/RemoteDeviceRegistryChecks.csproj"
OWNER = "owner-synthetic-A"


def seed_registry(path):
    result = subprocess.run(
        ["dotnet", "run", "--no-build", "--configuration", "Release",
         "--project", str(REGISTRY_PROJECT), "--", "seed", str(path)],
        cwd=ROOT, capture_output=True, text=True, check=True, timeout=25
    )
    assert "ENROLLED_DEVICE_ID=" in result.stdout


def main():
    checks = 0

    def check(condition, label):
        nonlocal checks
        checks += 1
        assert condition, label

    with tempfile.TemporaryDirectory(prefix="archer-local-oidc-") as folder_name:
        folder = Path(folder_name)
        registry = folder / "synthetic-enrollment.sqlite"
        seed_registry(registry)

        env = os.environ.copy()
        for name in list(env):
            if name.startswith("ARCHER_REMOTE_OIDC_") or name.startswith("ARCHER_REMOTE_TEST_"):
                env.pop(name, None)
        env.pop("ARCHER_REMOTE_PUBLIC_ENABLED", None)
        env.pop("ARCHER_REMOTE_BIND_ADDRESS", None)
        env.update({
            "ASPNETCORE_ENVIRONMENT": "Development",
            "ARCHER_REMOTE_GATEWAY_MODE": "local-test",
            "ASPNETCORE_URLS": "http://0.0.0.0:5093",
            "ARCHER_REMOTE_OIDC_ENABLED": "local-validation",
            "ARCHER_REMOTE_OIDC_ISSUER": "https://identity.example.com/tenant/v2.0",
            "ARCHER_REMOTE_OIDC_AUDIENCE": "api://archer-mobile",
            "ARCHER_REMOTE_OIDC_OWNER_SUBJECT": OWNER,
            "ARCHER_REMOTE_OIDC_DEVICE_REGISTRY_FILE": str(registry),
        })

        def verify_disabled_capabilities():
            code, live, _ = request("/health/live")
            check(code == 200, "loopback liveness")
            check(live["oidcLocalValidationProbeEnabled"] is True, "local provider probe configured")
            check(live["remoteAuthenticationEnabled"] is False, "real remote auth remains disabled")
            check(live["publicAccessEnabled"] is False, "public access remains off")
            check(live["conversationEndpointsEnabled"] is False, "no conversations")
            code, ready, _ = request("/health/ready")
            check(code == 503 and ready["status"] == "blocked", "still not production-ready")
            for path in ("/v1/conversations", "/v1/me/devices", "/v1/login",
                         "/_local/identity-probe/" + OWNER, "/swagger"):
                check(request(path)[0] == 404, "unimplemented path must be absent: " + path)

            # Missing bearer is handled without contacting any identity provider.
            check(request("/_local/oidc-probe/" + OWNER)[0] == 401,
                  "uncredentialed owner probe requires a signed token")
            check(request("/_local/oidc-probe/" + OWNER, method="POST")[0] == 401,
                  "uncredentialed mutating probe denied")
            check(request("/_local/oidc-probe/" + OWNER,
                          headers={"Origin": "https://attacker.example"})[0] == 403,
                  "browser origin rejected even on authenticated route")
            check(request("/_local/oidc-probe/" + OWNER,
                          headers={"Host": "attacker.example:5093"})[0] == 403,
                  "malicious Host rejected")

        run_server(env, verify_disabled_capabilities)

        def refuse(changes, expected):
            bad = env.copy()
            bad.update(changes)
            assert_bad_startup(bad, expected)
            check(True, "invalid configuration rejects startup: " + str(changes))

        refuse({"ASPNETCORE_ENVIRONMENT": "Production"}, "local-test/Development")
        refuse({"ARCHER_REMOTE_GATEWAY_MODE": "production"}, "supports only local-test mode")
        refuse({"ARCHER_REMOTE_OIDC_ENABLED": "true"}, "explicit local-validation")
        refuse({"ARCHER_REMOTE_OIDC_ISSUER": "http://identity.example.com"}, "approved public HTTPS")
        refuse({"ARCHER_REMOTE_OIDC_ISSUER": "https://localhost"}, "approved public HTTPS")
        refuse({"ARCHER_REMOTE_OIDC_DEVICE_REGISTRY_FILE": str(folder / "absent.sqlite")},
               "existing absolute synthetic enrollment registry")

        # Opt-in to a synthetic static JWT fixture and the OIDC backend at
        # the same time must be a hard startup error, not a scheme fallback.
        _, public = generate_keys(folder, "synthetic-jwt")
        bad = env.copy()
        bad.update({
            "ARCHER_REMOTE_TEST_JWT_PUBLIC_KEY_FILE": str(public),
            "ARCHER_REMOTE_TEST_JWT_ISSUER": "https://synthetic-issuer.test.invalid",
            "ARCHER_REMOTE_TEST_JWT_AUDIENCE": "archer.synthetic.api",
            "ARCHER_REMOTE_TEST_OWNER_SUBJECT": OWNER,
            "ARCHER_REMOTE_TEST_DEVICE_ID": "A" * 48,
            "ARCHER_REMOTE_TEST_DEVICE_REVOKED": "false",
        })
        assert_bad_startup(bad, "cannot be enabled together")
        check(True, "two bearer identity systems cannot be activated together")

        print(f"PASS: {checks} local OIDC boundary checks. No provider discovery, external calls or public routes.")


if __name__ == "__main__":
    main()
