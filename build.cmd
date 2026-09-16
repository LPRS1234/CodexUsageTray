@echo off
setlocal

set "PROJECT_DIR=%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] .NET Framework C# compiler was not found.
  exit /b 1
)

if not exist "%PROJECT_DIR%bin" mkdir "%PROJECT_DIR%bin"

"%CSC%" /nologo /utf8output /codepage:65001 /target:winexe /optimize+ /platform:anycpu ^
  /out:"%PROJECT_DIR%bin\CodexUsageTray.exe" ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Web.Extensions.dll ^
  /recurse:"%PROJECT_DIR%src\*.cs"

if errorlevel 1 exit /b 1
if not exist "%PROJECT_DIR%bin\assets" mkdir "%PROJECT_DIR%bin\assets"
copy /y "%PROJECT_DIR%assets\codex-terminal.png" "%PROJECT_DIR%bin\assets\codex-terminal.png" >nul
if errorlevel 1 exit /b 1
copy /y "%PROJECT_DIR%assets\dashboard.html" "%PROJECT_DIR%bin\assets\dashboard.html" >nul
if errorlevel 1 exit /b 1
copy /y "%PROJECT_DIR%assets\dashboard.css" "%PROJECT_DIR%bin\assets\dashboard.css" >nul
if errorlevel 1 exit /b 1
copy /y "%PROJECT_DIR%assets\dashboard.js" "%PROJECT_DIR%bin\assets\dashboard.js" >nul
if errorlevel 1 exit /b 1
echo Built: %PROJECT_DIR%bin\CodexUsageTray.exe
exit /b 0
