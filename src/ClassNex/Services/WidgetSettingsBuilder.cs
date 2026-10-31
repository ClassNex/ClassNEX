using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaFluentUI.Controls;
using ClassNex.Models;

namespace ClassNex.Services;














public static class WidgetSettingsBuilder
{
    private static readonly FontFamily IconFont = new("avares://ClassNex/Assets/Fonts/#lucide");

    
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
            
        }

        return rows;
    }

    
    public static bool HasSettings(string type) => type is "text" or "clock" or "schedule" or "nextclass" or "countdown";

    

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

    

    private static void BuildClock(WidgetConfig cfg, List<Control> rows, Action changed)
    {
        
        
        var flashRow = Row("\uE9D9", "闪动时间分隔符", "模拟电子时钟的分隔符闪动效果。",
            Check(cfg.FlashTimeSeparator, on => { cfg.FlashTimeSeparator = on; changed(); }),
            visible: !cfg.ShowSeconds);

        var seconds = Check(cfg.ShowSeconds, on =>
        {
            cfg.ShowSeconds = on;
            flashRow.IsVisible = !on; 
            changed();
        });
        rows.Add(Row("\uEC4A", "显示秒数", "启用后，时钟将显示精准到秒的时间。", seconds));
        rows.Add(flashRow);
    }

    

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

    

    private static void BuildNextClass(WidgetConfig cfg, List<Control> rows, Action changed)
    {
        rows.Add(Row("\uE8AB", "显示下一节课", "关闭后只显示当前正在上的课。",
            Check(cfg.ShowNextClass, on => { cfg.ShowNextClass = on; changed(); })));
    }

    

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
