#!/usr/bin/env python3
"""Test signed synthetic JWTs against durable SQLite enrollment state.

No OIDC server, real users, phone data, networking beyond loopback,
production keys or paid actions. All signing keys and DB files are temporary.
"""
from pathlib import Path
import os
import subprocess
import tempfile

from SmokeRemoteGateway import ROOT, request
from SmokeRemoteSignedIdentity import (
    OWNER, base_environment, generate_keys, issue, api, run_server,
    assert_bad_startup,
)

PROJECT = ROOT / "tests" / "RemoteDeviceRegistryChecks" / "RemoteDeviceRegistryChecks.csproj"


def registry_cli(*args):
    run = subprocess.run(
        ["dotnet", "run", "--no-build", "--configuration", "Release",
         "--project", str(PROJECT), "--", *map(str, args)],
        cwd=ROOT, capture_output=True, text=True, timeout=25, check=True,
    )
    return run.stdout


def status(token, method="GET"):
    return api(token, method=method)[0]


def main():
    assertions = 0
    def check(value, reason):
        nonlocal assertions
        assertions += 1
        assert value, reason

    with tempfile.TemporaryDirectory(prefix="archer-durable-id-") as temp:
        folder = Path(temp)
        private, public = generate_keys(folder, "authorized")
        db = folder / "approved-device.sqlite"

        output = registry_cli("seed", db)
        rows = [line for line in output.splitlines()
                if line.startswith("ENROLLED_DEVICE_ID=")]
        check(len(rows) == 1, "synthetic pairing tool returned one enrolled ID")
        device = rows[0].split("=", 1)[1]
        check(len(device) == 48 and device.isalnum(),
              "generated device ID is opaque and non-human-identifying")

        env = base_environment(public)
        env["ARCHER_REMOTE_TEST_DEVICE_ID"] = device
        env["ARCHER_REMOTE_TEST_DEVICE_REGISTRY_FILE"] = str(db)
        token = issue(private, {"device_id": device})

        def first_process():
            check(request("/health/live")[1]["remoteAuthenticationEnabled"] is False,
                  "real remote auth still disabled")
            check(request("/health/ready")[0] == 503,
                  "service still refuses production readiness")
            check(request("/v1/conversations")[0] == 404,
                  "no real messages API")
            check(status(token) == 200,
                  "signed enrolled device accepted before revocation")
            check(status(token, method="POST") == 204,
                  "enrolled device with approved write scope accepted for no-data POST")

            read_only = issue(private, {
                "device_id": device, "scope": "conversation:read"
            })
            check(status(read_only) == 200,
                  "signed read-only bearer can read with enrollment")
            check(status(read_only, method="POST") == 403,
                  "signed token cannot gain database's write scope")
            incorrect_device = issue(private, {"device_id": "A" * 48})
            check(status(incorrect_device) == 401,
                  "JWT without matching enrolled identity denied")

            result = registry_cli("revoke", db, device)
            check("REVOKED=true" in result, "separate process revoked persisted device")
            check(status(token) == 401,
                  "existing previously valid JWT denied immediately after revocation")
            check(status(token, method="POST") == 401,
                  "previously valid write JWT denied after revocation")

        run_server(env, first_process)

        def after_restart():
            check(status(token) == 401,
                  "revoked token still denied after gateway process restart")
            check(status(token, method="POST") == 401,
                  "revoked write scope still denied after gateway process restart")
            check(request("/health/ready")[0] == 503,
                  "revocation does not make service production-ready")

        run_server(env, after_restart)

        missing = env.copy()
        missing["ARCHER_REMOTE_TEST_DEVICE_REGISTRY_FILE"] = str(folder / "missing.sqlite")
        assert_bad_startup(missing, "requires an existing synthetic enrollment registry")

        # Database disappearance after startup must NOT fall back to the
        # synthetic JWT's signed scope or static device environment.
        another_db = folder / "missing-after-start.sqlite"
        output = registry_cli("seed", another_db)
        second_device = [x.split("=", 1)[1] for x in output.splitlines()
                         if x.startswith("ENROLLED_DEVICE_ID=")][0]
        second_env = env.copy()
        second_env["ARCHER_REMOTE_TEST_DEVICE_REGISTRY_FILE"] = str(another_db)
        second_env["ARCHER_REMOTE_TEST_DEVICE_ID"] = second_device
        second_token = issue(private, {"device_id": second_device})

        def missing_midflight():
            check(status(second_token) == 200,
                  "second enrolled test device initially accepted")
            os.unlink(another_db)
            check(status(second_token) == 401,
                  "database disappearance denies still-valid signed JWT")

        run_server(second_env, missing_midflight)

        print(f"PASS: {assertions} signed owner/device + SQLite persistence outcomes; "
              "revocation across requests and process restarts, missing database denied.")


if __name__ == "__main__":
    main()
