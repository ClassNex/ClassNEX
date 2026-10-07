using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaFluentUI.Controls;
using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>
/// 「组件设置」的**按类型**设置项构建器 —— 对照 CI 的
/// <c>ClassIsland/Controls/Components/XxxComponentSettingsControl.axaml</c>：
/// 每种组件类型只显示**它自己的**设置项，互不共用。
///
/// CI 里没有设置控件的组件（DateComponent、SeparatorComponent）在这里同样没有设置项，
/// 调用方会显示「该组件没有专属设置」。
///
/// 与 CI 的两点差异（本项目的 UI 库 AvaloniaFluentUI 1.0.3 缺这两个控件，
/// 按约定「CI 有、库没有 → 用类似的」处理）：
///   1. CI 用 <c>ToggleSwitch</c>，这里用 <c>CheckBox</c>；
///   2. CI 用 <c>ColorPicker</c>，这里用**预设色下拉框**（颜色是「多选一」，也符合本项目「多选一用下拉框」的规矩）。
/// </summary>
public static class WidgetSettingsBuilder
{
    private static readonly FontFamily IconFont = new("Segoe MDL2 Assets");

    /// <summary>文本组件可选颜色（预设色卡；第一项是「跟随主题」）。</summary>
    private static readonly (string Name, string? Hex)[] TextColors =
    {
        ("跟随主题", null),
        ("白色", "#FFFFFFFF"),
        ("浅灰", "#FFC8C8C8"),
        ("黑色", "#FF000000"),
        ("强调色", "#FF4CC2FF"),
        ("青色", "#FF7FFFD4"),
        ("绿色", "#FF6CCB5F"),
        ("黄色", "#FFF4EF74"),
        ("橙色", "#FFF7A501"),
        ("红色", "#FFE81123"),
        ("紫色", "#FF8B5CF6"),
    };

    /// <summary>
    /// 构建指定组件配置的专属设置行。
    /// <paramref name="changed"/> 在任一设置变化后调用（用于保存并刷新浮窗）。
    /// </summary>
    public static List<Control> Build(WidgetConfig cfg, Action changed)
    {
        var rows = new List<Control>();

        switch (cfg.Type)
        {
            case "text":
                BuildText(cfg, rows, changed);
                break;
            case "clock":
                BuildClock(cfg, rows, changed);
                break;
            case "schedule":
                BuildSchedule(cfg, rows, changed);
                break;
            case "nextclass":
                BuildNextClass(cfg, rows, changed);
                break;
            case "countdown":
                BuildCountdown(cfg, rows, changed);
                break;
            // date / divider：CI 的 DateComponent / SeparatorComponent 都没有设置控件 → 无专属设置
        }

        return rows;
    }

    /// <summary>该组件类型是否有专属设置（CI：有没有 XxxComponentSettingsControl）。</summary>
    public static bool HasSettings(string type) => type is "text" or "clock" or "schedule" or "nextclass" or "countdown";

    // ============================== 「文本」组件（CI TextComponentSettingsControl） ==============================

    private static void BuildText(WidgetConfig cfg, List<Control> rows, Action changed)
    {
        var box = new TextBox
        {
            Width = 300,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Text = cfg.Text ?? "",
            Watermark = "支持 {date} {day} {time} {parity} {current} {next} {countdown}",
        };
        box.TextChanged += (_, _) =>
        {
            cfg.Text = box.Text;
            changed();
        };
        rows.Add(Row("\uE70F", "文本内容", "要显示的文本，支持占位符。", box));

        // CI：自定义字体颜色 = 勾选 + 颜色选择器（这里颜色用预设色下拉框）
        var colorCombo = new ComboBox
        {
            Width = 150,
            ItemsSource = TextColors.Select(c => c.Name).ToList(),
            SelectedIndex = Math.Max(0, Array.FindIndex(TextColors, c => c.Hex == cfg.FontColor)),
        };
        var colorRow = Row("\uE790", "字体颜色", "自定义文本的颜色。", colorCombo,
            visible: cfg.UseCustomFontColor);
        colorCombo.SelectionChanged += (_, _) =>
        {
            var i = colorCombo.SelectedIndex;
            if (i < 0 || i >= TextColors.Length)
                return;
            cfg.FontColor = TextColors[i].Hex;
            changed();
        };

        var useCustom = Check(cfg.UseCustomFontColor, on =>
        {
            cfg.UseCustomFontColor = on;
            colorRow.IsVisible = on;
            changed();
        });
        rows.Add(Row("\uEC4A", "自定义字体颜色", "启用后使用下面选定的颜色。", useCustom));
        rows.Add(colorRow);
    }

