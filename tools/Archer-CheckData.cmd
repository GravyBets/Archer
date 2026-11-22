@echo off
setlocal
title Archer - Check saved data before moving or installing
echo.
echo Archer will check for saved conversations and assignments.
echo This check does not copy, upload or change your data.
echo.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Check-ArcherState.ps1"
set "CODE=%ERRORLEVEL%"
echo.
if "%CODE%"=="0" (
  echo Finished. Keep your Archer data private until we verify a safe backup.
) else (
  echo Some data needs review before migration. Do not reinstall or move state yet.
)
echo.
pause
exit /b %CODE%
