@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Archer.ps1" -Rollback
if errorlevel 1 (
  echo Rollback could not complete. See details above.
  pause
)
