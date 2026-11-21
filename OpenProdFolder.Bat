@echo off
setlocal

set "prodFolder=%LOCALAPPDATA%\Programs\OD.MyLocalAgent"
if not exist "%prodFolder%\" (
    echo Production folder not found: "%prodFolder%" 1>&2
    pause
    exit /b 1
)

start "" explorer.exe "%prodFolder%"
