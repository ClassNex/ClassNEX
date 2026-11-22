using System.Text.Json;
using ClassNex.Core.Abstractions;

namespace ClassNex.Services;

internal sealed record PluginEntry(string Directory, PluginManifest Manifest, string AssemblyPath, string? Error);

internal static class PluginCatalog
{
    internal const int ApiVersion = 1;
    internal static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    internal static List<PluginEntry> Read(string root, CancellationToken token)
    {
        var entries = new List<PluginEntry>();
        if (!Directory.Exists(root)) return entries;
        foreach (var directory in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            var path = Path.Combine(directory, "plugin.json");
            if (!File.Exists(path)) continue;
            var label = Path.GetFileName(directory);
            var manifest = new PluginManifest(label, label, "", "", "");
            try
            {
                manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("空清单。");
                if (!IsId(manifest.Id)) throw new InvalidDataException("插件 ID 无效。");
                if (string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.EntryType)) throw new InvalidDataException("缺少名称或入口类型。");
                if (string.IsNullOrWhiteSpace(manifest.Assembly) || Path.IsPathRooted(manifest.Assembly)) throw new InvalidDataException("请指定插件目录中的 DLL。");
                var full = Path.GetFullPath(manifest.Assembly, directory);
                if (!full.StartsWith(directory + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                    || !string.Equals(Path.GetExtension(full), ".dll", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("程序集不在插件目录中。");
                if (manifest.Dependencies is null || manifest.Dependencies.Any(d => !IsId(d.Key) || !Version.TryParse(d.Value, out _))) throw new InvalidDataException("依赖项必须使用插件 ID 和最低数字版本。");
                if (manifest.Permissions?.Any(p => !PluginPermissions.IsKnown(p)) == true) throw new InvalidDataException("清单包含未知的宿主权限。");
                entries.Add(new(directory, manifest, full, null));
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                if (!IsId(manifest.Id)) manifest = manifest with { Id = label };
                entries.Add(new(directory, manifest, "", "清单错误：" + ex.Message));
            }
        }
        return entries;
    }

    private static bool IsId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 120 && char.IsAsciiLetterOrDigit(id[0]) &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') && Path.GetFileName(id) == id && !id.EndsWith('.');
}
