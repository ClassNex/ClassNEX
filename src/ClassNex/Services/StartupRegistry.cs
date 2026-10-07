using Microsoft.Win32;

namespace ClassNex.Services;

/// <summary>
/// 开机自启与 Url 协议注册（写当前用户注册表，不需要管理员权限）。
/// 对照设置页「基本 → 行为」里的「开机自启」「注册 Url 协议」两项。
/// </summary>
public static class StartupRegistry
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ClassNEX";
    private const string ProtocolRoot = @"Software\Classes\classnex";

    /// <summary>开机自启：写/删 HKCU\...\Run\ClassNEX。</summary>
    public static void SetRunAtStartup(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null)
                return;

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exe))
                    key.SetValue(RunValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // 注册表不可写时忽略（不影响其它功能）
        }
    }

    /// <summary>注册/注销 classnex:// Url 协议。</summary>
    public static void SetUrlProtocol(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exe))
                    return;

                using var root = Registry.CurrentUser.CreateSubKey(ProtocolRoot);
                root?.SetValue("", "URL:ClassNEX Protocol");
                root?.SetValue("URL Protocol", "");

                using var command = Registry.CurrentUser.CreateSubKey(ProtocolRoot + @"\shell\open\command");
                command?.SetValue("", $"\"{exe}\" \"%1\"");
            }
            else
            {
                Registry.CurrentUser.DeleteSubKeyTree(ProtocolRoot, throwOnMissingSubKey: false);
            }
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>读取注册表当前状态（用于把设置项与实际状态对齐）。</summary>
    public static bool IsRunAtStartupEnabled()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string;
        }
        catch
        {
            return false;
        }
    }
}
