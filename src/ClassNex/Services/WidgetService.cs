using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>
/// 组件服务：主界面组件（Widgets）的增删、排序与配置持久化。
/// </summary>
public interface IWidgetService
{
    IReadOnlyList<WidgetConfig> Widgets { get; }

    IReadOnlyList<WidgetTypeInfo> AvailableTypes { get; }

    /// <summary>是否已添加某类型组件。</summary>
    bool Contains(string type);

    event Action? Changed;

    WidgetConfig Add(string type);

    void Remove(WidgetConfig config);

    /// <summary>上移 / 下移（delta = -1 / +1）。</summary>
    void Move(WidgetConfig config, int delta);

    /// <summary>通知配置已修改（并持久化）。</summary>
    void Save();

    /// <summary>恢复为默认组件布局。</summary>
    void ResetToDefault();
}

public sealed class WidgetService : IWidgetService
{
    private readonly Func<AppSettings> _settings;

    public WidgetService(Func<AppSettings> settingsAccessor) => _settings = settingsAccessor;

    public IReadOnlyList<WidgetConfig> Widgets => _settings().Widgets;

    public IReadOnlyList<WidgetTypeInfo> AvailableTypes => WidgetRegistry.Types;

    public event Action? Changed;

    public bool Contains(string type) =>
        _settings().Widgets.Any(w => w.Type == type);

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

    public void Remove(WidgetConfig config)
    {
        _settings().Widgets.Remove(config);
        Reindex();
        Save();
    }

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

    public void Save()
    {
        AppServices.SaveSettings();
        Changed?.Invoke();
    }

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
