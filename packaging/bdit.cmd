@echo off
rem Legacy CLI alias; same BuildStandard application host and argument boundaries.
setlocal
if not exist "%~dp0app\BuildStandard.App.exe" (
  echo Refused: extract the complete toolkit ZIP; the application host is missing. 1>&2
  exit /b 2
)
"%~dp0app\BuildStandard.App.exe" --cli %*
exit /b %errorlevel%
