using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>本机可选的组件方案名称及当前桌面正在编辑的方案。</summary>
public sealed record WidgetLayoutPresetIndex(string Active, IReadOnlyList<string> Names);

/// <summary>只保存组件实例的本机方案。外界提供当前桌面组件，以及应用新布局的回调。</summary>
public sealed class WidgetLayoutPresetStore(string filePath)
{
    private sealed record Entry(string Name, List<WidgetConfig> Widgets);
    private sealed record Catalog(int Version, string Active, List<Entry> Presets, string? SwitchingTo = null);
    private const string Initial = "默认方案";
    private const int Limit = 4 * 1024 * 1024;
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
    private static readonly WidgetLayoutPresetIndex InitialIndex = new(Initial, Array.AsReadOnly(new[] { Initial }));
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly string _path = Path.GetFullPath(filePath);
    private (DateTime Changed, long Length, WidgetLayoutPresetIndex Index)? _index;

    /// <summary>打开方案选择器。旧安装没有方案文件时不创建文件，也不替换桌面。</summary>
    public WidgetLayoutPresetIndex Read(IReadOnlyList<WidgetConfig> live)
    {
        var file = new FileInfo(_path);
        if (file.Exists && _index is { } saved && saved.Changed == file.LastWriteTimeUtc && saved.Length == file.Length) return saved.Index;
        try
        {
            var book = ReadCatalog(live, out var interrupted);
            var index = new WidgetLayoutPresetIndex(book.Active, Array.AsReadOnly(book.Presets.Select(e => e.Name).ToArray()));
            _index = file.Exists && !interrupted ? (file.LastWriteTimeUtc, file.Length, index) : null;
            return index;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { _index = null; return InitialIndex; }
    }

    /// <summary>先保存原方案，再切换到一个已有方案。</summary>
    public void Switch(string active, string name, IReadOnlyList<WidgetConfig> live, Action<List<WidgetConfig>> apply) =>
        Change(active, live, b => b with { SwitchingTo = Find(b, name).Name }, apply);

    /// <summary>用默认日期、时间、课表建立并启用新方案，保留原桌面。</summary>
    public void Create(string active, string name, IReadOnlyList<WidgetConfig> live, Action<List<WidgetConfig>> apply) =>
        Change(active, live, b => { var entry = new Entry(NewName(b, name), AppSettings.CreateDefaultWidgets()); b.Presets.Add(entry); return b with { SwitchingTo = entry.Name }; }, apply);

    /// <summary>把当前桌面另存为一个独立方案，不切换当前方案。</summary>
    public void SaveCopy(string active, string name, IReadOnlyList<WidgetConfig> live) =>
        Change(active, live, b => { b.Presets.Add(new(NewName(b, name), Copy(live))); return b; });

    /// <summary>重命名正在编辑的方案，不改变桌面。</summary>
    public void Rename(string active, string name, IReadOnlyList<WidgetConfig> live) =>
        Change(active, live, b => { var entry = Find(b, active); var newName = NewName(b, name, entry); b.Presets[b.Presets.IndexOf(entry)] = entry with { Name = newName }; return b with { Active = newName }; });

    /// <summary>删除一个未启用的方案，当前方案不可删除。</summary>
    public void Delete(string active, string name, IReadOnlyList<WidgetConfig> live) =>
        Change(active, live, b => { var entry = Find(b, name); if (Names.Equals(entry.Name, b.Active)) throw new InvalidOperationException("请先切换到其他方案，再删除此方案。"); b.Presets.Remove(entry); return b; });

    /// <summary>仅在方案文件损坏时，将原件另存为备份，再从当前桌面建立方案。</summary>
    public string Recover(IReadOnlyList<WidgetConfig> live)
    {
        using var lease = BeginWrite();
        try { ReadForWrite(live); }
        catch (InvalidDataException)
        {
            var backup = _path + $".{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.bak";
            File.Copy(_path, backup, false);
            Save(InitialCatalog(live));
            return backup;
        }
        throw new InvalidOperationException("方案文件已可正常读取，不需要恢复。");
    }

    private void Change(string active, IReadOnlyList<WidgetConfig> live, Func<Catalog, Catalog> update, Action<List<WidgetConfig>>? apply = null)
    {
        using var lease = BeginWrite();
        var book = ReadForWrite(live);
        if (!Names.Equals(book.Active, active)) throw new InvalidOperationException("当前方案已在其他窗口更改，请重新打开组件布局。");
        var outgoing = Find(book, active);
        book.Presets[book.Presets.IndexOf(outgoing)] = outgoing with { Widgets = Copy(live) };
        book = update(book);
        if (apply == null) { Save(book); return; }
        var target = Find(book, book.SwitchingTo!);
        Save(book);
        apply(Copy(target.Widgets));
        Save(book with { Active = target.Name, SwitchingTo = null });
    }

    private FileStream BeginWrite()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
    }

