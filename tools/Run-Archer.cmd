@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Archer.ps1"
if errorlevel 1 (
  echo Archer failed to launch. Check the error above.
  pause
)
