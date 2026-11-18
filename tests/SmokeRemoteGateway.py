#!/usr/bin/env python3
"""Standalone localhost-only RemoteGateway safety smoke.

Launches a synthetic process without keys, phone data, AI calls or outside
network access. Uses Python stdlib only. Never publishes ports.
"""
import http.client
import json
import os
import socket
import subprocess
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "Archer.RemoteGateway" / "Archer.RemoteGateway.csproj"
HOST = "127.0.0.1"
PORT = 5093


def request(path, method="GET", headers=None, body=None):
    conn = http.client.HTTPConnection(HOST, PORT, timeout=2)
    try:
        conn.request(method, path, headers=headers or {}, body=body)
        response = conn.getresponse()
        raw = response.read()
        data = json.loads(raw) if raw and response.getheader("Content-Type", "").startswith("application/json") else None
        return response.status, data, dict(response.getheaders())
    finally:
        conn.close()


def stop(process):
    if process.poll() is not None:
        return
    process.terminate()
    try:
        process.wait(timeout=6)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait(timeout=6)


def command():
    return [
        "dotnet", "run", "--no-build", "--no-launch-profile",
        "--configuration", "Release", "--project", str(PROJECT)
    ]


def wait_ready(process):
    for _ in range(70):
        if process.poll() is not None:
            raise AssertionError("RemoteGateway stopped unexpectedly")
        try:
            status, _, _ = request("/health/live")
            if status == 200:
                return
        except (OSError, TimeoutError):
            pass
        time.sleep(0.2)
    raise AssertionError("Local RemoteGateway did not become live")


def verify_rejected_startup(name, env_updates):
    env = os.environ.copy()
    env.pop("ARCHER_REMOTE_PUBLIC_ENABLED", None)
    env.pop("ARCHER_REMOTE_BIND_ADDRESS", None)
    env.pop("ARCHER_REMOTE_GATEWAY_MODE", None)
    env.update(env_updates)
    with tempfile.TemporaryFile(mode="w+t", encoding="utf-8") as log:
        process = subprocess.Popen(command(), cwd=ROOT, env=env,
                                   stdout=log, stderr=subprocess.STDOUT)
        try:
            try:
                code = process.wait(timeout=12)
            except subprocess.TimeoutExpired as ex:
                raise AssertionError(f"{name} must fail startup, not listen") from ex
            assert code != 0, f"{name} incorrectly accepted remote config"
            log.seek(0)
            output = log.read()
            assert any(marker in output for marker in ("Refusing", "supports only local-test", "Remote binding configuration is forbidden")), output[-1200:]
        finally:
            stop(process)


def main():
    env = os.environ.copy()
    for name in ("ARCHER_REMOTE_PUBLIC_ENABLED", "ARCHER_REMOTE_BIND_ADDRESS",
                 "ARCHER_REMOTE_GATEWAY_MODE"):
        env.pop(name, None)
    # Adversarial host config: explicit Kestrel Listen must take precedence.
    env["ASPNETCORE_URLS"] = "http://0.0.0.0:5093"
    env["DOTNET_URLS"] = "http://0.0.0.0:5093"
    env["ASPNETCORE_ENVIRONMENT"] = "Production"

    with tempfile.TemporaryFile(mode="w+t", encoding="utf-8") as log:
        process = subprocess.Popen(command(), cwd=ROOT, env=env,
                                   stdout=log, stderr=subprocess.STDOUT)
        try:
            try:
                wait_ready(process)
            except Exception:
                log.seek(0)
                print(log.read()[-3000:])
                raise

            code, live, headers = request("/health/live")
            assert code == 200 and live["prototype"] is True, live
            for field in ("publicAccessEnabled", "remoteAuthenticationEnabled",
                          "signedSyntheticIdentityProbeEnabled",
                          "oidcLocalValidationProbeEnabled",
                          "auth0LocalValidationProbeEnabled",
                          "conversationEndpointsEnabled", "pushEnabled",
                          "aiEnabled", "providerCallsEnabled"):
                assert live[field] is False, (field, live)
            assert headers.get("Cache-Control") == "no-store"
            assert headers.get("X-Content-Type-Options") == "nosniff"
            assert headers.get("Referrer-Policy") == "no-referrer"
            assert "Server" not in headers

            code, ready, _ = request("/health/ready")
            assert code == 503 and ready["status"] == "blocked"
            assert ready["publicAccessEnabled"] is False

            for path in ("/_local/auth0-probe/owner-synthetic-A",
                         "/_local/identity-probe/owner-synthetic-A",
                         "/v1/conversations", "/v1/me/devices",
                         "/v1/push/register", "/api/state",
                         "/internal/devices", "/swagger", "/"):
                code, _, _ = request(path)
                assert code == 404, (path, code)
            for method in ("POST", "PUT", "DELETE"):
                code, _, _ = request("/v1/conversations", method)
                assert code == 404, (method, code)
            assert request("/health/live", method="POST")[0] in (404, 405)

            assert request("/health/live", headers={"Host": "attacker.example:5093"})[0] == 403
            assert request("/health/live", headers={"Host": "127.0.0.1:1234"})[0] == 403
            assert request("/health/live", headers={"Origin": "https://attacker.example"})[0] == 403
            assert request("/health/live", headers={"Forwarded": "for=192.0.2.10"})[0] == 403
            for header in ("X-Forwarded-For", "X-Forwarded-Host",
                           "X-Forwarded-Proto", "X-Real-IP"):
                assert request("/health/live", headers={header: "attacker.example"})[0] == 403, header

            # 127.0.0.2 is a different loopback interface on Linux CI.
            # A wildcard 0.0.0.0 binding would accept it.
            other = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            other.settimeout(1)
            try:
                accepted = other.connect_ex(("127.0.0.2", PORT)) == 0
                assert not accepted, "RemoteGateway bound a wildcard address"
            finally:
                other.close()
        finally:
            stop(process)

    verify_rejected_startup("public mode", {"ARCHER_REMOTE_PUBLIC_ENABLED": "true"})
    verify_rejected_startup("production mode", {"ARCHER_REMOTE_GATEWAY_MODE": "production"})
    verify_rejected_startup("remote bind", {"ARCHER_REMOTE_BIND_ADDRESS": "0.0.0.0"})
    print("PASS: RemoteGateway localhost isolation, blocked readiness, no mobile routes, unsafe config refusal")


if __name__ == "__main__":
    main()
