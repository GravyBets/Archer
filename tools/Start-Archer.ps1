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
# The listener follows one Archer server launch. A later app update
# replaces the token so a companion from the previous version quits.
$sessionFile=Join-Path $env:ARCHER_DATA_DIR 'desktop-voice-session.txt'
if (-not $running -or -not (Test-Path -LiteralPath $sessionFile)) {
  [guid]::NewGuid().ToString('N') |
    Set-Content -LiteralPath $sessionFile -Encoding Ascii -NoNewline
}
$launchToken=(Get-Content -LiteralPath $sessionFile -Raw).Trim()
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
# SAFETY / QUALITY: the legacy Windows System.Speech companion has a
# robotic fallback voice and poor recognition on some PCs. It MUST NOT
# silently replace the proven browser voice. This experimental companion
# may be run only by an informed owner who explicitly sets this opt-in flag.
# On normal launch, Archer keeps browser voice and its saved British/rate prefs.
$voiceScript=Join-Path $PSScriptRoot 'Archer-Voice.ps1'
$enableLegacyVoice=($env:ARCHER_ENABLE_LEGACY_VOICE -ceq '1')
if ($enableLegacyVoice -and (Test-Path -LiteralPath $voiceScript -PathType Leaf)) {
  if (-not $running) {
    # Give the previous version's companion time to notice the changed
    # session token and relinquish its per-owner microphone mutex.
    Start-Sleep -Milliseconds 1600
  }
  try {
    $powershell=Join-Path $PSHOME 'powershell.exe'
    $voiceArgs='-NoLogo -NoProfile -STA -ExecutionPolicy Bypass ' +
      '-File "'+$voiceScript+'" -DataDir "'+$env:ARCHER_DATA_DIR+
      '" -SessionToken "'+$launchToken+'"'
    Start-Process -FilePath $powershell -ArgumentList $voiceArgs `
      -WorkingDirectory $PSScriptRoot -WindowStyle Hidden | Out-Null
  } catch {
    Write-Warning ('Desktop voice could not be started: '+$_.Exception.Message)
  }
}
Start-Process 'http://127.0.0.1:5087/'