    private Catalog ReadForWrite(IReadOnlyList<WidgetConfig> live)
    {
        try { return ReadCatalog(live, out _); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return InitialCatalog(live); }
    }

    private Catalog ReadCatalog(IReadOnlyList<WidgetConfig> live, out bool interrupted)
    {
        interrupted = false;
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > Limit) throw Damaged();
            var book = JsonSerializer.Deserialize<Catalog>(stream, Json);
            if (book is not { Version: 1, Presets.Count: > 0 } || book.Presets.Any(e => e == null || !ValidName(e.Name) || e.Widgets == null || e.Widgets.Any(w => w == null || string.IsNullOrWhiteSpace(w.Type))) ||
                book.Presets.Select(e => e.Name).Distinct(Names).Count() != book.Presets.Count || !book.Presets.Any(e => e.Name == book.Active)) throw Damaged();
            if (book.SwitchingTo is not { } pending) return book;
            var target = book.Presets.FirstOrDefault(e => e.Name == pending) ?? throw Damaged();
            interrupted = true;
            if (SameWidgets(Find(book, book.Active).Widgets, live)) return book with { SwitchingTo = null };
            if (SameWidgets(target.Widgets, live)) return book with { Active = target.Name, SwitchingTo = null };
            var recovered = "恢复的桌面布局";
            for (var i = 2; book.Presets.Any(e => Names.Equals(e.Name, recovered)); i++) recovered = $"恢复的桌面布局 {i}";
            book.Presets.Add(new(recovered, Copy(live)));
            return book with { Active = recovered, SwitchingTo = null };
        }
        catch (JsonException error) { throw Damaged(error); }
    }

    private void Save(Catalog book)
    {
        var contents = JsonSerializer.Serialize(book, Json);
        if (Encoding.UTF8.GetByteCount(contents) > Limit) throw new InvalidOperationException("组件方案文件过大，本次更改没有写入。");
        AtomicFile.Write(_path, contents);
        _index = null;
    }

    private static InvalidDataException Damaged(Exception? inner = null) => new("布局方案文件为空、已损坏或版本不受支持。原文件和桌面布局均已保留。", inner);
    private static Entry Find(Catalog book, string name) => book.Presets.FirstOrDefault(e => Names.Equals(e.Name, name)) ?? throw new InvalidOperationException("此方案已不存在，请重新打开组件布局。");
    private static List<WidgetConfig> Copy(IEnumerable<WidgetConfig> widgets) => widgets.Select(w => w.Copy()).ToList();
    private static bool SameWidgets(IReadOnlyList<WidgetConfig> a, IReadOnlyList<WidgetConfig> b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json);
    private static Catalog InitialCatalog(IReadOnlyList<WidgetConfig> live) => new(1, Initial, [new(Initial, Copy(live))]);
    private static bool ValidName(string? name) => name is { Length: > 0 and <= 64 } && name.Trim() == name && !name.Any(char.IsControl);
    private static string NewName(Catalog book, string name, Entry? current = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        name = name.Trim();
        if (!ValidName(name)) throw new ArgumentException("请输入 1 至 64 个字符的方案名称。", nameof(name));
        if (book.Presets.Any(e => e != current && Names.Equals(e.Name, name))) throw new InvalidOperationException("已有同名方案，请换一个名称。");
        return name;
    }
}
