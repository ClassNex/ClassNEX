using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ClassNex.Controls.NotificationEffects;
using ClassNex.Models;
using ClassNex.Views;

namespace ClassNex.Services;

/// <summary>
/// 通知服务：每秒核对当前时间点，发生变化时发出重要通知
/// = CI 的全局水波纹（<see cref="RippleEffect"/> + <see cref="TopmostEffectWindow"/>，1:1 移植）。
/// 触发条件照 CI MainWindowLine.ProcessNotification：
/// AllowNotificationEffect && 主界面可见 && 非编辑模式 && 尚未播放过提示音。
/// （ClassWidgets 灵动通知：暂缓，文件保留未接线。）
/// </summary>
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

        // 先同步一次基线（记录当前时间点），避免启动时把「正在上/正在课间」误报成新事件
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

        // 重要通知：上课开始 / 课间休息开始（CI：MainWindowLine 在通知的 Mask 播放时放 RippleEffect）
        if (key.StartsWith("course:") || key.StartsWith("break:"))
        {
            // 提醒设置（1:1 对照 CI ClassNotificationSettings）：
            // 上课提醒 / 下课提醒各自开关；遮罩文字取设置值（默认：上课 / 课间休息）
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

            // CI：settings.IsNotificationEffectEnabled && Settings.AllowNotificationEffect &&
            //     !IsAllComponentsHid && Settings.IsMainWindowVisible && !HasSoundsPlayed
            if (AppServices.Settings.AllowNotificationEffect
                && AppServices.Settings.IsMainWindowVisible
                && AppServices.MainWindow is { IsEditMode: false } mainWindow)
            {
                var maskText = key.StartsWith("break:") ? n.ClassOffMaskText : n.ClassOnMaskText;

                var center = mainWindow.GetIslandCenterOnScreen() ?? new PixelPoint(0, 0);
                _effectWindow.PlayEffect(new RippleEffect(center));
                mainWindow.ShowNotificationMask(maskText);

                // CI：下课（课间）提醒在面具之后还有 Overlay 阶段（ClassOffOverlay：下节课是…）
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

                // CI：通知请求结束后面具淡出（:mask-out 0.2s），这里按类通知默认时长 3 秒
                DispatcherTimer.RunOnce(mainWindow.HideNotificationMask, TimeSpan.FromSeconds(3));
            }
        }
    }

    /// <summary>当前时间点标识：course:{开始}:{科目} / break:{开始} / finished / before / empty。</summary>
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

    /// <summary>时长人性化（1:1 对照 CI ClassNotificationProviderControl.FormatTimeSpan）。</summary>
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

    /// <summary>与 ScheduleWidget.FindCurrent 完全相同的定位逻辑（当前课 / 课间空档）。</summary>
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
