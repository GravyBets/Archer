#!/usr/bin/env python3
"""Static contract: the staging preflight must be read-only and fail closed."""
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parents[1]
script = root / "deploy/linux/preflight.sh"
source = script.read_text()
core = (root / "deploy/linux/archer-core.service").read_text()
gateway = (root / "deploy/linux/archer-private-gateway.service").read_text()
for text in (
    "set -euo pipefail", "case \"$local_address\" in", "127.0.0.1:5087",
    "127.0.0.1:5094", "ARCHER_PRIVATE_GATEWAY_ENABLED=true",
    "ARCHER_COMPANION_BRIDGE_KEY", "stat -c '%a'", "tailscale status",
    "restic", "--live", "--prepared", "test restore", "Funnel"
):
    assert text in source, text
for forbidden in (
    "source /etc/archer", "eval ", "ufw allow", "iptables -A", "systemctl start",
    "systemctl enable", "curl -X POST", "echo $core_key", "echo $gateway_key",
    "tailscale funnel"
):
    assert forbidden not in source, forbidden
assert "EnvironmentFile=/etc/archer/core.env" in core
assert "EnvironmentFile=/etc/archer/gateway.env" in gateway
assert "ReadWritePaths=/var/lib/archer/core" in core
assert "User=archer" in core and "User=archergateway" in gateway
subprocess.run(["bash", "-n", str(script)], check=True)
print("PASS: Linux preflight is syntactically valid, read-only and checks separated private services")
