@echo off
setlocal

set "PROJECT_DIR=%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "TEST_PREFIX=%TEMP%\CodexUsageTray"

if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] .NET Framework C# compiler was not found.
  exit /b 1
)
call :run_test DashboardServerSmokeTest CodexUsageTray.Tests.DashboardServerSmokeTest
if errorlevel 1 exit /b 1
call :run_test UsageRefreshServiceSmokeTest CodexUsageTray.Tests.UsageRefreshServiceSmokeTest
if errorlevel 1 exit /b 1

echo All smoke tests passed.
exit /b 0

:run_test
"%CSC%" /nologo /utf8output /codepage:65001 /target:exe /optimize+ /platform:anycpu ^
  /main:%~2 ^
  /out:"%TEST_PREFIX%.%~1.exe" ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Web.Extensions.dll ^
  /recurse:"%PROJECT_DIR%src\*.cs" ^
  "%PROJECT_DIR%tests\%~1.cs"
if errorlevel 1 exit /b 1
"%TEST_PREFIX%.%~1.exe"
exit /b %errorlevel%
