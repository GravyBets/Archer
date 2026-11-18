#Requires -Version 5.1
# Starts only the owner-installed Archer executable on loopback.
# The app's persistent data is outside the current binary directory.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if (-not $env:LOCALAPPDATA) { throw 'LOCALAPPDATA is unavailable.' }
$env:ARCHER_DATA_DIR=Join-Path $env:LOCALAPPDATA 'Archer\Data'
New-Item -ItemType Directory -Path $env:ARCHER_DATA_DIR -Force | Out-Null
$exe=Join-Path $PSScriptRoot 'Archer.MissionControl.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Archer executable is missing.' }
$running=$false
try {
  $web=Invoke-WebRequest 'http://127.0.0.1:5087/api/health' -UseBasicParsing -TimeoutSec 2
  $running=($web.StatusCode -eq 200)
} catch { }
if (-not $running) {
  $proc=Start-Process -FilePath $exe -WorkingDirectory $PSScriptRoot -PassThru
  $ready=$false
  for ($i=0;$i -lt 35;$i++) {
    Start-Sleep -Milliseconds 450
    if ($proc.HasExited) {
      throw 'Archer closed during startup. Launch the executable directly for detailed error output.'
    }
    try {
      $web=Invoke-WebRequest 'http://127.0.0.1:5087/api/health' -UseBasicParsing -TimeoutSec 2
      if ($web.StatusCode -eq 200) { $ready=$true;break }
    } catch { }
  }
  if (-not $ready) { throw 'Archer did not answer on localhost:5087. Check permissions/firewall and console output.' }
}
Start-Process 'http://127.0.0.1:5087/'
