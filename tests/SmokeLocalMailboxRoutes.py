#!/usr/bin/env python3
"""No-token HTTP proof that private mailbox routes remain localhost/owner locked."""
import os
from pathlib import Path
from tempfile import TemporaryDirectory

from SmokeRemoteGateway import request
from SmokeRemoteSignedIdentity import run_server, assert_bad_startup

def main():
    with TemporaryDirectory(prefix="archer-local-mailbox-") as folder:
        env = {key: value for key, value in os.environ.items()
               if not key.startswith(("ARCHER_REMOTE_AUTH0_",
                                      "ARCHER_REMOTE_PAIRING_",
                                      "ARCHER_REMOTE_MAILBOX_",
                                      "ARCHER_REMOTE_OIDC_",
                                      "ARCHER_REMOTE_TEST_"))}
        env.pop("ARCHER_REMOTE_PUBLIC_ENABLED", None)
        env.pop("ARCHER_REMOTE_BIND_ADDRESS", None)
        env.update({
            "ASPNETCORE_ENVIRONMENT": "Development",
            "ARCHER_REMOTE_GATEWAY_MODE": "local-test",
            "ASPNETCORE_URLS": "http://0.0.0.0:5093",
            "ARCHER_REMOTE_AUTH0_SIGNIN_ENABLED": "local-proof",
            "ARCHER_REMOTE_AUTH0_SIGNIN_DOMAIN": "dev-synthetic.us.auth0.com",
            "ARCHER_REMOTE_AUTH0_SIGNIN_API_AUDIENCE": "https://archer.example/api/dev",
            "ARCHER_REMOTE_AUTH0_SIGNIN_NATIVE_CLIENT_ID": "NativeSynthetic123456789",
            "ARCHER_REMOTE_AUTH0_SIGNIN_OWNER_SUBJECT": "auth0|synthetic-owner",
            "ARCHER_REMOTE_PAIRING_ENABLED": "local-only",
            "ARCHER_REMOTE_PAIRING_REGISTRY_FILE": str(Path(folder) / "devices.sqlite"),
            "ARCHER_REMOTE_PAIRING_OPERATOR_SECRET":
                "LONG_SYNTHETIC_LOCAL_APPROVAL_SECRET_NEVER_USE_IN_PRODUCTION_2026",
            "ARCHER_REMOTE_MAILBOX_ENABLED": "local-readonly",
            "ARCHER_REMOTE_MAILBOX_FILE": str(Path(folder) / "mailbox.sqlite"),
            "ARCHER_REMOTE_MAILBOX_KEY_HEX":
                "AF2A4BC3D5E67890B12CCDE421FFE050C3029719A8BB7465CCDAE81FF0B93762"
        })
        checks = 0
        def check(passed, label):
            nonlocal checks
            checks += 1
            assert passed, label

        def verify():
            code, data, _ = request("/health/live")
            check(code == 200 and data["localEncryptedReadOnlyMailboxEnabled"] is True,
                  "local-only mailbox configured")
            check(data["publicAccessEnabled"] is False and
                  data["remoteAuthenticationEnabled"] is False,
                  "local mailbox does not activate remote service")
            code, data, _ = request("/health/ready")
            check(code == 503 and data["status"] == "blocked",
                  "public readiness stays blocked")
            check(Path(env["ARCHER_REMOTE_MAILBOX_FILE"]).exists(),
                  "encrypted private store initialized, not a public location")
            for route, method in (
                ("/_local/mailbox/challenge", "POST"),
                ("/_local/mailbox/enqueue", "POST"),
                ("/v1/conversation/messages", "GET")):
                headers = {"Content-Type": "application/json"}
                code, _, _ = request(route, method=method, headers=headers,
                                     body=b'{}' if method == "POST" else None)
                check(code == 401, "no Auth0 bearer cannot read: " + route)
                headers["Origin"] = "https://attacker.example"
                code, _, _ = request(route, method=method, headers=headers,
                                     body=b'{}' if method == "POST" else None)
                check(code == 403, "cross-site origin blocked: " + route)
                headers.pop("Origin")
                headers["X-Forwarded-Host"] = "public.example"
                code, _, _ = request(route, method=method, headers=headers,
                                     body=b'{}' if method == "POST" else None)
                check(code == 403, "reverse proxy spoof blocked: " + route)
            for route in ("/v1/conversation/messages/send", "/v1/missions/run",
                          "/v1/calls", "/v1/device/approve"):
                check(request(route)[0] == 404, "no phone writes or AI: " + route)
        run_server(env, verify)

        def deny(updates, needle):
            bad = dict(env)
            bad.update(updates)
            assert_bad_startup(bad, needle)
            check(True, "unsafe mailbox startup failed")
        deny({"ARCHER_REMOTE_MAILBOX_ENABLED": "public"},
             "Encrypted mailbox requires explicit Development")
        deny({"ARCHER_REMOTE_MAILBOX_KEY_HEX": "wrong"},
             "distinct 256-bit server key")
        deny({"ARCHER_REMOTE_PAIRING_ENABLED": "false"},
             "Local pairing requires development mode")
        print(f"PASS: {checks} read-only encrypted mailbox routes require owner "
              "authentication and forbid remote/proxy and phone writes")

if __name__ == "__main__":
    main()
