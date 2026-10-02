using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using Microsoft.Win32;

namespace CodexUsageTray.Setup
{
    internal sealed class InstallContext
    {
        internal readonly string InstallDirectory, StartMenuDirectory, UninstallRegistryPath, RunRegistryPath, RunValueName, ApplicationMutexName, SetupFilePath;

        internal InstallContext(string install, string menu, string uninstallKey, string runKey, string runValue, string mutex, string setup)
        {
            InstallDirectory = Path.GetFullPath(install).TrimEnd(Path.DirectorySeparatorChar);
            StartMenuDirectory = Path.GetFullPath(menu).TrimEnd(Path.DirectorySeparatorChar);
            UninstallRegistryPath = uninstallKey;
            RunRegistryPath = runKey;
            RunValueName = runValue;
            ApplicationMutexName = mutex;
            SetupFilePath = Path.GetFullPath(setup);
        }

        internal static InstallContext ForCurrentUser()
        {
            return new InstallContext(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CodexUsageTray"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Codex Usage Tray"),
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexUsageTray",
                @"Software\Microsoft\Windows\CurrentVersion\Run", "CodexUsageTray", @"Local\CodexUsageTray",
                Assembly.GetExecutingAssembly().Location);
        }
    }

    internal sealed class SetupEngine
    {
        internal static readonly string[] PayloadFiles = { "CodexUsageTray.exe", @"assets\codex-terminal.png", @"assets\dashboard.html", @"assets\dashboard.css", @"assets\dashboard.js" };
        private static readonly string[] RegistrationValues = { "DisplayName", "DisplayVersion", "Publisher", "InstallLocation", "DisplayIcon", "UninstallString", "NoModify", "NoRepair" };
        private readonly InstallContext _context;
        private readonly Dictionary<string, byte[]> _payload;

        internal SetupEngine(InstallContext context, Dictionary<string, byte[]> payload) { _context = context; _payload = payload; }
        internal string InstallDirectory { get { return _context.InstallDirectory; } }

        internal bool IsStartupEnabled
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_context.RunRegistryPath))
                    return IsRecognizedStartup(key == null ? null : key.GetValue(_context.RunValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
            }
        }

        internal bool IsRegistered
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_context.UninstallRegistryPath))
                {
                    string value = key == null ? null : key.GetValue("InstallLocation") as string;
                    return value != null && String.Equals(value.TrimEnd(Path.DirectorySeparatorChar), _context.InstallDirectory, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        internal void Install(bool startup)
        {
            ValidatePaths();
            using (ApplicationLock running = new ApplicationLock(_context.ApplicationMutexName))
            {
                CheckRegistrationForInstall();
                if (startup)
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_context.RunRegistryPath))
                    {
                        object existing = key == null ? null : key.GetValue(_context.RunValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        if (existing != null && !IsRecognizedStartup(existing)) throw new InvalidOperationException("같은 이름의 다른 시작 프로그램이 있습니다. 기존 항목을 확인한 뒤 다시 설치해 주세요.");
                    }
                }
                using (FileTransaction files = new FileTransaction(_context.InstallDirectory))
                {
                    RegistrySnapshot run = new RegistrySnapshot(_context.RunRegistryPath, new string[] { _context.RunValueName });
                    RegistrySnapshot registration = new RegistrySnapshot(_context.UninstallRegistryPath, RegistrationValues);
                    bool registryTouched = false;
                    try
                    {
                        foreach (string relative in PayloadFiles)
                        {
                            byte[] content;
                            if (!_payload.TryGetValue(relative, out content) || content == null || content.Length == 0) throw new InvalidOperationException("설치 파일에 필요한 구성 요소가 없습니다.");
                            files.Stage(relative, content);
                        }
                        FileVersionInfo appVersion = FileVersionInfo.GetVersionInfo(files.StagedPath("CodexUsageTray.exe"));
                        Version version = new Version(appVersion.FileMajorPart, appVersion.FileMinorPart, appVersion.FileBuildPart, appVersion.FilePrivatePart);
                        if (version.Major <= 0) throw new InvalidOperationException("설치할 앱의 버전 정보가 올바르지 않습니다.");
                        files.StageCopy("Uninstall.exe", _context.SetupFilePath);
                        Shortcut.Create(files.StagedPath("app.lnk"), ApplicationPath, "", _context.InstallDirectory);
                        Shortcut.Create(files.StagedPath("uninstall.lnk"), UninstallPath, "/uninstall", _context.InstallDirectory);
                        // Capture all destinations before replacing the first one.
                        foreach (string relative in PayloadFiles) files.Capture(Path.Combine(_context.InstallDirectory, relative));
                        files.Capture(UninstallPath);
                        files.Capture(AppShortcutPath);
                        files.Capture(UninstallShortcutPath);
                        foreach (string relative in PayloadFiles) files.Replace(files.StagedPath(relative), Path.Combine(_context.InstallDirectory, relative));
                        files.Replace(files.StagedPath("Uninstall.exe"), UninstallPath);
                        files.Replace(files.StagedPath("app.lnk"), AppShortcutPath);
                        files.Replace(files.StagedPath("uninstall.lnk"), UninstallShortcutPath);
                        registryTouched = true;
                        SetStartup(startup, true);
                        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(_context.UninstallRegistryPath))
                        {
                            key.SetValue("DisplayName", "Codex Usage Tray");
                            key.SetValue("DisplayVersion", version.ToString(3));
                            key.SetValue("Publisher", "Local");
                            key.SetValue("InstallLocation", _context.InstallDirectory);
                            key.SetValue("DisplayIcon", Quote(ApplicationPath) + ",0");
                            key.SetValue("UninstallString", Quote(UninstallPath) + " /uninstall");
                            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                        }
                        files.Commit();
                    }
                    catch (Exception failure) { RollBack(files, run, registration, registryTouched, failure); throw; }
                }
            }
        }

        internal void Update(int oldApplicationPid, int timeoutMilliseconds)
        {
            using (Process application = ValidateUpdateTarget(oldApplicationPid)) Update(application, timeoutMilliseconds);
        }

        // The accepted Process retains its handle across the ready event and shutdown.
        internal void Update(Process application, int timeoutMilliseconds)
        {
            Exception installFailure = null;
            bool exited = application == null;
            try
            {
                if (application != null && !application.WaitForExit(Math.Min(60000, Math.Max(0, timeoutMilliseconds))))
                    throw new InvalidOperationException("앱이 종료되지 않아 업데이트를 취소했습니다. 앱을 종료한 뒤 다시 시도해 주세요.");
                exited = true;
                Install(IsStartupEnabled);
            }
            catch (Exception ex)
            {
                installFailure = ex;
                if (!exited && application != null)
                {
                    try { exited = application.HasExited; }
                    catch (Win32Exception) { }
                    catch (InvalidOperationException) { }
                }
            }
            if (!exited) throw new InvalidOperationException("실행 중인 앱을 유지하고 업데이트를 취소했습니다.", installFailure);
            try { LaunchApplication(); }
            catch (Exception ex)
            {
                if (installFailure != null) throw new InvalidOperationException("업데이트에 실패했으며 앱을 다시 실행하지 못했습니다. 설치된 앱을 직접 실행해 주세요.", new AggregateException(installFailure, ex));
                throw new InvalidOperationException("업데이트 후 앱을 다시 실행하지 못했습니다. 설치된 앱을 직접 실행해 주세요.", ex);
            }
            if (installFailure != null) throw new InvalidOperationException("업데이트를 완료하지 못해 설치된 앱을 다시 실행했습니다. " + installFailure.Message, installFailure);
        }

        internal Process ValidateUpdateTarget(int oldApplicationPid)
        {
            ValidatePaths();
            if (!IsRegistered || !File.Exists(ApplicationPath)) throw new InvalidOperationException("등록된 사용자 설치 경로에서만 자동 업데이트를 실행할 수 있습니다.");
            if (oldApplicationPid <= 0) throw new ArgumentException("업데이트할 앱의 프로세스가 올바르지 않습니다.");
            Process application;
            try { application = Process.GetProcessById(oldApplicationPid); } catch (ArgumentException) { return null; }
            string path;
            try
            {
                if (application.HasExited) { application.Dispose(); return null; }
                if (application.Handle == IntPtr.Zero) throw new InvalidOperationException("업데이트 대상 프로세스 핸들을 열 수 없습니다.");
                path = application.MainModule.FileName;
            }
            catch (Win32Exception) { if (DisposeExited(application)) return null; application.Dispose(); throw; }
            catch (InvalidOperationException) { if (DisposeExited(application)) return null; application.Dispose(); throw; }
            if (!String.Equals(Path.GetFullPath(path), ApplicationPath, StringComparison.OrdinalIgnoreCase))
            {
                application.Dispose();
                throw new InvalidOperationException("업데이트 대상 프로세스가 설치된 앱과 일치하지 않습니다.");
            }
            return application;
        }

        internal void Uninstall()
        {
            ValidatePaths();
            if (!IsRegistered) throw new InvalidOperationException("현재 사용자에게 등록된 설치를 찾을 수 없습니다.");
            using (ApplicationLock running = new ApplicationLock(_context.ApplicationMutexName))
            using (FileTransaction files = new FileTransaction(_context.InstallDirectory))
            {
                RegistrySnapshot run = new RegistrySnapshot(_context.RunRegistryPath, new string[] { _context.RunValueName });
                RegistrySnapshot registration = new RegistrySnapshot(_context.UninstallRegistryPath, null);
                bool registryTouched = false;
                try
                {
                    List<string> owned = new List<string>();
                    foreach (string relative in PayloadFiles) owned.Add(Path.Combine(_context.InstallDirectory, relative));
                    owned.Add(UninstallPath);
                    if (Shortcut.TargetMatches(AppShortcutPath, ApplicationPath)) owned.Add(AppShortcutPath);
                    if (Shortcut.TargetMatches(UninstallShortcutPath, UninstallPath)) owned.Add(UninstallShortcutPath);
                    foreach (string path in owned) files.Capture(path);
                    foreach (string path in owned) files.Delete(path);
                    registryTouched = true;
                    SetStartup(false, false);
                    Registry.CurrentUser.DeleteSubKey(_context.UninstallRegistryPath, false);
                    files.Commit();
                }
                catch (Exception failure) { RollBack(files, run, registration, registryTouched, failure); throw; }
            }
            FileTransaction.RemoveEmptyDirectory(Path.Combine(_context.InstallDirectory, "assets"));
            FileTransaction.RemoveEmptyDirectory(_context.InstallDirectory);
            FileTransaction.RemoveEmptyDirectory(_context.StartMenuDirectory);
        }

        internal void LaunchApplication()
        {
            PathSafety.Check(ApplicationPath);
            Process.Start(new ProcessStartInfo(ApplicationPath) { UseShellExecute = false, WorkingDirectory = _context.InstallDirectory });
        }

        private string ApplicationPath { get { return Path.Combine(_context.InstallDirectory, "CodexUsageTray.exe"); } }
        private string UninstallPath { get { return Path.Combine(_context.InstallDirectory, "Uninstall.exe"); } }
        private string AppShortcutPath { get { return Path.Combine(_context.StartMenuDirectory, "Codex Usage Tray.lnk"); } }
        private string UninstallShortcutPath { get { return Path.Combine(_context.StartMenuDirectory, "제거.lnk"); } }
        private static string Quote(string value) { return "\"" + value + "\""; }
        private bool IsOwnedStartup(object value) { return value is string && String.Equals(((string)value).Trim(), Quote(ApplicationPath), StringComparison.OrdinalIgnoreCase); }

        private bool IsRecognizedStartup(object value)
        {
            if (IsOwnedStartup(value)) return true;
            string command = value as string;
            if (String.IsNullOrEmpty(command)) return false;
            command = command.Trim();
            if (command.Length > 1 && command[0] == '"' && command[command.Length - 1] == '"') command = command.Substring(1, command.Length - 2);
            if (command.IndexOf('"') >= 0 || !Path.IsPathRooted(command) || !String.Equals(Path.GetFileName(command), "CodexUsageTray.exe", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                PathSafety.Check(command);
                return File.Exists(command) && String.Equals(FileVersionInfo.GetVersionInfo(command).ProductName, "Codex Usage Tray", StringComparison.Ordinal);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (BadImageFormatException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (ArgumentException) { return false; }
        }

        private void SetStartup(bool enabled, bool migrateLegacy)
        {
            if (enabled)
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(_context.RunRegistryPath)) key.SetValue(_context.RunValueName, Quote(ApplicationPath));
            }
            else
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_context.RunRegistryPath, true))
                {
                    object existing = key == null ? null : key.GetValue(_context.RunValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (key != null && (IsOwnedStartup(existing) || (migrateLegacy && IsRecognizedStartup(existing)))) key.DeleteValue(_context.RunValueName, false);
                }
            }
        }

        private void CheckRegistrationForInstall()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_context.UninstallRegistryPath))
                if (key != null && !IsRegistered) throw new InvalidOperationException("다른 설치 경로가 등록되어 있어 설치를 계속할 수 없습니다.");
        }

        private void ValidatePaths()
        {
            PathSafety.Check(_context.InstallDirectory);
            PathSafety.Check(_context.StartMenuDirectory);
            PathSafety.Check(_context.SetupFilePath);
            foreach (string relative in PayloadFiles) PathSafety.Check(Path.Combine(_context.InstallDirectory, relative));
            PathSafety.Check(UninstallPath);
            PathSafety.Check(AppShortcutPath);
            PathSafety.Check(UninstallShortcutPath);
        }

        private static bool DisposeExited(Process application)
        {
            try { if (application.HasExited) { application.Dispose(); return true; } }
            catch (Win32Exception) { }
            catch (InvalidOperationException) { }
            return false;
        }

        private static void RollBack(FileTransaction files, RegistrySnapshot run, RegistrySnapshot registration, bool registryTouched, Exception failure)
        {
            List<Exception> errors = new List<Exception>();
            if (registryTouched)
            {
                try { registration.Restore(); } catch (Exception ex) { errors.Add(ex); }
                try { run.Restore(); } catch (Exception ex) { errors.Add(ex); }
            }
            try { files.RollBack(); } catch (Exception ex) { errors.Add(ex); }
            if (errors.Count != 0)
            {
                files.KeepBackup(); errors.Insert(0, failure);
                throw new InvalidOperationException("작업을 완료하지 못했고 일부 파일을 복구하지 못했습니다. 설치 폴더의 임시 백업을 보존했습니다.", new AggregateException(errors));
            }
        }
    }

    internal sealed class ApplicationLock : IDisposable
    {
        private Mutex _mutex;
        internal ApplicationLock(string name)
        {
            _mutex = new Mutex(false, name);
            bool acquired;
            try { acquired = _mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired)
            {
                _mutex.Dispose(); _mutex = null;
                throw new InvalidOperationException("Codex Usage Tray가 실행 중입니다. 트레이 메뉴에서 종료한 뒤 다시 시도해 주세요.");
            }
        }
        public void Dispose() { if (_mutex == null) return; _mutex.ReleaseMutex(); _mutex.Dispose(); _mutex = null; }
    }
}
