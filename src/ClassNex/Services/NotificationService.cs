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

        var key = CurrentKey(DateTime.Now);
        if (key == _lastKey)
            return;

        _lastKey = key;

        // 重要通知：上课开始 / 课间休息开始（CI：MainWindowLine 在通知的 Mask 播放时放 RippleEffect）
        if (key.StartsWith("course:") || key.StartsWith("break:"))
        {
            // CI：settings.IsNotificationEffectEnabled && Settings.AllowNotificationEffect &&
            //     !IsAllComponentsHid && Settings.IsMainWindowVisible && !HasSoundsPlayed
            if (AppServices.Settings.AllowNotificationEffect
                && AppServices.Settings.IsMainWindowVisible
                && AppServices.MainWindow is { IsEditMode: false } mainWindow)
            {
                var center = mainWindow.GetIslandCenterOnScreen() ?? new PixelPoint(0, 0);
                _effectWindow.PlayEffect(new RippleEffect(center));
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
