namespace ClassNex.Core.Abstractions;

/// <summary>插件发现与生命周期的兼容接口；启停管理通过 IPluginManager 提供。</summary>
public interface IPluginService
{
    /// <summary>最近一次发现结果的只读快照，含禁用、不兼容和加载失败项。</summary>
    IReadOnlyList<PluginInfo> LoadedPlugins { get; }

    /// <summary>新快照已发布；不保证在 UI 线程触发。</summary>
    event Action? Changed;

    /// <summary>重新发现插件，并按依赖顺序在后台初始化可用项。</summary>
    Task LoadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>按与启动相反的顺序关闭实例并释放宿主持有的引用。</summary>
    void StopAll();
}

/// <summary>Changes the local enabled preference without modifying the author's manifest.</summary>
public interface IPluginManager : IPluginService
{
    /// <summary>Applies the change to this plugin and re-evaluates its dependents.</summary>
    Task SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken = default);
}

/// <summary>已发现插件在某次扫描后的展示快照。</summary>
/// <param name="Id">清单中的稳定标识，用于依赖和本机启停偏好。</param>
/// <param name="Name">插件作者指定的可读名称。</param>
/// <param name="Version">清单声明的插件版本，不是宿主 API 版本。</param>
/// <param name="AssemblyPath">已解析的入口程序集路径；清单损坏时可能为空。</param>
/// <param name="Status">本次发现、依赖检查或初始化的结果。</param>
public sealed record PluginInfo(
    string Id,
    string Name,
    string Version,
    string AssemblyPath,
    string Status)
{
    /// <summary>本机偏好覆盖清单后的有效启用状态，不保证已成功运行。</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>标识唯一且清单有效时，管理页可修改启用偏好。</summary>
    public bool CanToggle { get; init; } = true;
    /// <summary>声明的宿主能力；旧清单显示兼容模式说明。</summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];
}

/// <summary>插件目录中 plugin.json 的公共格式，扩展字段可省略以读取 Alpha 清单。</summary>
/// <param name="Id">不区分大小写的插件标识，不随显示名称改变。</param>
/// <param name="Name">用于管理页的显示名。</param>
/// <param name="Version">插件自身版本；作为依赖项时与最低数字版本比较。</param>
/// <param name="Assembly">相对插件目录的入口 DLL 路径。</param>
/// <param name="EntryType">实现 IClassNexPlugin 的公开具体类型全名。</param>
/// <param name="Enabled">尚无本机启停偏好时的初始值。</param>
public sealed record PluginManifest(
    string Id,
    string Name,
    string Version,
    string Assembly,
    string EntryType,
    bool Enabled = true)
{
    /// <summary>此插件要求的宿主契约级别；省略时为 1。</summary>
    public int ApiVersion { get; init; } = 1;
    /// <summary>必需插件 ID 到最低数字版本的映射。</summary>
    public Dictionary<string, string> Dependencies { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>null 表示 Alpha 兼容访问，空数组表示不申请任何宿主能力。</summary>
    public string[]? Permissions { get; init; }
}

/// <summary>可信进程内插件的入口契约，Core 中保留 Alpha 的二进制类型标识。</summary>
public interface IClassNexPlugin
{
    /// <summary>接收宿主上下文；订阅的事件和创建的资源须在 Shutdown 中释放。</summary>
    void Initialize(PluginContext context);

    /// <summary>释放资源并停止后台活动；部分初始化后也可能被调用。</summary>
    void Shutdown();
}

/// <summary>为一个插件实例提供本机配置目录和宿主允许的业务服务；不构成安全沙箱。</summary>
public sealed class PluginContext
{
    /// <summary>Stable writable location for this plugin, separate from its installed binaries.</summary>
    public string ConfigurationDirectory { get; init; } = "";

    /// <summary>Typed, permission-checked access for new plugins; Services remains for Alpha compatibility.</summary>
    public ClassNex.Services.IScheduleService? Schedule => Services.GetService(typeof(ClassNex.Services.IScheduleService)) as ClassNex.Services.IScheduleService;

    /// <summary>获得课表时间查询能力，未授权或未提供时为空。</summary>
    public ClassNex.Services.ITimeService? Time => Services.GetService(typeof(ClassNex.Services.ITimeService)) as ClassNex.Services.ITimeService;

    /// <summary>获得桌面组件配置能力，未授权或未提供时为空。</summary>
    public ClassNex.Services.IWidgetService? Widgets => Services.GetService(typeof(ClassNex.Services.IWidgetService)) as ClassNex.Services.IWidgetService;

    /// <summary>为兼容旧插件保留的服务查询入口；新清单按声明的能力筛选。</summary>
    public required IServiceProvider Services { get; init; }

    /// <summary>通知控制接口；显式未申请此能力的插件调用时会被拒绝。</summary>
    public required INotificationService Notification { get; init; }

    /// <summary>NEX 会话入口；显式未申请此能力的插件调用时会被拒绝。</summary>
    public required IAiService Ai { get; init; }
}

/// <summary>兼容 Alpha 插件的实例表；由宿主在插件启动前登记，不负责创建或销毁服务。</summary>
public sealed class PluginServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = new();

    /// <summary>按契约类型登记实例；同类型再次登记时替换旧值。</summary>
    public void Add<T>(T service) where T : class
    {
        _services[typeof(T)] = service;
    }

    /// <summary>返回以该精确类型登记的实例，没有登记时为 null。</summary>
    public object? GetService(Type serviceType) =>
        _services.TryGetValue(serviceType, out var service) ? service : null;
}
