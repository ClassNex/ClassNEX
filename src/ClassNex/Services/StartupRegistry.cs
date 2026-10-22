using Microsoft.Win32;

namespace ClassNex.Services;





public static class StartupRegistry
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ClassNEX";
    private const string ProtocolRoot = @"Software\Classes\classnex";

    
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
            
        }
    }

    
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
            
        }
    }

    
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
