#!/usr/bin/env python3
"""Static fail-closed gate for trusted main-only Windows installer packaging."""
from pathlib import Path

workflow = (Path(__file__).resolve().parents[1] / ".github/workflows/archer-windows-package.yml").read_text()
required = [
    "workflow_dispatch:",
    "workflow_run:",
    'workflows: ["Archer Build"]',
    "github.event.workflow_run.conclusion == 'success'",
    "github.event.workflow_run.event == 'push'",
    "github.event.workflow_run.head_branch == 'main'",
    "github.event.workflow_run.head_repository.full_name == 'GravyBets/Archer'",
    "github.repository == 'GravyBets/Archer'",
    "github.event.workflow_run.head_sha",
    "github.ref == 'refs/heads/main'",
    "persist-credentials: false",
    "contents: read",
    "Archer-Windows-Installer.zip",
    "retention-days: 14",
]
for field in required:
    assert field in workflow, "Missing trusted main package gate: " + field
for forbidden in (
    "contents: write", "pull-requests: write", "secrets.OPENAI",
    "secrets.ARCHER", "docker push", "release create"
):
    assert forbidden not in workflow, "Unexpected packaging permission: " + forbidden
assert "on:\n  workflow_dispatch:\n  workflow_run:" in workflow
# Broken legacy SAPI must never be launched automatically when owner opens Archer.
launcher = (Path(__file__).resolve().parents[1] / "tools/Start-Archer.ps1").read_text()
assert "$enableLegacyVoice=($env:ARCHER_ENABLE_LEGACY_VOICE -ceq '1')" in launcher
assert "if ($enableLegacyVoice -and (Test-Path -LiteralPath $voiceScript -PathType Leaf))" in launcher
assert "if (-not $enableLegacyVoice -or -not $running" in launcher
print("PASS: native legacy microphone opt-in only; previous listener retired on normal launch")
print("PASS: installer triggered only by manual main or green trusted main build")
