using ClassNex.Models;
using ClassNex.Services;

namespace ClassNex.ViewModels;

/// <summary>组件列表项（供「主界面组件」设置页显示）。</summary>
public sealed class WidgetItem : ViewModelBase
{
    public WidgetItem(WidgetConfig config) => Config = config;

    public WidgetConfig Config { get; }

    public string Type => Config.Type;

    public string Name => WidgetRegistry.DisplayNameOf(Config.Type);

    public string Glyph => WidgetRegistry.GlyphOf(Config.Type);

    public string DisplayText => $"{Name}";

    /// <summary>触发列表文本刷新。</summary>
    public void Refresh() => OnPropertyChanged(nameof(DisplayText));
}
