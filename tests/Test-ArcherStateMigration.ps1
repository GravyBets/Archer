# Test offline Archer state discovery with synthetic data only. No owner state or credentials.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$check=Join-Path $project 'tools/Check-ArcherState.ps1'
$root=Join-Path ([IO.Path]::GetTempPath()) ('archer-state-test-'+[guid]::NewGuid().ToString('N'))
$repository=Join-Path $root 'checkout'
$installed=Join-Path $root 'owner-app'
$dev=Join-Path $repository 'Archer.MissionControl/Data'
$installedData=Join-Path $installed 'Data'
New-Item -ItemType Directory -Path $dev,$installedData -Force | Out-Null
$source=Join-Path $dev 'archer-state.json'
$secondary=Join-Path $installedData 'archer-state.json'
$marker='SENSITIVE_OWNER_CONTENT_MUST_NOT_LEAK_TO_OUTPUT'
$sample='{ "missions":[{"title":"'+$marker+'"}], "conversation":[{"content":"'+$marker+'"}], "projects":[], "developmentBatches":[] }'
function Inspect([string]$explicit='') {
  if ($explicit) {
    $output=& pwsh -NoProfile -File $check -RepositoryRoot $repository -InstalledRoot $installed -StateFile $explicit 2>&1 | Out-String
  } else {
    $output=& pwsh -NoProfile -File $check -RepositoryRoot $repository -InstalledRoot $installed 2>&1 | Out-String
  }
  return [pscustomobject]@{Text=[string]$output; Code=$LASTEXITCODE}
}
try {
  Set-Content -LiteralPath $source -Value $sample -Encoding UTF8
  $before=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
  $ok=Inspect
  if ($ok.Code -ne 0) { throw ('A valid state was not recognized: '+$ok.Text) }
  if (-not $ok.Text.Contains('Visual Studio Archer') -or -not $ok.Text.Contains('missions: 1') -or
      -not $ok.Text.Contains('No data was copied or uploaded')) {
    throw 'Owner-friendly summary missing.'
  }
  if ($ok.Text.Contains($marker)) { throw 'State contents leaked to terminal!' }
  if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $before) {
    throw 'Read-only state check modified the source.'
  }

  Copy-Item -LiteralPath $source -Destination $secondary
  $same=Inspect
  if ($same.Code -ne 0 -or -not $same.Text.Contains('Multiple matching copies found')) {
    throw 'Identical copies were reported as conflicting.'
  }
  Set-Content -LiteralPath $secondary -Value ('{ "missions":[], "conversation":[], "projects":[] }') -Encoding UTF8
  $ambiguous=Inspect
  if ($ambiguous.Code -ne 2 -or -not $ambiguous.Text.Contains('Multiple DIFFERENT')) {
    throw 'Divergent sources were not explicitly blocked.'
  }

  $named=Inspect -explicit $source
  if ($named.Code -ne 0 -or $named.Text.Contains('Installed Archer')) {
    throw 'Explicit source should suppress automatic candidate inference.'
  }
  Remove-Item -LiteralPath $source,$secondary -Force
  $empty=Inspect
  if ($empty.Code -ne 1 -or -not $empty.Text.Contains('No valid state file found')) {
    throw 'Missing owner state was not reported.'
  }
  Set-Content -LiteralPath $source -Value ('{ "conversation": ["'+$marker+'"], ') -Encoding UTF8
  $broken=Inspect
  if ($broken.Code -ne 1 -or $broken.Text.Contains($marker)) {
    throw 'Malformed JSON must fail closed without printing contents.'
  }
  Write-Host 'PASS: read-only owner state discovery, privacy, two-copy ambiguity, missing and corrupt state.'
} finally {
  Remove-Item -LiteralPath $root -Force -Recurse -ErrorAction SilentlyContinue
}
