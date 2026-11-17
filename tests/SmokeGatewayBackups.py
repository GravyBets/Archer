#!/usr/bin/env python3
"""Offline encrypted backup and restore acceptance test.

Uses a generated AES key and synthetic local message only. No real secrets,
phone service, cloud server or user's machine are accessed.
"""
import json
import os
import secrets
import subprocess
import tempfile
from pathlib import Path
from uuid import uuid4

from SmokeGatewayEncryption import run_gateway, stop, call, ADMIN


TOOL = Path("Archer.GatewayBackup/bin/Release/net10.0/Archer.GatewayBackup.dll").resolve()


def invoke(command, source, target=None, key=None, success=True, new_key=None):
    env = dict(os.environ)
    env.pop("ARCHER_GATEWAY_NEW_DATA_KEY_HEX", None)
    if key is not None:
        env["ARCHER_GATEWAY_DATA_KEY_HEX"] = key
    else:
        env.pop("ARCHER_GATEWAY_DATA_KEY_HEX", None)
    if new_key is not None:
        env["ARCHER_GATEWAY_NEW_DATA_KEY_HEX"] = new_key
    params = ["dotnet", str(TOOL), command, str(source)]
    if target is not None:
        params.append(str(target))
    result = subprocess.run(params, capture_output=True, text=True,
                            timeout=18, env=env)
    if success:
        assert result.returncode == 0, (command, result.stderr[:180])
        assert "PASS:" in result.stdout
    else:
        assert result.returncode != 0, command
        assert "FAILED:" in result.stderr
    assert "Hello from synthetic backup" not in (result.stdout + result.stderr)
    return result


def main():
    assert TOOL.exists(), "Build Archer.GatewayBackup Release before running smoke."
    key = secrets.token_hex(32)
    wrong_key = secrets.token_hex(32)
    rotated_key = secrets.token_hex(32)
    with tempfile.TemporaryDirectory(prefix="archer-backup-ci-") as root:
        root = Path(root)
        source_dir = root / "active"
        backup_dir = root / "backup"
        restore_dir = root / "restored"
        rotated_dir = root / "rotated"
        for directory in (source_dir, backup_dir, restore_dir, rotated_dir):
            directory.mkdir()
        source = source_dir / "mobile-gateway-state.json"
        backup = backup_dir / "archer-state-verified.archerbackup"
        restored = restore_dir / "mobile-gateway-state.json"
        corrupt = backup_dir / "damaged.archerbackup"
        refused = restore_dir / "never-created.json"
        rotated = rotated_dir / "mobile-gateway-state.json"
        refused_rotate = rotated_dir / "never-rotated.json"

        # Create real encrypted local state with a synthetic phone message.
        process = run_gateway(source_dir, key)
        try:
            status, raw = call("/internal/devices/enroll", "POST", {
                "label": "Synthetic personal phone",
                "lifetimeDays": 1,
                "scopes": ["conversation:read", "conversation:write"]
            }, {"X-Archer-Admin-Key": ADMIN})
            assert status == 200, status
            bearer = json.loads(raw)["deviceToken"]
            status, raw = call("/v1/conversation/messages", "POST", {
                "clientMessageId": str(uuid4()),
                "content": "Hello from synthetic backup"
            }, {"Authorization": "Bearer " + bearer})
            assert status == 200
            assert json.loads(raw)["outcome"] == "saved"

            # Backing up a live gateway snapshots the atomic file safely.
            invoke("backup", source, backup, key)
            invoke("verify", backup, key=key)
            # Never attempt live restore while gateway is listening.
            invoke("restore-new", backup, restored, key, success=False)
            invoke("rotate-new", source, rotated, key, success=False,
                   new_key=rotated_key)
            assert not restored.exists()
            assert not rotated.exists()
        finally:
            stop(process)

        encrypted_source = source.read_bytes()
        assert backup.read_bytes() == encrypted_source
        assert b"Hello from synthetic backup" not in backup.read_bytes()
        invoke("backup", source, backup, key, success=False)  # no overwrite
        invoke("verify", backup, key=wrong_key, success=False)
        invoke("verify", backup, success=False)  # no key

        damaged = bytearray(backup.read_bytes())
        damaged[len(damaged) // 2] ^= 1
        corrupt.write_bytes(damaged)
        invoke("verify", corrupt, key=key, success=False)
        invoke("restore-new", corrupt, refused, key=key, success=False)
        assert not refused.exists()

        # A rotation must refuse missing, malformed, identical or wrong
        # keys and never write any state on failure.
        invoke("rotate-new", source, refused_rotate, key, success=False)
        invoke("rotate-new", source, refused_rotate, key, success=False,
               new_key=key)
        invoke("rotate-new", source, refused_rotate, key, success=False,
               new_key="0" * 20)
        invoke("rotate-new", source, refused_rotate, wrong_key, success=False,
               new_key=rotated_key)
        invoke("rotate-new", corrupt, refused_rotate, key, success=False,
               new_key=rotated_key)
        invoke("rotate-new", source, source, key, success=False,
               new_key=rotated_key)
        assert not refused_rotate.exists()
        assert source.read_bytes() == encrypted_source

        invoke("rotate-new", source, rotated, key, new_key=rotated_key)
        assert rotated.exists()
        assert rotated.read_bytes() != encrypted_source
        assert b"Hello from synthetic backup" not in rotated.read_bytes()
        invoke("verify", rotated, key=rotated_key)
        invoke("verify", rotated, key=key, success=False)
        invoke("rotate-new", source, rotated, key, success=False,
               new_key=rotated_key)  # no overwrite
        assert source.read_bytes() == encrypted_source

        # New encrypted copy must be readable by the SAME device token,
        # and preserve message order and session identity.
        process = run_gateway(rotated_dir, rotated_key)
        try:
            status, raw = call("/v1/conversation/messages", header={
                "Authorization": "Bearer " + bearer
            })
            assert status == 200, status
            messages = json.loads(raw)["messages"]
            assert len(messages) == 1
            assert messages[0]["content"] == "Hello from synthetic backup"
            assert messages[0]["sequence"] == 1
            assert json.loads(raw)["assistantConnected"] is False
        finally:
            stop(process)

        # Restore ONLY to a new file; a valid backup and original key
        # preserve device identity, messages and session sequence.
        invoke("restore-new", backup, restored, key)
        assert restored.read_bytes() == backup.read_bytes()
        invoke("restore-new", backup, restored, key, success=False)

        process = run_gateway(restore_dir, key)
        try:
            status, raw = call("/v1/conversation/messages", header={
                "Authorization": "Bearer " + bearer
            })
            assert status == 200, status
            messages = json.loads(raw)["messages"]
            assert len(messages) == 1
            assert messages[0]["content"] == "Hello from synthetic backup"
            assert messages[0]["sequence"] == 1
            # A manual restore did not generate a model response, call or push.
            assert json.loads(raw)["assistantConnected"] is False
        finally:
            stop(process)

        assert source.read_bytes() == encrypted_source

    print("PASS: verified backup, no-overwrite restore, offline key rotation, wrong-key/tamper refusal and recovered owner journal")


if __name__ == "__main__":
    main()
