using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CodexUsageTray.Setup
{
    internal static class PathSafety
    {
        internal static void Check(string path)
        {
            string current = Path.GetFullPath(path);
            while (!String.IsNullOrEmpty(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("연결 또는 재분석 지점을 포함한 경로에는 설치하거나 제거할 수 없습니다.");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                current = Path.GetDirectoryName(current);
            }
        }
    }

    internal sealed class RegistrySnapshot
    {
        private readonly string _path;
        private readonly bool _existed;
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>();
        private readonly Dictionary<string, RegistryValueKind> _kinds = new Dictionary<string, RegistryValueKind>();
        internal RegistrySnapshot(string path, string[] names)
        {
            _path = path;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path))
            {
                _existed = key != null;
                if (names == null) names = key == null ? new string[0] : key.GetValueNames();
                foreach (string name in names)
                {
                    object value = key == null ? null : key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    _values.Add(name, value);
                    if (value != null) _kinds.Add(name, key.GetValueKind(name));
                }
            }
        }
        internal void Restore()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(_path))
            {
                foreach (KeyValuePair<string, object> pair in _values)
                {
                    if (pair.Value == null) key.DeleteValue(pair.Key, false);
                    else key.SetValue(pair.Key, pair.Value, _kinds[pair.Key]);
                }
                if (_existed || key.ValueCount != 0 || key.SubKeyCount != 0) return;
            }
            Registry.CurrentUser.DeleteSubKey(_path, false);
        }
    }

    internal sealed class FileTransaction : IDisposable
    {
        private readonly string _stage;
        private readonly Dictionary<string, string> _backups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _changed = new List<string>();
        private readonly List<string> _createdDirectories = new List<string>();
        private readonly List<string> _temporaryFiles = new List<string>();
        private bool _committed, _keepBackup;
        internal FileTransaction(string installDirectory)
        {
            EnsureDirectory(installDirectory);
            _stage = Path.Combine(installDirectory, ".setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_stage);
            Directory.CreateDirectory(Path.Combine(_stage, "assets"));
            Directory.CreateDirectory(Path.Combine(_stage, "backups"));
        }
        internal string StagedPath(string relative) { return Path.Combine(_stage, relative); }
        internal void Stage(string relative, byte[] bytes) { File.WriteAllBytes(StagedPath(relative), bytes); }
        internal void StageCopy(string relative, string source) { File.Copy(source, StagedPath(relative)); }
        internal void Capture(string destination)
        {
            if (_backups.ContainsKey(destination)) return;
            PathSafety.Check(destination);
            string backup = null;
            if (File.Exists(destination))
            {
                backup = Path.Combine(_stage, "backups", _backups.Count.ToString() + ".bak");
                File.Copy(destination, backup);
            }
            _backups.Add(destination, backup);
        }
        internal void Replace(string source, string destination)
        {
            Capture(destination);
            EnsureDirectory(Path.GetDirectoryName(destination));
            ReplaceCore(source, destination);
            _changed.Add(destination);
        }
        private void ReplaceCore(string source, string destination)
        {
            PathSafety.Check(destination);
            string temporary = Path.Combine(Path.GetDirectoryName(destination), ".CodexUsageTray-" + Guid.NewGuid().ToString("N") + ".tmp");
            _temporaryFiles.Add(temporary);
            File.Copy(source, temporary);
            if (File.Exists(destination)) File.Replace(temporary, destination, null, true);
            else File.Move(temporary, destination);
        }
        internal void Delete(string destination)
        {
            if (!File.Exists(destination)) return;
            Capture(destination); PathSafety.Check(destination);
            File.Delete(destination); _changed.Add(destination);
        }
        private void EnsureDirectory(string path)
        {
            PathSafety.Check(path);
            if (Directory.Exists(path)) return;
            string parent = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(parent)) EnsureDirectory(parent);
            Directory.CreateDirectory(path); _createdDirectories.Add(path);
        }
        internal void Commit() { _committed = true; }
        internal void KeepBackup() { _keepBackup = true; }
        internal void RollBack()
        {
            List<Exception> errors = new List<Exception>();
            for (int i = _changed.Count - 1; i >= 0; i--)
            {
                string destination = _changed[i];
                try
                {
                    string backup = _backups[destination];
                    if (backup == null) { PathSafety.Check(destination); File.Delete(destination); }
                    else ReplaceCore(backup, destination);
                }
                catch (Exception ex) { errors.Add(ex); }
            }
            if (errors.Count > 0) throw new AggregateException(errors);
        }
        public void Dispose()
        {
            foreach (string temporary in _temporaryFiles) TryDeleteFile(temporary);
            if (_keepBackup) return;
            foreach (string relative in SetupEngine.PayloadFiles) TryDeleteFile(StagedPath(relative));
            TryDeleteFile(StagedPath("Uninstall.exe")); TryDeleteFile(StagedPath("app.lnk")); TryDeleteFile(StagedPath("uninstall.lnk"));
            foreach (string backup in _backups.Values) if (backup != null) TryDeleteFile(backup);
            RemoveEmptyDirectory(Path.Combine(_stage, "backups")); RemoveEmptyDirectory(Path.Combine(_stage, "assets")); RemoveEmptyDirectory(_stage);
            if (!_committed) for (int i = _createdDirectories.Count - 1; i >= 0; i--) RemoveEmptyDirectory(_createdDirectories[i]);
        }
        private static void TryDeleteFile(string path)
        {
            try { PathSafety.Check(path); if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (InvalidOperationException) { }
        }
        internal static void RemoveEmptyDirectory(string path)
        {
            try { PathSafety.Check(path); if (Directory.Exists(path)) Directory.Delete(path, false); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (InvalidOperationException) { }
        }
    }

    internal static class Shortcut
    {
        internal static void Create(string path, string target, string arguments, string workingDirectory)
        {
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            object shortcut = null;
            try
            {
                shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                shortcut.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { target });
                shortcut.GetType().InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { arguments });
                shortcut.GetType().InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
                shortcut.GetType().InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Codex Usage Tray" });
                shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally { if (shortcut != null) Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
        }
        internal static bool TargetMatches(string path, string target)
        {
            if (!File.Exists(path)) return false;
            PathSafety.Check(path);
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            object shortcut = null;
            try
            {
                shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                string actual = (string)shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null);
                return String.Equals(actual, target, StringComparison.OrdinalIgnoreCase);
            }
            finally { if (shortcut != null) Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
        }
    }
}
