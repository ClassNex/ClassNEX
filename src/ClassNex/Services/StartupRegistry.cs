using System.Diagnostics;
using Microsoft.Win32;

namespace ClassNex.Services;

public static class StartupRegistry
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ClassNEX";
    private const string ProtocolRoot = @"Software\Classes\classnex";
    private const string CommandKey = ProtocolRoot + @"\shell\open\command";
    private const string ProtocolDescription = "URL:ClassNEX Protocol";
    private const string ProductExe = "ClassNex.exe";
    private const string StartupApproval = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public static void SetRunAtStartup(bool enabled) => WithCurrentInstall((registry, processPath) =>
    {
        var launcher = FindPortableLauncher(processPath);
        using var run = enabled ? registry.CreateSubKey(RunKeyPath) : registry.OpenSubKey(RunKeyPath, true);
        if (run is null) return;
        var existing = run.GetValue(RunValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (existing is not null && !OwnsCommand(existing, processPath, launcher, false)) return;
        if (enabled) run.SetValue(RunValueName, Command(launcher ?? processPath, false));
        else run.DeleteValue(RunValueName, false);
    });

    public static void SetUrlProtocol(bool enabled) => WithCurrentInstall((registry, processPath) =>
    {
        var launcher = FindPortableLauncher(processPath);
        using (var protocol = registry.OpenSubKey(ProtocolRoot))
        {
            if (protocol is not null)
            {
                using var command = registry.OpenSubKey(CommandKey);
                if (!OurProtocol(protocol) || !OwnsCommand(command?.GetValue(null, null, RegistryValueOptions.DoNotExpandEnvironmentNames), processPath, launcher, true)) return;
            }
        }
        if (!enabled) { registry.DeleteSubKeyTree(ProtocolRoot, false); return; }
        using var registered = registry.CreateSubKey(ProtocolRoot);
        registered.SetValue("", ProtocolDescription);
        registered.SetValue("URL Protocol", "");
        using var opener = registry.CreateSubKey(CommandKey);
        opener.SetValue("", Command(launcher ?? processPath, true));
    });

    internal static void RepairPortableRegistrations(bool runAtStartup, bool registerUrlProtocol)
    {
        if (runAtStartup || registerUrlProtocol)
            WithCurrentInstall((registry, processPath) => RepairExisting(registry, processPath, runAtStartup, registerUrlProtocol));
    }

    internal static void RepairExisting(RegistryKey registry, string processPath, bool runAtStartup, bool registerUrlProtocol)
    {
        if (FindPortableLauncher(processPath) is not { } launcher) return;
        if (runAtStartup)
        {
            using var approval = registry.OpenSubKey(StartupApproval);
            var state = approval?.GetValue(RunValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (state is null || state is byte[] { Length: > 0 } bytes && bytes[0] is 2 or 6) Repair(RunKeyPath, RunValueName, false);
        }
        if (registerUrlProtocol)
        {
            using var protocol = registry.OpenSubKey(ProtocolRoot);
            if (protocol is not null && OurProtocol(protocol)) Repair(CommandKey, "", true);
        }

        void Repair(string keyPath, string name, bool protocol)
        {
            using var key = registry.OpenSubKey(keyPath, true);
            var existing = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (key is null || existing is not string text || key.GetValueKind(name) != RegistryValueKind.String ||
                text.Equals(Command(launcher, protocol), StringComparison.OrdinalIgnoreCase) || !OwnsCommand(text, processPath, launcher, protocol)) return;
            key.SetValue(name, Command(launcher, protocol), RegistryValueKind.String);
        }
    }

    private static bool OurProtocol(RegistryKey key) => key.GetValue("") as string == ProtocolDescription && key.GetValue("URL Protocol") as string == "";

    private static string Command(string exe, bool protocol) => protocol ? $"\"{exe}\" \"%1\"" : $"\"{exe}\"";

    private static bool OwnsCommand(object? value, string processPath, string? launcher, bool protocol)
    {
        if (value is not string text) return false;
        if (text.Equals(Command(processPath, protocol), StringComparison.OrdinalIgnoreCase)) return true;
        if (launcher is null) return false;
        if (text.Equals(Command(launcher, protocol), StringComparison.OrdinalIgnoreCase)) return true;
        var suffix = protocol ? "\" \"%1\"" : "\"";
        if (text.Length <= suffix.Length + 1 || text[0] != '"' || !text.EndsWith(suffix, StringComparison.Ordinal)) return false;
        var path = text[1..^suffix.Length];
        var install = Path.GetDirectoryName(launcher)!;
        var prefix = install.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var parts = path[prefix.Length..].Split(Path.DirectorySeparatorChar);
        if (parts is not [var payload, var exe] || !PayloadName(payload) || !exe.Equals(ProductExe, StringComparison.OrdinalIgnoreCase)) return false;
        var oldDirectory = Path.Combine(install, payload);
        return (!Directory.Exists(oldDirectory) || !IsLink(oldDirectory)) && (!File.Exists(path) || IsProductExe(path));
    }

    private static bool PayloadName(string name)
    {
        var last = name.LastIndexOf('-');
        return last > 4 && name.StartsWith("app-", StringComparison.OrdinalIgnoreCase) && char.IsAsciiLetterOrDigit(name[4]) &&
            last < name.Length - 1 && name[4..last].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') && name[(last + 1)..].All(char.IsAsciiDigit);
    }

    private static string? FindPortableLauncher(string exe)
    {
        if (!LocalProductPath(exe) || Path.GetDirectoryName(exe) is not { } payload || !PayloadName(Path.GetFileName(payload)) || Path.GetDirectoryName(payload) is not { } install) return null;
        var launcher = Path.Combine(install, ProductExe);
        var marker = Path.Combine(install, "PackageType");
        return !IsLink(payload) && !IsLink(install) && File.Exists(marker) && !IsLink(marker) && new FileInfo(marker).Length == 6 &&
            File.ReadAllBytes(marker).AsSpan().SequenceEqual("folder"u8) && File.Exists(Path.Combine(install, "files.json")) &&
            File.Exists(launcher) && IsProductExe(launcher) ? launcher : null;
    }

    private static bool IsProductExe(string path) => !IsLink(path) && string.Equals(FileVersionInfo.GetVersionInfo(path).ProductName, "ClassNex", StringComparison.OrdinalIgnoreCase);
    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static bool LocalProductPath(string path) => path.Length > 2 && path[1] == ':' && Path.IsPathFullyQualified(path) &&
        Path.GetFileName(path).Equals(ProductExe, StringComparison.OrdinalIgnoreCase) && new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Network;

    private static void WithCurrentInstall(Action<RegistryKey, string> action)
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { } path) return;
        try
        {
            var root = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.FullName;
            if (root is null || !LocalProductPath(path) || Environment.GetCommandLineArgs().Skip(1).Any(a => a.StartsWith("--review-", StringComparison.Ordinal) || a.StartsWith("--verify-", StringComparison.Ordinal)) ||
                File.Exists(Path.Combine(root, ".classnex-review")) || File.Exists(Path.Combine(root, ".classnex-oobe-review"))) return;
            action(Registry.CurrentUser, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or System.ComponentModel.Win32Exception)
        { Trace.WriteLine(ex.Message); }
    }

    public static bool IsRunAtStartupEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }
}
