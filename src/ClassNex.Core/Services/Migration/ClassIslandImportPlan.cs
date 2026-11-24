using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>本次选择的 ClassIsland 2.x 安装与配置位置；仅用于本机导入和核对回收目标。</summary>
public sealed record ClassIslandSource(string InstallRoot, string DataRoot, string SelectedExe)
{
    /// <summary>预检时读取的包类型；未声明时为空。只有显式的 folder 包可以申请回收。</summary>
    public string PackageType { get; internal init; } = "";
}

/// <summary>已解析的导入预览。读取此对象不会写入 ClassNEX 或 ClassIsland。</summary>
public sealed class ClassIslandImportPlan
{
    internal ClassIslandImportPlan(ClassIslandSource source) => Source = source;

    /// <summary>此次选择的来源。</summary>
    public ClassIslandSource Source { get; }
    /// <summary>已识别的可迁移组件实例数，重复实例分别计数。</summary>
    public int WidgetCount => Widgets.Count;
    /// <summary>不受支持的组件或模式数量。</summary>
    public int SkippedWidgetCount { get; internal set; }
    /// <summary>转成 CSES 的本地档案数。</summary>
    public int ProfileCount => Profiles.Count;
    /// <summary>不可表示的内容，不含账号或插件设置。</summary>
    public IReadOnlyList<string> Warnings => Notes;
    internal List<string> Notes { get; } = [];
    internal List<WidgetConfig> Widgets { get; } = [];
    internal List<(string Name, ScheduleProfile Profile, TimeLayout? Layout, bool Active)> Profiles { get; } = [];
    internal AppSettings Appearance { get; } = new();
    internal Dictionary<string, byte[]> SourceHashes { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal string[] ProfileFiles { get; set; } = [];
    internal List<string> MissingFiles { get; } = [];
    internal void Warn(string text) { if (!Notes.Contains(text) && Notes.Count < 60) Notes.Add(text); }
}

/// <summary>仅在设置和全部新档案通过回读校验后返回；源文件未由导入器更动。</summary>
public sealed record ClassIslandImportResult(ClassIslandSource Source, AppSettings Settings, string? ActiveProfilePath);
