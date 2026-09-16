using System;
using Microsoft.Win32;

namespace CodexUsageTray
{
    internal sealed class AutoStartService
    {
        private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunRegistryValue = "CodexUsageTray";

        private readonly string _executablePath;

        public AutoStartService(string executablePath)
        {
            _executablePath = executablePath;
        }

        public bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, false))
                {
                    object value = key == null ? null : key.GetValue(RunRegistryValue);
                    return value != null && value.ToString().IndexOf(
                        _executablePath, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public void Toggle()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, true))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("시작 프로그램 레지스트리를 열 수 없습니다.");
                }

                if (IsEnabled())
                {
                    key.DeleteValue(RunRegistryValue, false);
                }
                else
                {
                    key.SetValue(RunRegistryValue, "\"" + _executablePath + "\"");
                }
            }
        }
    }
}
