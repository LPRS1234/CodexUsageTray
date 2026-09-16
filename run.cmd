@echo off
setlocal

set "APP=%~dp0bin\CodexUsageTray.exe"
if not exist "%APP%" (
  call "%~dp0build.cmd"
  if errorlevel 1 exit /b 1
)

start "" "%APP%"
