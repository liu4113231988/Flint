using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Flint3.Helpers
{
    /// <summary>
    /// 免打包应用没有包标识，无法使用 StartupTask，改用 HKCU Run 注册开机自启
    /// </summary>
    public static class StartupUtil
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private const string ValueName = "Flint";

        /// <summary>
        /// 读取真实自启状态：注册表值存在且仍指向当前 exe 才算已启用
        /// </summary>
        public static bool IsEnabled()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return string.Equals(key?.GetValue(ValueName) as string, BuildCommand(), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
                return false;
            }
        }

        public static void Enable()
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key.SetValue(ValueName, BuildCommand(), RegistryValueKind.String);
        }

        public static void Disable()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        private static string BuildCommand()
        {
            // 自启项按当前 exe 的实际位置写入，移动或重装后读取会显示为未启用
            string exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Flint3.exe");
            return $"\"{exePath}\"";
        }
    }
}
