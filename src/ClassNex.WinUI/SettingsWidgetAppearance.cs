using ClassNex.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace ClassNex.WinUI;

public sealed partial class NativeSettingsWindow
{
    private void WidgetAppearance(Func<AppSettings, WidgetConfig> widget)
    {
        var empty = new WidgetPresentation();
        WidgetPresentation Read(AppSettings settings) => widget(settings).Presentation ?? empty;
        void Set(AppSettings settings, WidgetPresentation style) => widget(settings).Presentation = style == empty ? null : style;
        var body = new StackPanel { Spacing = 4 };
        var header = CardHeader("\uE790", "布局与外观", SettingCaption("调整这个组件的尺寸、留白和底色。"));
        header.Margin = new Thickness(0, 12, 0, 12);
        var expander = Target("WidgetAppearance", "布局与外观", new Expander { Header = header, Content = body, MinHeight = SettingsCardHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        var parent = _body;
        _body = body;
        try
        {
            void Length(string id, string title, Func<WidgetPresentation, double?> get, Func<WidgetPresentation, double?, WidgetPresentation> set,
                double maximum = 4096, string hint = "自动", string detail = "")
            {
                var field = Target(id, title, new NumberBox { Minimum = 0, Maximum = maximum, Width = 112, PlaceholderText = hint,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact });
                _bindings.Add(s => { var next = get(Read(s)) ?? double.NaN; if (!field.Value.Equals(next)) field.Value = next; });
                field.ValueChanged += (_, e) =>
                {
                    double? next = double.IsFinite(e.NewValue) ? e.NewValue : null;
                    if (!_refreshing && get(Read(_bridge.ReadSettings())) != next) Edit(s => Set(s, set(Read(s), next)));
                };
                Row(title, detail, field);
            }
            Length("WidgetWidth", "固定宽度", p => p.Width, (p, v) => p with { Width = v }, detail: "留空按内容决定；指定后优先于最小、最大宽度。");
            Length("WidgetMinWidth", "最小宽度", p => p.MinWidth, (p, v) => p with { MinWidth = v, MaxWidth = v > p.MaxWidth ? v : p.MaxWidth });
            Length("WidgetMaxWidth", "最大宽度", p => p.MaxWidth, (p, v) => p with { MaxWidth = v, MinWidth = v < p.MinWidth ? v : p.MinWidth });
            string?[] align = [null, "left", "center", "right", "stretch"];
            Choice("WidgetAlign", "内容对齐", ["跟随组件", "左对齐", "居中", "右对齐", "拉伸"], s => Array.IndexOf(align, Read(s).Alignment),
                (s, v) => Set(s, Read(s) with { Alignment = align[v] }));
            var margins = new Grid { ColumnSpacing = 8, RowSpacing = 4 };
            for (var i = 0; i < 2; i++) { margins.ColumnDefinitions.Add(new()); margins.RowDefinitions.Add(new() { Height = GridLength.Auto }); }
            var sides = new[] { ("Left", "左"), ("Top", "上"), ("Right", "右"), ("Bottom", "下") };
            for (var i = 0; i < sides.Length; i++)
            {
                var index = i; var (key, name) = sides[i];
                var field = Target("WidgetMargin" + key, name + "边距", new NumberBox { Header = name + "边距", PlaceholderText = "默认", MinWidth = 0,
                    Minimum = -160, Maximum = 512, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact });
                _bindings.Add(s => { var values = Read(s).Margin; var v = values is { Length: 4 } ? values[index] : double.NaN; if (!field.Value.Equals(v)) field.Value = v; });
                field.ValueChanged += (_, args) =>
                {
                    if (_refreshing) return;
                    var v = double.IsFinite(args.NewValue) ? args.NewValue : 0;
                    Edit(s => { var p = Read(s); var values = p.Margin is { Length: 4 } m ? m.ToArray() : new double[4]; values[index] = v;
                        Set(s, p with { Margin = values.Any(side => side != 0) ? values : null }); });
                };
                Grid.SetColumn(field, i % 2); Grid.SetRow(field, i / 2); margins.Children.Add(field);
            }
            body.Children.Add(margins);
            Scale("WidgetOpacity", "组件不透明度", 0, 1, .01, s => Read(s).Opacity, (s, v) => Set(s, Read(s) with { Opacity = v }), v => $"{v:P0}");
            var color = Target("WidgetBackground", "组件底色", new Button());
            _bindings.Add(s => color.Content = Read(s).Background ?? "跟随课堂底色");
            color.Click += (_, _) => PickWidgetBackground(color, widget(_bridge.ReadSettings()));
            Row("组件底色", "", color);
            Length("WidgetBackgroundOpacity", "底色不透明度 (%)", p => p.BackgroundOpacity * 100, (p, v) => p with { BackgroundOpacity = v / 100 }, 100, "跟随课堂");
            Length("WidgetRadius", "底色圆角", p => p.CornerRadius, (p, v) => p with { CornerRadius = v }, 512);
            var reset = ActionButton("WidgetAppearanceReset", "恢复默认", () => { CommitInputs(); Edit(s => widget(s).Presentation = null); });
            _bindings.Add(s => reset.IsEnabled = widget(s).Presentation is not null);
            Row("恢复此组件的布局与外观", "保留内容、字号和排列顺序。", reset);
        }
        finally { _body = parent; }
        parent.Children.Add(expander);
    }

    private void PickWidgetBackground(Button anchor, WidgetConfig instance)
    {
        var fallback = new UISettings().GetColorValue(UIColorType.Accent);
        var current = instance.Presentation?.Background;
        var initial = current is { Length: 7 or 9 } && current[0] == '#' && uint.TryParse(current.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var argb)
            ? Color.FromArgb(current.Length == 9 ? (byte)(argb >> 24) : (byte)255, (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb) : fallback;
        var picker = new ColorPicker { Color = initial, IsAlphaEnabled = true, IsHexInputVisible = true, IsMoreButtonVisible = true, Width = 250 };
        var reset = new Button { Content = "跟随课堂底色", HorizontalAlignment = HorizontalAlignment.Stretch };
        var content = new StackPanel { Spacing = 6 }; content.Children.Add(picker); content.Children.Add(reset);
        var flyout = new Flyout { Content = content };
        string? pending = null; var dirty = false;
        var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(80); timer.IsRepeating = false;
        void Apply()
        {
            timer.Stop(); if (!dirty) return; dirty = false;
            if (_closed || !_bridge.ReadSettings().Widgets.Contains(instance)) return;
            Edit(_ => { var style = (instance.Presentation ?? new()) with { Background = pending }; instance.Presentation = style == new WidgetPresentation() ? null : style; });
        }
        timer.Tick += (_, _) => Apply();
        picker.ColorChanged += (_, args) => { var c = args.NewColor; pending = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}"; dirty = true; timer.Start(); };
        reset.Click += (_, _) => { dirty = true; pending = null; Apply(); flyout.Hide(); };
        flyout.Closed += (_, _) => Apply(); flyout.ShowAt(anchor);
    }
}
