using System;
using Microsoft.Win32;

namespace DiskPulse
{
    internal static class AutoStart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DiskPulse";

        public static string ExpectedCommand
        {
            get
            {
                return "\"" + AppPaths.ExecutablePath + "\" --silent";
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    return key.GetValue(ValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool IsCurrentCommand()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    string value = key.GetValue(ValueName) as string;
                    return value != null && string.Equals(value, ExpectedCommand, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool SetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null) return false;
                    if (enabled)
                    {
                        key.SetValue(ValueName, ExpectedCommand, RegistryValueKind.String);
                        Logger.Info("已启用开机自动记录: " + ExpectedCommand);
                    }
                    else
                    {
                        if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
                        Logger.Info("已关闭开机自动记录");
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("修改开机启动项失败", ex);
                return false;
            }
        }

        public static void Ensure(bool enabled)
        {
            try
            {
                if (enabled)
                {
                    if (!IsCurrentCommand()) SetEnabled(true);
                }
                else
                {
                    if (IsEnabled()) SetEnabled(false);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("同步开机启动项失败", ex);
            }
        }
    }
}
