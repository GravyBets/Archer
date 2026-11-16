#!/usr/bin/env python3
"""AES-GCM gateway state regression: migration, wrong key, missing key, tampering.

All payloads are synthetic, all networking is loopback, and NO key or plaintext
sentinel is logged. Uses the already-built .NET Release gateway assembly.
"""
import base64
import json
import os
import secrets
import subprocess
import sys
import tempfile
import time
from datetime import datetime, timezone
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen
from uuid import uuid4


API = "http://127.0.0.1:5091"
ASSEMBLY = Path("Archer.MobileGateway/bin/Release/net10.0/Archer.MobileGateway.dll").resolve()
ADMIN = secrets.token_urlsafe(48)
INGEST = secrets.token_urlsafe(48)


def call(path, method="GET", data=None, header=None):
    body = json.dumps(data).encode("utf-8") if data is not None else None
    headers = {"Content-Type": "application/json"}
    if header:
        headers.update(header)
    req = Request(API + path, method=method, data=body, headers=headers)
    try:
        with urlopen(req, timeout=5) as response:
            return response.status, response.read()
    except HTTPError as error:
        return error.code, error.read()


def run_gateway(directory, key):
    env = dict(os.environ)
    env["ARCHER_GATEWAY_DATA_DIR"] = str(directory)
    env["ARCHER_GATEWAY_ADMIN_KEY"] = ADMIN
    env["ARCHER_GATEWAY_INGEST_KEY"] = INGEST
    env["ASPNETCORE_ENVIRONMENT"] = "Production"
    if key is None:
        env.pop("ARCHER_GATEWAY_DATA_KEY_HEX", None)
    else:
        env["ARCHER_GATEWAY_DATA_KEY_HEX"] = key

    process = subprocess.Popen(
        ["dotnet", str(ASSEMBLY)],
        env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL
    )
    for _ in range(90):
        if process.poll() is not None:
            raise AssertionError("Gateway stopped unexpectedly during encryption smoke.")
        try:
            status, _ = call("/health/live")
            if status == 200:
                return process
        except (URLError, TimeoutError):
            time.sleep(0.2)
    process.terminate()
    raise AssertionError("Gateway did not start on isolated loopback port.")


def stop(process):
    process.terminate()
    try:
        process.wait(timeout=8)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait(timeout=4)
    # Wait for socket release before starting a new process on the same port.
    for _ in range(40):
        try:
            call("/health/live")
            time.sleep(0.1)
        except (URLError, TimeoutError):
            break


def main():
    assert ASSEMBLY.exists(), "Build Release gateway before running encryption smoke."
    with tempfile.TemporaryDirectory(prefix="archer-encryption-ci-") as location:
        directory = Path(location)
        state_path = directory / "mobile-gateway-state.json"
        sentinel = "Migration smoke: owner-authored local journal entry"
        client_id = uuid4()
        message_id = uuid4()
        now = datetime.now(timezone.utc).isoformat()

        # Seed v1.5 recognizable plaintext on disk, then ensure the first
        # authenticated read migrates it before any data is returned.
        legacy = {
            "devices": [], "events": [], "audit": [],
            "conversationMessages": [{
                "id": str(uuid4()),
                "deviceId": str(client_id),
                "clientMessageId": str(message_id),
                "role": "owner",
                "content": sentinel,
                "createdAtUtc": now
            }]
        }
        state_path.write_text(json.dumps(legacy), encoding="utf-8")
        key = secrets.token_hex(32)
        wrong_key = secrets.token_hex(32)
        process = run_gateway(directory, key)
        try:
            assert call("/health/ready")[0] == 200
            assert call("/internal/devices", header={"X-Archer-Admin-Key": ADMIN})[0] == 200
            encrypted_file = state_path.read_bytes()
            payload = json.loads(encrypted_file)
            assert payload["format"] == "archer-gateway-aes256gcm-v1"
            assert sentinel.encode("utf-8") not in encrypted_file
            assert "conversationMessages" not in encrypted_file.decode("utf-8")

            status, raw = call(
                "/internal/devices/enroll", "POST",
                {"label": "Migration test phone", "lifetimeDays": 1,
                 "scopes": ["conversation:read"]},
                {"X-Archer-Admin-Key": ADMIN}
            )
            assert status == 200, status
            bearer = json.loads(raw)["deviceToken"]
            status, raw = call("/v1/conversation/messages",
                               header={"Authorization": "Bearer " + bearer})
            assert status == 200
            assert json.loads(raw)["messages"][0]["content"] == sentinel
            encrypted_file = state_path.read_bytes()
            assert sentinel.encode("utf-8") not in encrypted_file
        finally:
            stop(process)

        # An incorrect key cannot silently discard, reset or overwrite state.
        original = state_path.read_bytes()
        process = run_gateway(directory, wrong_key)
        try:
            assert call("/health/ready")[0] == 200  # key presence != key validity
            assert call("/internal/devices",
                        header={"X-Archer-Admin-Key": ADMIN})[0] == 500
            assert state_path.read_bytes() == original
        finally:
            stop(process)

        # Missing key permits liveness only; no state read or write.
        process = run_gateway(directory, None)
        try:
            assert call("/health/live")[0] == 200
            assert call("/health/ready")[0] == 503
            assert call("/internal/devices",
                        header={"X-Archer-Admin-Key": ADMIN})[0] == 500
            assert state_path.read_bytes() == original
        finally:
            stop(process)

        # Authenticated ciphertext tampering also fails closed.
        payload = json.loads(original)
        tag = bytearray(base64.b64decode(payload["tag"]))
        tag[0] ^= 1
        payload["tag"] = base64.b64encode(tag).decode("ascii")
        damaged = json.dumps(payload).encode("utf-8")
        state_path.write_bytes(damaged)
        process = run_gateway(directory, key)
        try:
            assert call("/internal/devices",
                        header={"X-Archer-Admin-Key": ADMIN})[0] == 500
            assert state_path.read_bytes() == damaged
        finally:
            stop(process)

        # Restore original ciphertext; same correct key recovers all history.
        state_path.write_bytes(original)
        process = run_gateway(directory, key)
        try:
            status, raw = call("/v1/conversation/messages",
                               header={"Authorization": "Bearer " + bearer})
            assert status == 200
            assert json.loads(raw)["messages"][0]["content"] == sentinel
        finally:
            stop(process)

        print("PASS: AES-256-GCM migration, authenticated recovery, missing/wrong key and tamper fail-closed")


if __name__ == "__main__":
    main()
