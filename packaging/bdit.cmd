@echo off
setlocal
if not exist "%~dp0app\BDIT.TenantToolkit.App.exe" (
  echo Refused: extract the complete toolkit ZIP; the application host is missing. 1>&2
  exit /b 2
)
"%~dp0app\BDIT.TenantToolkit.App.exe" --cli %*
exit /b %errorlevel%
