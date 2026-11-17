#!/usr/bin/env python3
"""Cryptographic JWT contract smoke for Archer's LOCAL ONLY RemoteGateway.

All tokens and RSA keys are ephemeral synthetic test data. Private keys are
never written into the repository, API responses, logs or GitHub artifacts.
No external OIDC, Apple, provider account or server is accessed.
"""
import base64
import copy
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time

from SmokeRemoteGateway import ROOT, command, request, stop, wait_ready

ISSUER = "https://issuer.archer.test.invalid"
AUDIENCE = "archer.synthetic.api"
OWNER = "owner-synthetic-A"
DEVICE = "device-synthetic-1"
PROBE = f"/_local/identity-probe/{OWNER}"


def openssl(*args, input_data=None):
    result = subprocess.run(["openssl", *map(str, args)], input=input_data,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                            check=True)
    return result.stdout


def generate_keys(folder, label, bits=2048):
    private = folder / f"{label}.pem"
    public = folder / f"{label}.pub.pem"
    openssl("genpkey", "-algorithm", "RSA", "-pkeyopt",
            f"rsa_keygen_bits:{bits}", "-out", private)
    openssl("pkey", "-in", private, "-pubout", "-out", public)
    return private, public


def b64url(data):
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode("ascii")


def issue(private_key, claims=None, header=None):
    now = int(time.time())
    payload = {
        "iss": ISSUER, "aud": AUDIENCE, "sub": OWNER,
        "device_id": DEVICE, "scope": "conversation:read conversation:write",
        "iat": now, "nbf": now - 5, "exp": now + 300
    }
    if claims:
        payload.update(claims)
    header = header or {"alg": "RS256", "typ": "JWT"}
    encoded = ".".join(
        b64url(json.dumps(obj, separators=(",", ":"), sort_keys=True).encode())
        for obj in (header, payload))
    signature = openssl("dgst", "-sha256", "-sign", private_key,
                        input_data=encoded.encode())
    return encoded + "." + b64url(signature)


def api(token=None, method="GET", path=PROBE, extra_headers=None):
    headers = dict(extra_headers or {})
    if token is not None:
        headers["Authorization"] = "Bearer " + token
    return request(path, method=method, headers=headers)


def base_environment(public_key):
    env = os.environ.copy()
    for name in list(env):
        if name.startswith("ARCHER_REMOTE_TEST_"):
            env.pop(name)
    for name in ("ARCHER_REMOTE_PUBLIC_ENABLED", "ARCHER_REMOTE_BIND_ADDRESS"):
        env.pop(name, None)
    env.update({
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ARCHER_REMOTE_GATEWAY_MODE": "local-test",
        "ARCHER_REMOTE_TEST_JWT_PUBLIC_KEY_FILE": str(public_key),
        "ARCHER_REMOTE_TEST_JWT_ISSUER": ISSUER,
        "ARCHER_REMOTE_TEST_JWT_AUDIENCE": AUDIENCE,
        "ARCHER_REMOTE_TEST_OWNER_SUBJECT": OWNER,
        "ARCHER_REMOTE_TEST_DEVICE_ID": DEVICE,
        "ARCHER_REMOTE_TEST_DEVICE_REVOKED": "false",
    })
    return env


def run_server(env, checks):
    with tempfile.TemporaryFile(mode="w+t", encoding="utf-8") as log:
        process = subprocess.Popen(command(), cwd=ROOT, env=env,
                                   stdout=log, stderr=subprocess.STDOUT)
        try:
            try:
                wait_ready(process)
                checks()
            except Exception:
                log.seek(0)
                print(log.read()[-3000:])
                raise
        finally:
            stop(process)


def assert_bad_startup(env, needle):
    with tempfile.TemporaryFile(mode="w+t", encoding="utf-8") as log:
        process = subprocess.Popen(command(), cwd=ROOT, env=env,
                                   stdout=log, stderr=subprocess.STDOUT)
        try:
            exit_code = process.wait(timeout=14)
            assert exit_code != 0, "Unsafe synthetic JWT configuration accepted"
            log.seek(0)
            output = log.read()
            assert needle in output, output[-2000:]
        finally:
            stop(process)


