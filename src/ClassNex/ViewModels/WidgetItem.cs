using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;

namespace ClassNex.ViewModels;


public sealed class WidgetItem : ViewModelBase
{
    public WidgetItem(WidgetConfig config) => Config = config;

    public WidgetConfig Config { get; }

    public string Type => Config.Type;

    public string Name => WidgetRegistry.DisplayNameOf(Config.Type);

    public string Glyph => SettingsIcons.WidgetGlyph(Config.Type);

    public string DisplayText => $"{Name}";

    
    public void Refresh() => OnPropertyChanged(nameof(DisplayText));
}
