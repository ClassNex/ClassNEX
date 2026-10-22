using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ClassNex.Controls.NotificationEffects;
using ClassNex.Models;
using ClassNex.Views;

namespace ClassNex.Services;








public sealed class NotificationService
{
    private readonly DispatcherTimer _timer;
    private readonly TopmostEffectWindow _effectWindow;

    private string _lastKey = "\0";
    private bool _started;

    public NotificationService(TopmostEffectWindow effectWindow)
    {
        _effectWindow = effectWindow;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => OnTick();
    }

    public void Start()
    {
        if (_started)
            return;
        _started = true;

        
        _lastKey = CurrentKey(DateTime.Now);
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _started = false;
    }

    private void OnTick()
    {
        if (!AppServices.Settings.AllowNotification)
            return;

        var now = DateTime.Now;
        var key = CurrentKey(now);
        if (key == _lastKey)
            return;

        _lastKey = key;

        
        if (key.StartsWith("course:") || key.StartsWith("break:"))
            PlayNotification(key, now);
    }

    
    
    
    
    private void PlayNotification(string key, DateTime now)
    {
        
        
        var n = AppServices.Settings.Notification;
        if (key.StartsWith("course:"))
        {
            if (!n.IsClassOnNotificationEnabled)
                return;
        }
        else
        {
            if (!n.IsClassOffNotificationEnabled)
                return;
        }

        
        
        if (AppServices.Settings.AllowNotificationEffect
            && AppServices.Settings.IsMainWindowVisible
            && AppServices.MainWindow is { IsEditMode: false } mainWindow)
        {
            var maskText = key.StartsWith("break:") ? n.ClassOffMaskText : n.ClassOnMaskText;

            var center = mainWindow.GetIslandCenterOnScreen() ?? new PixelPoint(0, 0);
            _effectWindow.PlayEffect(new RippleEffect(center));
            mainWindow.ShowNotificationMask(maskText);

            
            if (key.StartsWith("break:"))
            {
                var summary = AppServices.Time.GetTodaySummary(AppServices.Schedule.Profile, now);
                var next = AppServices.Time.GetNextCourse(summary.Slots, now.TimeOfDay);
                if (next is { } nxt)
                {
                    var breakStart = TimeSpan.Parse(key["break:".Length..]);
                    var gap = next.Start - breakStart;
                    var teacher = n.ShowTeacherName && !string.IsNullOrWhiteSpace(nxt.Teacher)
                        ? nxt.Teacher!
                        : "";
                    var left = $"本节{n.ClassOffMaskText}长 {FormatDuration(gap)}";
                    DispatcherTimer.RunOnce(() => mainWindow.ShowNotificationOverlay(
                            left, nxt.DisplayName, teacher, nxt.TimeRange),
                        TimeSpan.FromMilliseconds(1500));
                    DispatcherTimer.RunOnce(mainWindow.HideNotificationMask, TimeSpan.FromMilliseconds(3500));
                    return;
                }
            }

            
            DispatcherTimer.RunOnce(mainWindow.HideNotificationMask, TimeSpan.FromSeconds(3));
        }
    }

    
    public void DebugTriggerClassOn()
    {
        var now = DateTime.Now;
        PlayNotification($"course:{now:HHmmss}:debug", now);
    }

    
    public void DebugTriggerClassOff()
    {
        var now = DateTime.Now;
        PlayNotification($"break:{now:HH\\:mm\\:ss}", now);
    }

    
    private string CurrentKey(DateTime now)
    {
        var summary = AppServices.Time.GetTodaySummary(AppServices.Schedule.Profile, now);
        var cur = FindCurrent(summary.Slots, now.TimeOfDay);

        if (cur is { } c)
            return c.Title == "课间休息" ? $"break:{c.Start}" : $"course:{c.Start}:{c.Title}";

        if (summary.IsFinished)
            return "finished";

        return summary.Slots.Count == 0 ? "empty" : "before";
    }

    
    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalSeconds <= 0)
            return "0 分钟";

        var parts = new List<string>(3);
        if (span.Hours > 0)
            parts.Add($"{span.Hours} 小时");
        if (span.Minutes > 0)
            parts.Add(span.Seconds > 0 ? $"{span.Minutes} 分" : $"{span.Minutes} 分钟");
        if (span.Seconds > 0)
            parts.Add($"{span.Seconds} 秒");

        return string.Join(" ", parts);
    }

    
    private static (string Title, TimeSpan Start, TimeSpan End)? FindCurrent(IReadOnlyList<CourseSlot> slots, TimeSpan now)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            if (i > 0)
            {
                var gapStart = slots[i - 1].End;
                var gapEnd = slots[i].Start;
                if (gapEnd > gapStart && now >= gapStart && now < gapEnd)
                    return ("课间休息", gapStart, gapEnd);
            }

            if (slots[i].Contains(now))
                return (slots[i].Subject, slots[i].Start, slots[i].End);
        }

        return null;
    }
}
