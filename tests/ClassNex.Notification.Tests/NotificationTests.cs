using ClassNex.Core.Abstractions;
using ClassNex.Models;
using ClassNex.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace ClassNex.Notification.Tests;

[TestClass]
public sealed class NotificationTests
{
    private sealed class Clock(int hour = 0, int minute = 0, int second = 0) : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 16, hour, minute, second, TimeSpan.Zero);
        private readonly List<ManualTimer> _timers = [];
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => _now;
        public int Created => _timers.Count;
        public int Ticks { get; private set; }
        public void Sleep(double seconds) => _now = _now.AddSeconds(seconds);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(double seconds)
        {
            var target = _now.AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
            while (_timers.Where(t => t.Due.HasValue && t.Due <= target).MinBy(t => t.Due) is { } timer)
            {
                _now = timer.Due!.Value;
                timer.Fire();
                if (++Ticks > 100000) throw new InvalidOperationException("Timer did not make progress.");
            }
            _now = target;
        }

        private sealed class ManualTimer(Clock clock, TimerCallback callback, object? state) : ITimer
        {
            private bool _disposed;
            public DateTimeOffset? Due { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (_disposed) return false;
                Assert.AreEqual(Timeout.InfiniteTimeSpan, period);
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
                return true;
            }
            public void Fire() { Due = null; callback(state); }
            public void Dispose() { Due = null; _disposed = true; }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private sealed record Overlay(string Caption, string Subject, string Teacher, string Time, string Message, bool Vertical, TimeSpan Track);

    private sealed class Surface : INotificationSurface
    {
        public bool IsAvailable { get; set; } = true;
        public bool ThrowOnMask { get; set; }
        public List<string> Masks { get; } = [];
        public List<TimeSpan> Lifetimes { get; } = [];
        public List<Overlay> Overlays { get; } = [];
        public List<string> Countdowns { get; } = [];
        public int Hides { get; private set; }
        public void ShowMask(string text, string leftGlyph, string rightGlyph, bool playEffect)
        {
            if (ThrowOnMask) throw new InvalidOperationException("Surface closed");
            Masks.Add(text);
        }
        public void ShowMask(string text, string leftGlyph, string rightGlyph, bool playEffect, TimeSpan lifetime)
        {
            ShowMask(text, leftGlyph, rightGlyph, playEffect);
            Lifetimes.Add(lifetime);
        }
        public void ShowOverlay(string left, string subject, string teacher, string time, string prefix, string message, bool verticalPageTurn, TimeSpan trackDuration)
            => Overlays.Add(new(left, subject, teacher, time, message, verticalPageTurn, trackDuration));
        public void UpdateCountdown(string text) => Countdowns.Add(text);
        public void Hide() => Hides++;
    }

    private sealed class Fixture : IDisposable
    {
        public readonly Clock Clock;
        public readonly AppSettings Settings = new();
        public readonly Surface Screen = new();
        public readonly ScheduleService Schedule;
        public readonly NotificationService Service;
        public Fixture(Clock? clock = null, Surface? secondary = null)
        {
            Clock = clock ?? new();
            Schedule = new(() => Settings);
            var channels = new Dictionary<string, INotificationSurface> { ["classroom"] = Screen };
            if (secondary != null) channels.Add("secondary", secondary);
            Service = new(channels, () => Settings, new TimeService(() => Settings), Schedule, Clock);
        }
        public void Lesson(string name, string alias, string start, string end, bool outdoor = false)
        {
            Schedule.Profile.Subjects.Add(new() { Name = name, SimplifiedName = alias, Teacher = "蔡老师", IsOutDoor = outdoor });
            Schedule.Profile.GetOrCreateSchedule(1, "all").Classes.Add(new() { Subject = name, StartTime = start, EndTime = end });
        }
        public NotificationRequest Request(string id, int duration = 3, int ttl = 60, NotificationPriority priority = NotificationPriority.Normal, string channel = "classroom")
            => new(id, id, Clock.GetUtcNow().AddSeconds(ttl), TimeSpan.FromSeconds(duration), TimeSpan.FromSeconds(duration), priority, channel);
        public void Dispose() => Service.Dispose();
    }

    [TestMethod]
    public void IdleUsesOneTimerAndStopCancelsAllWakeups()
    {
        using var f = new Fixture();
        f.Service.Start();
        f.Service.Start();
        f.Clock.Advance(600);
        Assert.AreEqual(1, f.Clock.Created);
        Assert.AreEqual(20, f.Clock.Ticks);
        f.Service.Stop();
        f.Clock.Advance(3600);
        Assert.AreEqual(20, f.Clock.Ticks);
        f.Service.Start();
        Assert.AreEqual(1, f.Clock.Created);
        f.Service.Dispose();
        f.Clock.Advance(600);
        Assert.AreEqual(20, f.Clock.Ticks);
    }

    [TestMethod]
    public void QueueIsStableAndSkipsExpiredCancelledAndDuplicateRequests()
    {
        using var f = new Fixture();
        f.Service.Start();
        Assert.IsTrue(f.Service.Publish(f.Request("active")));
        Assert.IsTrue(f.Service.Publish(f.Request("expired", ttl: 1)));
        Assert.IsTrue(f.Service.Publish(f.Request("one")));
        Assert.IsTrue(f.Service.Publish(f.Request("removed")));
        Assert.IsTrue(f.Service.Publish(f.Request("two")));
        Assert.IsFalse(f.Service.Publish(f.Request("one")));
        Assert.IsTrue(f.Service.Cancel("removed"));
        f.Clock.Advance(12);
        CollectionAssert.AreEqual(new[] { "active", "one", "two" }, f.Screen.Masks);
        Assert.AreEqual(3, f.Screen.Hides);
        Assert.IsFalse(f.Service.Publish(f.Request("active")));
        f.Clock.Advance(55);
        Assert.IsTrue(f.Service.Publish(f.Request("active")));
        Assert.IsFalse(f.Service.Publish(f.Request("no-time", ttl: 0)));
        Assert.IsFalse(f.Service.Publish(f.Request("no-channel", channel: "missing")));
    }

    [TestMethod]
    public void ImportantInterruptsLowerPriorityAndCancellationDispatchesNext()
    {
        using var f = new Fixture();
        f.Service.Start();
        f.Service.Publish(f.Request("low", priority: NotificationPriority.Low));
        f.Service.Publish(f.Request("low-waiting", priority: NotificationPriority.Low));
        f.Service.Publish(f.Request("priority", priority: NotificationPriority.Important));
        f.Service.Publish(f.Request("normal"));
        Assert.IsTrue(f.Service.Cancel("priority"));
        f.Clock.Advance(8);
        CollectionAssert.AreEqual(new[] { "low", "priority", "normal", "low-waiting" }, f.Screen.Masks);
        Assert.IsFalse(f.Service.Cancel("not-present"));
    }

    [TestMethod]
    public void HiddenOrFaultedChannelCannotBlockHealthyChannel()
    {
        var secondary = new Surface { IsAvailable = false };
        using var f = new Fixture(secondary: secondary);
        var failures = new List<string>();
        f.Service.Failed += (channel, _) => failures.Add(channel);
        f.Service.Start();
        f.Service.Publish(f.Request("hidden", channel: "secondary"));
        secondary.IsAvailable = true;
        secondary.ThrowOnMask = true;
        f.Service.Publish(f.Request("failed", channel: "secondary"));
        f.Service.Publish(f.Request("working"));
        CollectionAssert.AreEqual(new[] { "secondary" }, failures);
        CollectionAssert.AreEqual(new[] { "working" }, f.Screen.Masks);
        Assert.IsEmpty(secondary.Masks);
    }

    [TestMethod]
    public void PreparationShowsFullSubjectAndExpiresAtExactClassBoundary()
    {
        using var f = new Fixture(new Clock(7, 56, 59));
        f.Settings.Notification.ShowTeacherName = true;
        f.Lesson("思想政治", "政", "08:00", "08:45");
        f.Service.Start();
        f.Clock.Advance(1);
        Assert.AreEqual("即将上课", f.Screen.Masks.Single());
        Assert.AreEqual(TimeSpan.FromMinutes(3), f.Screen.Lifetimes.Single());
        f.Clock.Advance(3);
        var page = f.Screen.Overlays.Single();
        Assert.AreEqual("思想政治", page.Subject);
        Assert.AreEqual("蔡老师", page.Teacher);
        Assert.AreEqual("08:00–08:45", page.Time);
        Assert.AreEqual("距上课还剩 177 秒", page.Caption);
        Assert.IsTrue(page.Vertical);
        Assert.AreEqual(f.Settings.Notification.ClassOnPreparingText, page.Message);
        Assert.AreEqual(TimeSpan.FromSeconds(177), page.Track);
        f.Clock.Advance(1);
        Assert.AreEqual("距上课还剩 176 秒", f.Screen.Countdowns.Single());
        f.Clock.Advance(176);
        CollectionAssert.AreEqual(new[] { "即将上课", "上课" }, f.Screen.Masks);
        f.Clock.Advance(5);
        Assert.AreEqual(2, f.Screen.Hides);
        f.Clock.Advance(45 * 60 - 5);
        Assert.AreEqual("课间休息", f.Screen.Masks.Last());
        f.Clock.Advance(8);
        Assert.AreEqual(3, f.Screen.Hides);
        Assert.HasCount(1, f.Screen.Overlays);
    }

    [TestMethod]
    public void BreakUsesFullNextCourseAndTrackUsesActualRemainingLifetime()
    {
        using var f = new Fixture(new Clock(8, 44, 59));
        f.Lesson("语文", "语", "08:00", "08:45");
        f.Lesson("信息技术", "信", "09:00", "09:45");
        f.Service.Start();
        f.Clock.Advance(1);
        Assert.AreEqual("课间休息", f.Screen.Masks.Single());
        Assert.AreEqual(TimeSpan.FromSeconds(8), f.Screen.Lifetimes.Single());
        f.Clock.Advance(1.2);
        var page = f.Screen.Overlays.Single();
        Assert.AreEqual("信息技术", page.Subject);
        Assert.AreEqual("课间 15 分钟", page.Caption);
        Assert.AreEqual("", page.Teacher);
        Assert.AreEqual(TimeSpan.FromMilliseconds(6800), page.Track);
        f.Clock.Advance(6.8);
        Assert.AreEqual(1, f.Screen.Hides);
    }

    [TestMethod]
    public void OutdoorHasIndependentLeadAndTurningOffMinorRemindersStopsIt()
    {
        using var f = new Fixture(new Clock(7, 49, 59));
        f.Settings.Notification.OutdoorClassOnPreparingMaskText = "去操场";
        f.Lesson("体育与健康", "体", "08:00", "08:45", outdoor: true);
        f.Service.Start();
        f.Clock.Advance(1);
        Assert.AreEqual("去操场", f.Screen.Masks.Single());
        f.Settings.AllowMinorNotification = false;
        f.Service.RefreshSchedule();
        f.Clock.Advance(100);
        Assert.AreEqual(1, f.Screen.Hides);
        Assert.IsEmpty(f.Screen.Overlays);
        f.Settings.AllowMinorNotification = true;
        f.Service.RefreshSchedule();
        Assert.HasCount(2, f.Screen.Masks);
    }

    [TestMethod]
    public void ResumeDoesNotPlayAnExpiredClassOrBreakTransition()
    {
        using var f = new Fixture(new Clock(7, 30));
        f.Lesson("体育", "体", "08:00", "08:45");
        f.Service.Start();
        f.Clock.Sleep(45 * 60);
        f.Service.RefreshSchedule();
        Assert.IsEmpty(f.Screen.Masks);
        f.Clock.Sleep(60 * 60);
        f.Service.RefreshSchedule();
        Assert.IsEmpty(f.Screen.Masks);
    }

    [TestMethod]
    public void BoundedQueueRejectsExcessAndAnUnavailableChannelCannotPreempt()
    {
        var hidden = new Surface { IsAvailable = false };
        using var f = new Fixture(secondary: hidden);
        f.Service.Start();
        Assert.IsTrue(f.Service.Publish(f.Request("held", duration: 10, priority: NotificationPriority.Important)));
        for (var i = 0; i < 64; i++) Assert.IsTrue(f.Service.Publish(f.Request($"waiting{i}", priority: NotificationPriority.Low)));
        Assert.IsFalse(f.Service.Publish(f.Request("too-many", priority: NotificationPriority.Low)));
        Assert.IsTrue(f.Service.Publish(f.Request("next", priority: NotificationPriority.Normal)));
        Assert.IsFalse(f.Service.Cancel("waiting63"));
        f.Clock.Advance(10);
        Assert.AreEqual("next", f.Screen.Masks.Last());
        Assert.IsFalse(f.Service.Publish(f.Request("hidden", priority: NotificationPriority.Important, channel: "secondary")));
        Assert.AreEqual(1, f.Screen.Hides);
    }
}
