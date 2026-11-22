#!/usr/bin/env python3
"""Populate only public iOS app configuration at owner-approved release time.

Never prints values or accepts model credentials, private keys or arbitrary URLs.
The unconfigured templates remain checked in; the release job writes these
temporarily on its disposable macOS runner before compilation.
"""
from __future__ import annotations
import os
from pathlib import Path
import plistlib
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "ios" / "ArcherMobile" / "App"


def validate_client(value: str) -> str:
    if not re.fullmatch(r"[a-zA-Z0-9_-]{12,128}", value):
        raise ValueError("Auth0 Native client ID missing or invalid.")
    if value.startswith("REPLACE_"):
        raise ValueError("Auth0 Native client ID is still a placeholder.")
    return value


def validate_host(value: str) -> str:
    if not re.fullmatch(r"[a-z0-9][a-z0-9.-]{1,165}\\.ts\\.net", value):
        raise ValueError("Private HTTPS host must be an exact lower-case tailnet name.")
    if ".." in value or value.startswith("-") or value.startswith("127."):
        raise ValueError("Invalid private HTTPS host.")
    return value


def configure(client: str, host: str, app: Path) -> None:
    client = validate_client(client)
    host = validate_host(host)
    data = plistlib.loads((app / "Auth0.plist").read_bytes())
    if data.get("Domain") != "dev-1l3ic41kxj7zpeb8.us.auth0.com":
        raise ValueError("Unexpected Auth0 tenant. Do not ship.")
    data["ClientId"] = client
    (app / "Auth0.plist").write_bytes(plistlib.dumps(data))
    (app / "Gateway.plist").write_bytes(plistlib.dumps({"Host": host}))


def self_test() -> None:
    assert validate_client("owner_native_abc1234567") == "owner_native_abc1234567"
    assert validate_host("archer.tailnet-abc.ts.net") == "archer.tailnet-abc.ts.net"
    for bad in ("", "REPLACE_WITH_AUTH0_NATIVE_APP_CLIENT_ID", "bad", "a" * 200):
        try:
            validate_client(bad)
            raise AssertionError("Bad client accepted.")
        except ValueError:
            pass
    for bad in ("", "localhost", "archer.example.com", "a.ts.net.evil.com", "https://archer.ts.net", "bad..tail.ts.net"):
        try:
            validate_host(bad)
            raise AssertionError("Unsafe host accepted.")
        except ValueError:
            pass
    print("PASS: TestFlight release configuration validates host and public client without secrets.")


if __name__ == "__main__":
    if sys.argv[1:] == ["--self-test"]:
        self_test()
    elif len(sys.argv) == 1:
        configure(os.environ.get("IOS_NATIVE_CLIENT_ID", ""), os.environ.get("IOS_PRIVATE_HOST", ""), APP)
    else:
        raise SystemExit("Usage: python3 tools/ios_release_config.py [--self-test]")
