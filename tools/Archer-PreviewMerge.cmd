@echo off
setlocal
title Archer - Preview saved conversation merge
echo.
echo This is READ-ONLY. It will not change your installed or Visual Studio Archer.
echo.
pushd "%~dp0.."
dotnet run --project "Archer.StateMerge\Archer.StateMerge.csproj" --configuration Release -- 
set "CODE=%ERRORLEVEL%"
popd
echo.
if "%CODE%"=="0" (
  echo Review the totals above. No files have been combined or replaced.
) else (
  echo Review stopped safely. Do not overwrite your saved Archer files.
)
pause
exit /b %CODE%
