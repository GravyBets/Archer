#!/usr/bin/env python3
"""Verify remote HTTPS ingress is explicitly blocked pending a security release."""
from pathlib import Path
root = Path(__file__).resolve().parents[1]
caddy = (root / "deploy/remote-gateway/Caddyfile.review-only").read_text()
server = (root / "Archer.RemoteGateway/Program.cs").read_text()
boundary = (root / "Archer.RemoteGateway/SecurityBoundary.cs").read_text()
guide = (root / "deploy/remote-gateway/README.md").read_text()
checks = {
    "reserved placeholder domain": "https://gateway.example.invalid" in caddy,
    "no upstream reverse proxy": "reverse_proxy " not in "\n".join(
        line for line in caddy.splitlines() if not line.lstrip().startswith("#")),
    "TLS security header": 'Strict-Transport-Security' in caddy,
    "API explicitly 503": 'respond @gateway_api "Archer remote gateway not enabled." 503' in caddy,
    "default deny 503": 'respond "Archer remote gateway not enabled." 503' in caddy,
    "local loopback only": "server.Listen(IPAddress.Loopback, SecurityBoundary.Port)" in server,
    "server readiness blocked": "status = \"blocked\"" in server and
        "StatusCodes.Status503ServiceUnavailable" in server,
    "remote public flag refused": "ARCHER_REMOTE_PUBLIC_ENABLED" in boundary,
    "forged proxy headers refused": all(x in boundary for x in (
        "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto",
        "Forwarded", "X-Real-IP")),
    "release gates documented": all(x in guide for x in (
        "issuer/audience/JWKS", "device-bound proof", "backups",
        "explicit owner go-live authorization")),
}
for label, result in checks.items():
    assert result, f"Unsafe remote deployment preflight: {label}"
print(f"PASS: {len(checks)} fail-closed remote HTTPS ingress checks; service NOT exposed")
