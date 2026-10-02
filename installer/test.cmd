@echo off
setlocal
set "PROJECT_DIR=%~dp0..\"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" exit /b 1
set "TEST_EXE=%TEMP%\CodexUsageTray.InstallerSmokeTest.exe"
"%CSC%" /nologo /utf8output /codepage:65001 /target:exe /optimize+ /platform:anycpu /main:CodexUsageTray.Tests.InstallerSmokeTest ^
  /out:"%TEST_EXE%" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ^
  /recurse:"%PROJECT_DIR%installer\*.cs" "%PROJECT_DIR%tests\InstallerSmokeTest.cs"
if errorlevel 1 exit /b 1
if /i "%~1"=="/compile-only" exit /b 0
"%TEST_EXE%" %*
exit /b %errorlevel%
