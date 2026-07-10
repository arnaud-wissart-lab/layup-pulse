@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run-LayupPulse.ps1"
exit /b %ERRORLEVEL%
