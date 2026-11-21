@echo off
setlocal
powershell.exe -NoProfile -File "%~dp0scripts\publish.ps1" %*
set "publishExitCode=%errorlevel%"
pause
exit /b %publishExitCode%
