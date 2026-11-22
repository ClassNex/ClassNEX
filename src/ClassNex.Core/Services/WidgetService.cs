using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>维护当前桌面组件布局及其持久化顺序，不创建具体 UI 控件。</summary>
public interface IWidgetService
{
    /// <summary>当前布局中的实例，包括已停用的组件。</summary>
    IReadOnlyList<WidgetConfig> Widgets { get; }

    /// <summary>可以通过组件库添加的类型元数据。</summary>
    IReadOnlyList<WidgetTypeInfo> AvailableTypes { get; }

    /// <summary>当前布局中是否已有使用此类型键的实例。</summary>
    bool Contains(string type);

    /// <summary>组件配置已提交给设置存储后触发。</summary>
    event Action? Changed;

    /// <summary>以默认参数追加启用的实例，并返回其可编辑配置。</summary>
    WidgetConfig Add(string type);

    /// <summary>移除指定实例，并使剩余实例的顺序编号连续。</summary>
    void Remove(WidgetConfig config);

    /// <summary>按相对偏移调整实例顺序，超出范围时不更改布局。</summary>
    void Move(WidgetConfig config, int delta);

    /// <summary>保存对组件参数的直接编辑，并通知订阅者。</summary>
    void Save();

    /// <summary>用默认的日期、时间和课表组件替换现有布局并保存。</summary>
    void ResetToDefault();
}

/// <summary>在当前设置中保存、排序和恢复桌面组件实例。</summary>
public sealed class WidgetService : IWidgetService
{
    private readonly Func<AppSettings> _settings;
    private readonly Action _saveSettings;

    /// <summary>传入当前设置及其持久化回调，不要求具体桌面入口。</summary>
    public WidgetService(Func<AppSettings> settingsAccessor, Action saveSettings)
    {
        _settings = settingsAccessor;
        _saveSettings = saveSettings;
    }

    /// <inheritdoc />
    public IReadOnlyList<WidgetConfig> Widgets => _settings().Widgets;

    /// <inheritdoc />
    public IReadOnlyList<WidgetTypeInfo> AvailableTypes => WidgetRegistry.Types;

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public bool Contains(string type) =>
        _settings().Widgets.Any(w => w.Type == type);

    /// <inheritdoc />
    public WidgetConfig Add(string type)
    {
        var config = new WidgetConfig
        {
            Type = type,
            IsEnabled = true,
            Order = _settings().Widgets.Count,
        };

        _settings().Widgets.Add(config);
        Save();
        return config;
    }

    /// <inheritdoc />
    public void Remove(WidgetConfig config)
    {
        _settings().Widgets.Remove(config);
        Reindex();
        Save();
    }

    /// <inheritdoc />
    public void Move(WidgetConfig config, int delta)
    {
        var list = _settings().Widgets;
        var index = list.IndexOf(config);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= list.Count)
            return;

        (list[index], list[target]) = (list[target], list[index]);
        Reindex();
        Save();
    }

    /// <inheritdoc />
    public void Save()
    {
        _saveSettings();
        Changed?.Invoke();
    }

    /// <inheritdoc />
    public void ResetToDefault()
    {
        _settings().Widgets = AppSettings.CreateDefaultWidgets();
        Reindex();
        Save();
    }

    private void Reindex()
    {
        var list = _settings().Widgets;
        for (var i = 0; i < list.Count; i++)
            list[i].Order = i;
    }
}
