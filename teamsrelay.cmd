@echo off
setlocal
set "TEAMSRELAY_SCRIPT=%~dp0scripts\Invoke-TeamsRelay.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%TEAMSRELAY_SCRIPT%" %*
set "EXITCODE=%ERRORLEVEL%"
exit /b %EXITCODE%
