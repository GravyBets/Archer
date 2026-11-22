#!/usr/bin/env python3
"""Offline security gates for the optional, main-only private Linux package."""
from pathlib import Path
import re
import subprocess

root=Path(__file__).resolve().parents[1]
workflow=(root/".github/workflows/archer-linux-staging.yml").read_text()
preflight=(root/"deploy/linux/preflight.sh").read_text()
gateway=(root/"Archer.PrivateGateway/Program.cs").read_text()
scopes=(root/"Archer.PrivateGateway/GatewayScopes.cs").read_text()

for field in (
    "workflow_run:", 'workflows: ["Archer Build"]', "github.event.workflow_run.conclusion == 'success'",
    "github.event.workflow_run.event == 'push'",
    "github.event.workflow_run.head_branch == 'main'",
    "github.event.workflow_run.head_repository.full_name == 'GravyBets/Archer'",
    "github.repository == 'GravyBets/Archer'",
    "ref: ${{ github.event.workflow_run.head_sha }}", "persist-credentials: false",
    "contents: read", "SOURCE_COMMIT", "sha256sum --check",
    "if-no-files-found: error", "Archer-Linux-Staging.tar.gz", "retention-days: 14",
):
    assert field in workflow, "Staging contract missing: "+field
for forbidden in (
    "workflow_dispatch:", "contents: write", "pull-requests: write",
    "secrets.", "docker push", "systemctl enable", "tailscale funnel",
    "release create", "curl -X POST",
):
    assert forbidden not in workflow, "Staging must not run unapproved deployments: "+forbidden

for name,valid,invalid in (
    ("ARCHER_PRIVATE_HOST", ["archer.owner.ts.net"], ["public.example.com","archer.ts.net.evil.com","REPLACE_WITH_APPROVED_PRIVATE_TS_HOST"]),
    ("ARCHER_PRIVATE_AUTH0_DOMAIN", ["dev-example.us.auth0.com"], ["auth0.example.com","missing"]),
    ("ARCHER_PRIVATE_NATIVE_CLIENT_ID", ["owner_client_123456"], ["short"]),
    ("ARCHER_PRIVATE_OWNER_SUBJECT", ["auth0|owner_123"], ["someone@example.com","auth0|"]),
    ("ARCHER_COMPANION_DEVICE_SHA256", ["A"*64], ["test-fingerprint","A"*63]),
):
    match=re.search(r"^  check_gateway_field "+name+r" '([^']+)'$",preflight,re.M)
    assert match, "Incomplete shell regex for "+name
    regex=re.compile(match.group(1))
    for value in valid: assert regex.fullmatch(value), "Valid "+name+" rejected"
    for value in invalid: assert not regex.fullmatch(value), "Invalid "+name+" accepted"

assert "read_env_value" in preflight
assert "CHECK MANUALLY" in preflight
assert "GatewayScopes.Allows(ctx.User" in gateway
assert 'static bool CanRead(HttpContext ctx)' in gateway
assert "if(!CanRead(ctx)||!Verify(ctx,[],proof))" in gateway
assert "required is not" in scopes
subprocess.run(["bash","-n",str(root/"deploy/linux/preflight.sh")],check=True)
print("PASS: Linux packaging remains trusted-main-only and read-only, strict gateway readiness, distinct owner scopes")
