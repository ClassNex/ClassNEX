using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>只读取所选 ClassIsland 2.x 安装的本地数据；不执行原程序、插件或课表行动。</summary>
public static partial class ClassIslandImporter
{
    private const int JsonLimit = 16 * 1024 * 1024;
    private static readonly StringComparison Paths = StringComparison.OrdinalIgnoreCase;

    /// <summary>检查程序身份，解析当前布局和本地档案。预览不写任何文件。</summary>
    public static ClassIslandImportPlan Inspect(string selectedExe, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = Locate(selectedExe);
        var plan = new ClassIslandImportPlan(source);
        long bytesRead = 0;
        JsonElement Read(string file)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckPath(file);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > JsonLimit || (bytesRead += stream.Length) > 96L * 1024 * 1024)
                throw new InvalidDataException("配置为空或超出导入大小限制：" + Path.GetFileName(file));
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            try
            {
                var bom = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
                using var document = JsonDocument.Parse(bytes.AsMemory(bom), new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
                plan.SourceHashes.Add(file, SHA256.HashData(bytes));
                return document.RootElement.Clone();
            }
            catch (JsonException ex) { throw new InvalidDataException("无法解析配置：" + Path.GetFileName(file), ex); }
        }
        var settings = Read(Path.Combine(source.DataRoot, "Settings.json"));
        ReadAppearance(settings, plan);
        var layoutName = SafeName(settings.Str("CurrentComponentConfig") ?? "Default");
        var layout = Path.Combine(source.DataRoot, "Config", "ComponentLayouts", layoutName + ".json");
        CheckPath(layout);
        if (File.Exists(layout)) ReadWidgets(Read(layout), plan);
        else if (layoutName.Equals("Default", Paths))
        {
            plan.MissingFiles.Add(layout);
            plan.Widgets.Add(new() { Type = "date" });
            plan.Widgets.Add(new() { Type = "schedule", Order = 1 });
        }
        else throw new InvalidDataException("找不到选中的组件布局：" + layoutName);
        var selected = SafeName(settings.Flag("IsManagementEnabled") ? "_management-profile.json" : settings.Str("SelectedProfile") ?? "Default.json");
        if (settings.Flag("IsManagementEnabled")) plan.Warn("只迁移本地缓存的集控课表，不复制 ClassIsland 的管理凭据或连接。");
        if (!selected.EndsWith(".json", Paths)) throw new InvalidDataException("当前档案必须是本地 JSON 文件。");
        var profiles = ProfileFiles(source.DataRoot);
        if (!profiles.Any(f => Path.GetFileName(f).Equals(selected, Paths)) && (profiles.Length != 0 || !selected.Equals("Default.json", Paths)))
            throw new InvalidDataException("找不到选中的档案：" + selected);
        foreach (var file in profiles)
            ReadProfile(Read(file), Path.GetFileNameWithoutExtension(file), Path.GetFileName(file).Equals(selected, Paths), plan);
        plan.ProfileFiles = profiles;
        if (profiles.Length == 0) plan.Warn("此安装没有本地档案，将保留 ClassNEX 原有课表。");
        return plan;
    }

    private static ClassIslandSource Locate(string selectedExe)
    {
        if (!Path.IsPathFullyQualified(selectedExe)) throw new InvalidDataException("请选择本机 ClassIsland 程序。");
        var exe = Path.GetFullPath(selectedExe);
        CheckPath(exe);
        var name = Path.GetFileName(exe);
        if (!name.Equals("ClassIsland.exe", Paths) && !name.Equals("ClassIsland.Desktop.exe", Paths)) throw new InvalidDataException("请选择 ClassIsland.exe 或 ClassIsland.Desktop.exe。");
        if (!File.Exists(exe)) throw new FileNotFoundException("所选程序不存在。");
        RequireVersion2(exe);
        var directory = Path.GetDirectoryName(exe)!;
        var root = name.Equals("ClassIsland.Desktop.exe", Paths) && Path.GetFileName(directory).StartsWith("app-2.", Paths) ? Directory.GetParent(directory)!.FullName : directory;
        var marker = Path.Combine(directory, "PackageType");
        CheckPath(marker);
        if (!directory.Equals(root, Paths) && !File.Exists(marker)) { marker = Path.Combine(root, "PackageType"); CheckPath(marker); }
        if (File.Exists(marker) && new FileInfo(marker).Length > 64) throw new InvalidDataException("安装类型标记无效。");
        var packageType = File.Exists(marker) ? File.ReadAllText(marker).Trim().ToLowerInvariant() : "";
        var data = packageType switch
        {
            "folder" => Path.Combine(root, "data"),
            "folderclassic" => name.Equals("ClassIsland.Desktop.exe", Paths) ? directory : ClassicLauncherData(directory),
            "installer" or "msix" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClassIsland", "Data"),
            "" when File.Exists(Path.Combine(root, "data", "Settings.json")) => Path.Combine(root, "data"),
            "" when File.Exists(Path.Combine(directory, "Settings.json")) => directory,
            "" when File.Exists(Path.Combine(root, "AppxManifest.xml")) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClassIsland", "Data"),
            _ => throw new InvalidDataException("找不到此安装的 ClassIsland 2.x 数据目录。")
        };
        CheckPath(data);
        if (!File.Exists(Path.Combine(data, "Settings.json"))) throw new InvalidDataException("找不到 Settings.json。请先确认原程序已经运行并保存过设置。");
        return new(root, data, exe) { PackageType = packageType };
    }

    private static void RequireVersion2(string exe)
    {
        var identity = FileVersionInfo.GetVersionInfo(exe);
        if (!string.Equals(identity.ProductName, "ClassIsland", Paths) || identity.ProductMajorPart != 2)
            throw new InvalidDataException("所选文件不是 ClassIsland 2.x 程序。");
    }

    private static string ClassicLauncherData(string directory)
    {
        string? active = null;
        foreach (var payload in Directory.EnumerateDirectories(directory, "app-2.*", SearchOption.TopDirectoryOnly))
        {
            var parts = Path.GetFileName(payload).Split('-');
            if (parts.Length is < 2 or > 3 || !Version.TryParse(parts[1], out var version) || version.Major != 2 ||
                parts.Length == 3 && !uint.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _)) continue;
            var desktop = Path.Combine(payload, "ClassIsland.Desktop.exe");
            CheckPath(desktop);
            if (!File.Exists(desktop) || File.Exists(Path.Combine(payload, ".partial")) || File.Exists(Path.Combine(payload, ".destroy"))) continue;
            RequireVersion2(desktop);
            if (active is not null) throw new InvalidDataException("经典版中有多个版本目录，请直接选择原先使用的 ClassIsland.Desktop.exe。");
            active = payload;
        }
        return active ?? directory;
    }

    private static string[] ProfileFiles(string data)
    {
        var root = Path.Combine(data, "Profiles");
        CheckPath(root);
        var files = Directory.Exists(root) ? Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly).Take(129).Order(StringComparer.OrdinalIgnoreCase).ToArray() : [];
        if (files.Length > 128) throw new InvalidDataException("一次最多可迁移 128 个本地档案。");
        return files;
    }

    internal static void CheckPath(string path)
    {
        for (var item = Path.GetFullPath(path); item != null; item = Path.GetDirectoryName(item))
        {
            try { if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("不从链接、联接或挂载目录导入或写入。"); }
            catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
        }
    }

    private static string SafeName(string text) => text.Length is > 0 and <= 190 && text.Trim() == text && !text.EndsWith('.') && text is not "." and not ".." &&
        !text.Any(c => c < ' ' || "<>:\"/\\|?*".Contains(c)) && Path.GetFileName(text) == text ? text : throw new InvalidDataException("配置包含无效的本地文件名。");

    /// <summary>源配置、所选安装和档案列表是否仍与预览一致。发生变化时应重新选择程序。</summary>
    public static void VerifySource(ClassIslandImportPlan plan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Locate(plan.Source.SelectedExe) != plan.Source)
            throw new InvalidDataException("原安装的配置已变化，请重新选择程序。");
        foreach (var (file, expected) in plan.SourceHashes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckPath(file);
            using var input = File.OpenRead(file);
            if (input.Length > JsonLimit || !CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(input))) throw new InvalidDataException("原配置已变化，请重新选择程序：" + Path.GetFileName(file));
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!plan.ProfileFiles.SequenceEqual(ProfileFiles(plan.Source.DataRoot), StringComparer.OrdinalIgnoreCase) || plan.MissingFiles.Any(File.Exists))
            throw new InvalidDataException("原安装的配置已变化，请重新选择程序。");
    }

    /// <summary>将新档案写入专属目录，校验回读后原子提交设置。失败或取消时保留旧设置与源安装。</summary>
    public static ClassIslandImportResult Apply(ClassIslandImportPlan plan, AppSettings current, string targetDataDirectory, CancellationToken cancellationToken = default)
    {
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDataDirectory));
        static bool Within(string a, string b) => a.Equals(b, Paths) || a.StartsWith(Path.TrimEndingDirectorySeparator(b) + Path.DirectorySeparatorChar, Paths);
        if (Within(target, plan.Source.InstallRoot) || Within(plan.Source.InstallRoot, target) || Within(target, plan.Source.DataRoot) || Within(plan.Source.DataRoot, target))
            throw new InvalidDataException("ClassNEX 数据目录与原安装重叠。");
        var path = Path.Combine(target, "Settings.json");
        CheckPath(path);
        VerifySource(plan, cancellationToken);
        var store = path.Equals(SettingsService.SettingsPath, Paths) ? SettingsService.Store : new SettingsStore(path);
        var (settings, priorSettings) = store.ReadImportBase(current);
        CopyAppearance(plan.Appearance, settings);
        settings.Widgets = plan.Widgets;
        settings.WidgetLayoutVersion = 1;
        string? active = null;
        var root = Path.Combine(target, "ClassIsland-" + Guid.NewGuid().ToString("N"));
        var created = new List<string>();
        bool createdDirectory = false;
        try
        {
            foreach (var (name, profile, layout, selected) in plan.Profiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!createdDirectory) { Directory.CreateDirectory(root); createdDirectory = true; }
                var file = Path.Combine(root, SafeName(name) + ".yaml");
                var yaml = CsesCodec.SerializeWorkingProfile(profile);
                using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    created.Add(file);
                    using var writer = new StreamWriter(stream); writer.Write(yaml);
                }
                if (File.ReadAllText(file) != yaml || CsesCodec.SerializeWorkingProfile(CsesCodec.Load(file)) != yaml) throw new IOException("无法验证迁入的课表。");
                if (!selected) continue;
                active = settings.TimetablePath = file;
                settings.TimeLayout = layout ?? TimeLayout.FromProfile(profile);
            }
            VerifySource(plan, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new(plan.Source, store.SaveImport(settings, priorSettings), active);
        }
        catch
        {
            foreach (var file in created) File.Delete(file);
            if (createdDirectory && !Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
            throw;
        }
    }
}
