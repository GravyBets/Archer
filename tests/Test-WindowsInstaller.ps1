# Verify installer on fake, nonexecutable packages only. Does not start Archer.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$installScript=Join-Path $project 'tools/Install-Archer.ps1'
$root=Join-Path ([IO.Path]::GetTempPath()) ('archer-installer-test-'+[guid]::NewGuid().ToString('N'))
$env:ARCHER_INSTALL_TEST_MODE='1'
$installRoot=Join-Path $root 'installed'
$source=Join-Path $root 'source'
New-Item -ItemType Directory -Path (Join-Path $source 'wwwroot'),$installRoot -Force | Out-Null
$zip=Join-Path $root 'archer-app.zip'
$hash=Join-Path $root 'archer-app.sha256'
try {
  foreach($file in @('Archer.MissionControl.exe','Run-Archer.cmd','Start-Archer.ps1')) {
    Set-Content -LiteralPath (Join-Path $source $file) -Value ('sample-'+$file)
  }
  Set-Content -LiteralPath (Join-Path $source 'wwwroot/index.html') -Value 'ARCHER-A'
  Compress-Archive -Path (Join-Path $source '*') -DestinationPath $zip -Force
  (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash |
    Set-Content -LiteralPath $hash -NoNewline

  & $installScript -PackagePath $zip -HashPath $hash -InstallRoot $installRoot
  if (-not (Test-Path (Join-Path $installRoot 'current/Archer.MissionControl.exe'))) {
    throw 'First install did not create current executable.'
  }
  $userData=Join-Path $installRoot 'Data/owner-conversation.json'
  Set-Content -LiteralPath $userData -Value 'keep-mission-history'
  Set-Content -LiteralPath (Join-Path $source 'wwwroot/index.html') -Value 'ARCHER-B'
  Compress-Archive -Path (Join-Path $source '*') -DestinationPath $zip -Force
  (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash |
    Set-Content -LiteralPath $hash -NoNewline
  & $installScript -PackagePath $zip -HashPath $hash -InstallRoot $installRoot
  $content=Get-Content -Raw (Join-Path $installRoot 'current/wwwroot/index.html')
  if (-not $content.Contains('ARCHER-B')) { throw 'New app not installed.' }
  if (-not (Test-Path (Join-Path $installRoot 'previous/wwwroot/index.html'))) {
    throw 'Rollback copy missing.'
  }
  if ((Get-Content -Raw $userData).Trim() -ne 'keep-mission-history') {
    throw 'Persistent user data changed during update.'
  }

  $wrong=Join-Path $root 'wrong.sha256'
  ('0'*64)|Set-Content -LiteralPath $wrong -NoNewline
  $rejected=$false
  try { & $installScript -PackagePath $zip -HashPath $wrong -InstallRoot $installRoot }
  catch { $rejected=$true }
  if (-not $rejected) { throw 'Corrupt hash was accepted.' }
  $content=Get-Content -Raw (Join-Path $installRoot 'current/wwwroot/index.html')
  if (-not $content.Contains('ARCHER-B')) { throw 'Bad hash changed installed version.' }

  & $installScript -Rollback -InstallRoot $installRoot
  $content=Get-Content -Raw (Join-Path $installRoot 'current/wwwroot/index.html')
  if (-not $content.Contains('ARCHER-A')) { throw 'Rollback restored the wrong version.' }
  if ((Get-Content -Raw $userData).Trim() -ne 'keep-mission-history') {
    throw 'Rollback touched user data.'
  }
  Write-Host 'PASS: first install, update, SHA256 rejection, durable state and rollback.'
} finally {
  if (Test-Path -LiteralPath $root) {
    Remove-Item -LiteralPath $root -Recurse -Force
  }
}
