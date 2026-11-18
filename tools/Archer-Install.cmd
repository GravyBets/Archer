@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Archer.ps1" -PackagePath "%~dp0archer-app.zip" -HashPath "%~dp0archer-app.sha256"
if errorlevel 1 (
  echo Installation stopped without deploying a new version. See details above.
  pause
)