def main():
    with tempfile.TemporaryDirectory(prefix="archer-synthetic-rs256-") as temp:
        folder = Path(temp)
        private, public = generate_keys(folder, "allowed")
        attacker_private, _ = generate_keys(folder, "attacker")
        env = base_environment(public)
        checks = 0

        def expect(label, expected, token=None, method="GET", path=PROBE, extra_headers=None):
            nonlocal checks
            status, data, _ = api(token, method=method, path=path,
                                  extra_headers=extra_headers)
            assert status == expected, f"{label}: expected {expected}, got {status}: {data}"
            checks += 1
            return data

        def verify_tokens():
            live = request("/health/live")[1]
            assert live["remoteAuthenticationEnabled"] is False
            assert live["signedSyntheticIdentityProbeEnabled"] is True
            assert request("/health/ready")[0] == 503
            assert request("/v1/conversations")[0] == 404
            assert request("/v1/me/devices")[0] == 404

            valid = issue(private)
            body = expect("valid signed owner/device reads", 200, valid)
            assert body["authenticated"] and body["deviceAuthorized"]
            assert body["syntheticOnly"] and not body["remoteAccessEnabled"]
            expect("valid signed owner/device writes no data", 204, valid, method="POST")
            expect("anonymous request", 401)
            expect("arbitrary garbage", 401, "not-a-jwt")
            expect("no signature", 401, valid.rsplit(".", 1)[0] + ".")
            expect("wrong signing key", 401, issue(attacker_private))
            expect("forged algorithm none", 401, issue(private, header={"alg": "none", "typ": "JWT"}))
            expect("issuer confusion", 401, issue(private, {"iss": "https://evil.test.invalid"}))
            expect("audience confusion", 401, issue(private, {"aud": "other-api"}))
            expect("expired credential", 401, issue(private, {"exp": int(time.time()) - 20}))
            expect("not-yet-valid credential", 401, issue(private, {"nbf": int(time.time()) + 100}))
            expect("different owner", 401, issue(private, {"sub": "owner-synthetic-B"}))
            expect("different device", 401, issue(private, {"device_id": "device-synthetic-2"}))
            expect("missing subject", 401, issue(private, {"sub": ""}))
            expect("missing device", 401, issue(private, {"device_id": ""}))
            expect("missing scope", 401, issue(private, {"scope": ""}))
            expect("wrong owner URL must not read", 403, valid,
                   path="/_local/identity-probe/owner-synthetic-B")
            expect("wrong owner URL must not write", 403, valid,
                   method="POST", path="/_local/identity-probe/owner-synthetic-B")
            read_only = issue(private, {"scope": "conversation:read"})
            expect("read scope can read", 200, read_only)
            expect("read scope cannot write", 403, read_only, method="POST")
            write_only = issue(private, {"scope": "conversation:write"})
            expect("write-only cannot read", 403, write_only)
            expect("write-only cannot write", 403, write_only, method="POST")
            expect("unrelated scope denied", 403, issue(private, {"scope": "inbox:read"}))
            expect("scope casing denied", 403, issue(private, {"scope": "Conversation:read"}))
            expect("host confusion denied", 403, valid,
                   extra_headers={"Host": "wrong.invalid:5093"})
            expect("browser origin denied", 403, valid,
                   extra_headers={"Origin": "https://wrong.invalid"})
            expect("forwarded credentials denied", 403, valid,
                   extra_headers={"X-Forwarded-For": "203.0.113.8"})

            # Change the claim without changing its original signature.
            header, payload, signature = valid.split(".")
            tampered = json.loads(base64.urlsafe_b64decode(payload + "=" * (-len(payload) % 4)))
            tampered["sub"] = "owner-synthetic-B"
            tampered_payload = b64url(json.dumps(tampered, separators=(",", ":")).encode())
            expect("modified signed payload denied", 401,
                   header + "." + tampered_payload + "." + signature)

        run_server(env, verify_tokens)
        revoked = env.copy()
        revoked["ARCHER_REMOTE_TEST_DEVICE_REVOKED"] = "true"
        run_server(revoked, lambda: expect("revoked enrolled device denied", 401, issue(private)))
        production = env.copy()
        production["ASPNETCORE_ENVIRONMENT"] = "Production"
        assert_bad_startup(production, "requires Development")
        missing = env.copy()
        missing.pop("ARCHER_REMOTE_TEST_JWT_AUDIENCE")
        assert_bad_startup(missing, "Incomplete or invalid")
        private_key_as_public = env.copy()
        private_key_as_public["ARCHER_REMOTE_TEST_JWT_PUBLIC_KEY_FILE"] = str(private)
        assert_bad_startup(private_key_as_public, "Only a public RSA signing key")
        invalid_revocation = env.copy()
        invalid_revocation["ARCHER_REMOTE_TEST_DEVICE_REVOKED"] = "maybe"
        assert_bad_startup(invalid_revocation, "Invalid synthetic device revocation")
        print(f"PASS: {checks} signed RS256 identity probe outcomes, rejected insecure configs, "
              "and real signature/expiry/issuer/audience/device/scope checks. No remote access.")


if __name__ == "__main__":
    main()
