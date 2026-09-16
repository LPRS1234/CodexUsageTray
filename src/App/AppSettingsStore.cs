using System;
using Microsoft.Win32;

namespace CodexUsageTray
{
    internal sealed class AppSettingsStore
    {
        private const string SettingsRegistryPath = @"Software\CodexUsageTray";
        private const string PositionRegistryValue = "CardCorner";
        private const string DisplayModeRegistryValue = "UsageDisplayMode";

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
