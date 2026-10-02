@echo off
setlocal

set "PROJECT_DIR=%~dp0"
set "OUTPUT_DIR=%PROJECT_DIR%bin"
if not "%~1"=="" set "OUTPUT_DIR=%~f1"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] .NET Framework C# compiler was not found.
  exit /b 1
)

if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

"%CSC%" /nologo /utf8output /codepage:65001 /target:winexe /optimize+ /platform:anycpu ^
  /out:"%OUTPUT_DIR%\CodexUsageTray.exe" ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Web.Extensions.dll ^
  /recurse:"%PROJECT_DIR%src\*.cs"

if errorlevel 1 exit /b 1
if not exist "%OUTPUT_DIR%\assets" mkdir "%OUTPUT_DIR%\assets"
copy /y "%PROJECT_DIR%assets\codex-terminal.png" "%OUTPUT_DIR%\assets\codex-terminal.png" >nul
if errorlevel 1 exit /b 1
copy /y "%PROJECT_DIR%assets\dashboard.html" "%OUTPUT_DIR%\assets\dashboard.html" >nul
if errorlevel 1 exit /b 1
copy /y "%PROJECT_DIR%assets\dashboard.css" "%OUTPUT_DIR%\assets\dashboard.css" >nul
if errorlevel 1 exit /b 1
copy /y "%PROJECT_DIR%assets\dashboard.js" "%OUTPUT_DIR%\assets\dashboard.js" >nul
if errorlevel 1 exit /b 1
echo Built: %OUTPUT_DIR%\CodexUsageTray.exe
exit /b 0