    // ============================== 「时钟」组件（CI ClockComponentSettingsControl） ==============================

    private static void BuildClock(WidgetConfig cfg, List<Control> rows, Action changed)
    {
        // 注：CI 的「使用实际时间」依赖时间偏移功能，本项目暂无时间偏移，故不提供该设置项
        // （字段 ShowRealTime 保留在模型里，将来做时间偏移时再接）。
        var flashRow = Row("\uE9D9", "闪动时间分隔符", "模拟电子时钟的分隔符闪动效果。",
            Check(cfg.FlashTimeSeparator, on => { cfg.FlashTimeSeparator = on; changed(); }),
            visible: !cfg.ShowSeconds);

        var seconds = Check(cfg.ShowSeconds, on =>
        {
            cfg.ShowSeconds = on;
            flashRow.IsVisible = !on; // CI：显示秒时不显示「闪动分隔符」
            changed();
        });
        rows.Add(Row("\uEC4A", "显示秒数", "启用后，时钟将显示精准到秒的时间。", seconds));
        rows.Add(flashRow);
    }

    // ============================== 「课程表」组件（CI LessonControlSettings 的子集） ==============================

    private static void BuildSchedule(WidgetConfig cfg, List<Control> rows, Action changed)
    {
        rows.Add(Row("\uE8A5", "淡化上过的课程", "已经上过的课程将被淡化显示。",
            Check(cfg.FadeCompletedClasses, on => { cfg.FadeCompletedClasses = on; changed(); })));

        var valueText = new TextBlock
        {
            Text = $"{cfg.ScheduleSpacing:0.00}x",
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.7,
            MinWidth = 48,
        };
        var slider = new Slider
        {
            Minimum = 0.5,
            Maximum = 1.5,
            Value = Math.Clamp(cfg.ScheduleSpacing, 0.5, 1.5),
            Width = 220,
            VerticalAlignment = VerticalAlignment.Center,
        };
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty)
                return;
            cfg.ScheduleSpacing = slider.Value;
            valueText.Text = $"{slider.Value:0.00}x";
            changed();
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(slider);
        panel.Children.Add(valueText);
        rows.Add(Row("\uE71E", "课程表文本间距", "使课程表显示更紧凑或宽松。", panel));
    }

    // ============================== 「当前 / 下节课」组件（CI 无此组件，本项目自定） ==============================

    private static void BuildNextClass(WidgetConfig cfg, List<Control> rows, Action changed)
    {
        rows.Add(Row("\uE8AB", "显示下一节课", "关闭后只显示当前正在上的课。",
            Check(cfg.ShowNextClass, on => { cfg.ShowNextClass = on; changed(); })));
    }

    // ============================== 「倒计时」组件（CI CountDownComponentSettings 的子集） ==============================

    private static void BuildCountdown(WidgetConfig cfg, List<Control> rows, Action changed)
    {
        var nameBox = new TextBox
        {
            Width = 240,
            Text = cfg.CountdownName ?? "",
            Watermark = "会作为事件名称显示",
        };
        nameBox.TextChanged += (_, _) =>
        {
            cfg.CountdownName = nameBox.Text;
            changed();
        };
        rows.Add(Row("\uE916", "倒计时名称", "会作为设定日期事件名称显示。", nameBox));

        var picker = new DatePicker
        {
            Width = 220,
            SelectedDate = cfg.CountdownTarget,
        };
        picker.SelectedDateChanged += (_, _) =>
        {
            cfg.CountdownTarget = picker.SelectedDate;
            changed();
        };
        rows.Add(Row("\uEC4A", "目标日期", "倒计时为 0 天时的日期。", picker));

        rows.Add(Row("\uE790", "使用强调色", "启用后事件名与倒计时使用应用强调色。",
            Check(cfg.UseCountdownAccent, on => { cfg.UseCountdownAccent = on; changed(); })));
    }

    // ============================== 小工具 ==============================

    /// <summary>一行设置（CI 的 SettingsExpander：图标 + 标题 + 说明 + 右侧常显控件）。</summary>
    private static SettingsExpander Row(string glyph, string header, string description, Control footer, bool visible = true)
        => new()
        {
            IconSource = new FontIconSource
            {
                FontFamily = IconFont,
                Glyph = glyph,
                FontSize = 16,
            },
            Header = header,
            Description = description,
            Footer = footer,
            IsVisible = visible,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

    private static CheckBox Check(bool value, Action<bool> set)
    {
        var box = new CheckBox { IsChecked = value, MinWidth = 0 };
        box.IsCheckedChanged += (_, _) => set(box.IsChecked == true);
        return box;
    }
}
