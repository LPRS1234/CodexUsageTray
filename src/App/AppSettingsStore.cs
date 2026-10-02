using System;
using Microsoft.Win32;

namespace CodexUsageTray
{
    internal sealed class AppSettingsStore
    {
        private const string SettingsRegistryPath = @"Software\CodexUsageTray";
        private const string PositionRegistryValue = "CardCorner";
        private const string DisplayModeRegistryValue = "UsageDisplayMode";
        private const string DashboardThemeRegistryValue = "DashboardTheme";

        public CardCorner LoadCardCorner()
        {
            return LoadEnum(PositionRegistryValue, CardCorner.BottomRight);
        }

        public void SaveCardCorner(CardCorner corner)
        {
            SaveValue(PositionRegistryValue, corner.ToString(), "카드 위치 설정을 저장할 수 없습니다.");
        }

        public UsageDisplayMode LoadUsageDisplayMode()
        {
            return LoadEnum(DisplayModeRegistryValue, UsageDisplayMode.Both);
        }

        public void SaveUsageDisplayMode(UsageDisplayMode displayMode)
        {
            SaveValue(DisplayModeRegistryValue, displayMode.ToString(), "표시 설정을 저장할 수 없습니다.");
        }

        public string LoadDashboardTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsRegistryPath, false))
                {
                    object value = key == null ? null : key.GetValue(DashboardThemeRegistryValue);
                    string theme = value == null ? null : value.ToString();
                    if (string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase))
                    {
                        return theme.ToLowerInvariant();
                    }
                }
            }
            catch
            {
                // Invalid or inaccessible settings fall back to the browser preference.
            }

            return null;
        }

        public void SaveDashboardTheme(string theme)
        {
            if (!string.Equals(theme, "light", StringComparison.Ordinal) &&
                !string.Equals(theme, "dark", StringComparison.Ordinal))
            {
                throw new ArgumentException("지원하지 않는 대시보드 테마입니다.", "theme");
            }

            SaveValue(DashboardThemeRegistryValue, theme, "대시보드 테마 설정을 저장할 수 없습니다.");
        }

        private static T LoadEnum<T>(string valueName, T defaultValue) where T : struct
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsRegistryPath, false))
                {
                    object value = key == null ? null : key.GetValue(valueName);
                    T parsedValue;
                    if (value != null && Enum.TryParse(value.ToString(), true, out parsedValue))
                    {
                        return parsedValue;
                    }
                }
            }
            catch
            {
                // Invalid or inaccessible settings fall back to the default.
            }

            return defaultValue;
        }

        private static void SaveValue(string valueName, string value, string errorMessage)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsRegistryPath))
            {
                if (key == null)
                {
                    throw new InvalidOperationException(errorMessage);
                }

                key.SetValue(valueName, value, RegistryValueKind.String);
            }
        }
    }
}
