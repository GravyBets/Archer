#!/usr/bin/env python3
"""Local-only Auth0 owner-device API smoke: no real tokens, phones or secrets."""
import os
from pathlib import Path
from tempfile import TemporaryDirectory

from SmokeRemoteGateway import request
from SmokeRemoteSignedIdentity import run_server, assert_bad_startup

def main():
    with TemporaryDirectory(prefix="archer-local-pairing-") as path:
        env = {k: v for k, v in os.environ.items() if not k.startswith(
            ("ARCHER_REMOTE_AUTH0_", "ARCHER_REMOTE_PAIRING_",
             "ARCHER_REMOTE_TEST_", "ARCHER_REMOTE_OIDC_"))}
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
            "ARCHER_REMOTE_PAIRING_REGISTRY_FILE": str(Path(path) / "devices.sqlite"),
            "ARCHER_REMOTE_PAIRING_OPERATOR_SECRET":
                "LONG_SYNTHETIC_LOCAL_APPROVAL_SECRET_NEVER_USE_IN_PRODUCTION_2026"
        })
        checks = 0
        def check(condition, message):
            nonlocal checks
            checks += 1
            assert condition, message

        def test():
            code, body, _ = request("/health/live")
            check(code == 200 and body["localOwnerPairingEnabled"] is True,
                  "pairing available only in local-test opt-in")
            code, body, _ = request("/health/ready")
            check(code == 503 and body["publicAccessEnabled"] is False,
                  "pairing never makes public gateway production-ready")
            check(Path(env["ARCHER_REMOTE_PAIRING_REGISTRY_FILE"]).exists(),
                  "private test registry prepared")
            for route in ("/begin", "/prove", "/approve", "/revoke"):
                name = "/_local/pairing" + route
                status, _, _ = request(name, method="POST",
                    headers={"Content-Type": "application/json"},
                    body=b'{}')
                check(status == 401, "no Auth0 bearer: " + name)
                status, _, _ = request(name, method="POST",
                    headers={"Origin": "https://attacker.example",
                             "Content-Type": "application/json"},
                    body=b'{}')
                check(status == 403, "cross-origin denied: " + name)
                status, _, _ = request(name, method="POST",
                    headers={"X-Forwarded-Proto": "https",
                             "Content-Type": "application/json"},
                    body=b'{}')
                check(status == 403, "forged proxy header denied: " + name)
            for route in ("/v1/conversation/messages", "/v1/inbox", "/v1/missions"):
                check(request(route)[0] == 404, "public data still absent: " + route)
            check(request("/_local/pairing/begin", method="POST",
                          headers={"Host": "evil.example:5093"})[0] == 403,
                  "Host confusion rejected")
            check(request("/_local/auth0-owner-proof")[0] == 401,
                  "Auth0 owner probe still requires signed token")
        run_server(env, test)

        def reject(change, expected):
            candidate = dict(env)
            candidate.update(change)
            assert_bad_startup(candidate, expected)
            check(True, "unsafe pairing startup rejected")

        reject({"ARCHER_REMOTE_PAIRING_ENABLED": "true"},
               "Local pairing requires development mode")
        reject({"ARCHER_REMOTE_PAIRING_OPERATOR_SECRET": "bad"},
               "long, distinct local secret")
        reject({"ASPNETCORE_ENVIRONMENT": "Production"},
               "Development + local-test")
        reject({"ARCHER_REMOTE_GATEWAY_MODE": "public"},
               "supports only local-test")
        print(f"PASS: {checks} local owner pairing endpoints fail-closed, no token calls or remote access")

if __name__ == "__main__":
    main()
