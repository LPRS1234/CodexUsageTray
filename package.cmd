@echo off
setlocal
set "PROJECT_DIR=%~dp0"
set "SETUP_DIR=%PROJECT_DIR%bin\setup"
set "PAYLOAD_DIR=%SETUP_DIR%\payload"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] .NET Framework C# compiler was not found.
  exit /b 1
)
call "%PROJECT_DIR%build.cmd" "%PAYLOAD_DIR%"
if errorlevel 1 exit /b 1
findstr /l /b /c:"[assembly: System.Reflection.AssemblyVersion(" /c:"[assembly: System.Reflection.AssemblyFileVersion(" "%PROJECT_DIR%src\Program.cs" > "%SETUP_DIR%\SetupAssemblyInfo.cs"
if errorlevel 1 exit /b 1
echo [assembly: System.Reflection.AssemblyTitle("Codex Usage Tray Setup")]>> "%SETUP_DIR%\SetupAssemblyInfo.cs"
echo [assembly: System.Reflection.AssemblyProduct("Codex Usage Tray")]>> "%SETUP_DIR%\SetupAssemblyInfo.cs"
echo [assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]>> "%SETUP_DIR%\SetupAssemblyInfo.cs"
"%CSC%" /nologo /utf8output /codepage:65001 /target:winexe /optimize+ /platform:anycpu /main:CodexUsageTray.Setup.SetupProgram ^
  /out:"%SETUP_DIR%\CodexUsageTray-Setup.exe" /win32manifest:"%PROJECT_DIR%installer\setup.manifest" ^
  /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ^
  /resource:"%PAYLOAD_DIR%\CodexUsageTray.exe",Payload.CodexUsageTray.exe ^
  /resource:"%PAYLOAD_DIR%\assets\codex-terminal.png",Payload.assets.codex-terminal.png ^
  /resource:"%PAYLOAD_DIR%\assets\dashboard.html",Payload.assets.dashboard.html ^
  /resource:"%PAYLOAD_DIR%\assets\dashboard.css",Payload.assets.dashboard.css ^
  /resource:"%PAYLOAD_DIR%\assets\dashboard.js",Payload.assets.dashboard.js ^
  /recurse:"%PROJECT_DIR%installer\*.cs" "%SETUP_DIR%\SetupAssemblyInfo.cs"
if errorlevel 1 exit /b 1
echo Packaged: %SETUP_DIR%\CodexUsageTray-Setup.exe
exit /b 0
