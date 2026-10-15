@echo off
rem Double-click to build the Windows release into build\windows. Arguments are passed on, e.g. build-windows.cmd -Zip
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-windows.ps1" %*
set code=%ERRORLEVEL%
if "%~1"=="" pause
exit /b %code%
