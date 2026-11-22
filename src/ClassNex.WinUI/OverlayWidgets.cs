using ClassNex.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ClassNex.WinUI;

public sealed partial class NativeOverlayWindow
{
    private sealed record WidgetStyle(string Type, double Scale, string? Text, string? Color, bool Seconds, bool Flash,
        bool Fade, double Spacing, bool ShowNext, string? CountdownName, DateTimeOffset? Target, bool CountdownAccent)
    {
        internal static WidgetStyle From(WidgetConfig w) => new(w.Type ?? "", Clamp(w.FontScale, .5, 2.5, 1), w.Text,
            w.UseCustomFontColor ? w.FontColor : null, w.ShowSeconds, w.FlashTimeSeparator, w.FadeCompletedClasses,
            Clamp(w.ScheduleSpacing, .5, 2, 1), w.ShowNextClass, w.CountdownName, w.CountdownTarget, w.UseCountdownAccent);
    }

    private sealed record WidgetView(FrameworkElement View, Action<OverlaySnapshot>? Update, SchedulePart? Schedule = null);
    private WidgetStyle[] _widgetStyles = [];
    private readonly List<WidgetView> _widgetViews = [];
    private bool _bringQueued, _widgetsInitialized;

    private void EnsureWidgets(AppSettings settings, bool force)
    {
        var styles = settings.Widgets.Where(w => w.IsEnabled).OrderBy(w => w.Order).Select(WidgetStyle.From).ToArray();
        if (_widgetsInitialized && !force && styles.AsSpan().SequenceEqual(_widgetStyles)) return;
        _widgetStyles = styles;
        _widgetsInitialized = true;
        _widgetViews.Clear();
        _widgets.Children.Clear();
        _widgets.Spacing = 5;
        foreach (var style in styles)
        {
            var item = CreateWidget(style);
            _widgetViews.Add(item);
            _widgets.Children.Add(item.View);
        }
        if (_widgetViews.Count == 0)
        {
            var hint = Label(14);
            hint.Text = "未启用组件 · 前往组件设置";
            var chip = Chip(hint);
            chip.Tapped += (_, _) => GestureCommand("widgets");
            _widgets.Children.Add(chip);
        }
        _layoutDirty = true;
    }

    private WidgetView CreateWidget(WidgetStyle style)
    {
        var factor = style.Scale;
        var custom = _palette.Custom(style.Color);
        TextBlock Text(double size) => Label(size * factor, custom);
        switch (style.Type)
        {
            case "date":
                var day = Text(9.5);
                var date = Text(15);
                day.Foreground = custom ?? _palette.SecondaryInk;
                var pair = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                pair.Children.Add(day); pair.Children.Add(date);
                var mark = new Border { Width = 3, Height = 20 * factor, CornerRadius = new CornerRadius(2), Background = _palette.Accent, VerticalAlignment = VerticalAlignment.Center };
                var dateRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                dateRow.Children.Add(mark); dateRow.Children.Add(pair);
                dateRow.Children.Add(new Border { Width = 1, Height = 22 * factor, Background = _palette.Edge, Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center });
                return new(dateRow, s => { SetText(day, s.Today.DayText); SetText(date, s.Today.DateText); });
            case "clock":
                var clock = Text(20);
                return new(clock, s => SetText(clock, style.Seconds ? s.Now.ToString("HH:mm:ss") :
                    s.Now.ToString(style.Flash && s.Now.Second % 2 != 0 ? "HH mm" : "HH:mm")));
            case "text":
                var free = Text(14.5);
                return new(free, s => SetText(free, (style.Text ?? "{date}")
                    .Replace("{date}", s.Today.DateText).Replace("{day}", s.Today.DayText).Replace("{time}", s.Now.ToString("HH:mm"))
                    .Replace("{parity}", s.Today.ParityText).Replace("{countdown}", s.CountdownText)
                    .Replace("{current}", s.Today.Current?.Subject ?? "").Replace("{next}", s.Today.Next?.Subject ?? "")));
            case "countdown":
                var countdown = Text(13.5);
                if (custom is null && style.Target.HasValue && style.CountdownAccent) countdown.Foreground = _palette.AccentInk;
                return new(Chip(countdown), s =>
                {
                    if (style.Target is not { } target) { SetText(countdown, s.CountdownText); return; }
                    var days = (target.Date - s.Now.Date).Days;
                    var name = string.IsNullOrWhiteSpace(style.CountdownName) ? "倒计时" : style.CountdownName;
                    SetText(countdown, days switch { > 0 => $"{name} {days} 天", 0 => $"{name} 就是今天", _ => $"{name} 已过 {-days} 天" });
                });
            case "nextclass":
                var next = Text(14.5);
                var nextChip = Chip(next);
                return new(nextChip, s =>
                {
                    var message = s.Today.Current is { } current ? $"正在上 {current.Subject} · 至 {current.EndText}" :
                        s.Today.Next is { } upcoming ? style.ShowNext ? $"下节课 {upcoming.Subject} · {upcoming.StartText}" : "" :
                        s.Today.IsEmpty ? "今天没有课程" : "今日课程已结束";
                    SetText(next, message);
                    SetVisible(nextChip, message.Length != 0);
                });
            case "divider":
                return new(new Border { Width = 1, Height = 22 * factor, Background = custom ?? _palette.Edge, Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center }, null);
            case "schedule":
                var schedule = new SchedulePart(this, style);
                return new(schedule.Row, schedule.Update, schedule);
            default:
                var plugin = Text(14.5);
                var pluginChip = Chip(plugin);
                return new(pluginChip, s =>
                {
                    var value = s.PluginText?.GetValueOrDefault(style.Type);
                    SetVisible(pluginChip, !string.IsNullOrWhiteSpace(value));
                    if (value is not null) SetText(plugin, value);
                });
        }
    }

    private void SetVisible(FrameworkElement element, bool visible)
    {
        var next = visible ? Visibility.Visible : Visibility.Collapsed;
        if (element.Visibility == next) return;
        element.Visibility = next;
        _layoutDirty = true;
    }

    private void UpdateWidgets(OverlaySnapshot snapshot)
    {
        foreach (var widget in _widgetViews) widget.Update?.Invoke(snapshot);
    }

    private void RequestCourseInView()
    {
        if (_bringQueued || !_visible || _surface != Surface.Widgets) return;
        _bringQueued = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _bringQueued = false;
            if (_closed || !_visible || _surface != Surface.Widgets || _scroller.ScrollableWidth <= 1 && _scroller.ScrollableHeight <= 1) return;
            var target = _widgetViews.Select(w => w.Schedule?.Focus).FirstOrDefault(e => e is not null);
            if (target?.XamlRoot is not null) target.StartBringIntoView(new BringIntoViewOptions { HorizontalAlignmentRatio = .5, VerticalAlignmentRatio = .5, AnimationDesired = false });
        });
    }
}
