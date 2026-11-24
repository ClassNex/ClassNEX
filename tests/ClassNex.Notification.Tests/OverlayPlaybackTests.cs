using ClassNex.Core.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassNex.Notification.Tests;

public sealed partial class NotificationTests
{
    private sealed class PlaybackSurface : Surface, INotificationSurface
    {
        public List<INotificationOverlayProgress> Sessions { get; } = [];

        public void ShowOverlay(string left, string subject, string teacher, string time, string prefix, string message,
            bool verticalPageTurn, TimeSpan trackDuration, INotificationOverlayProgress progress)
        {
            base.ShowOverlay(left, subject, teacher, time, prefix, message, verticalPageTurn, trackDuration);
            Sessions.Add(progress);
        }
    }

    [TestMethod]
    public void OnlyDetailsStartPlaybackAndTheRelativeClockSpansBothPages()
    {
        var screen = new PlaybackSurface();
        using var f = new Fixture(primary: screen);
        f.Settings.Notification.ClassOffOverlayText = "课间提醒";
        f.Service.Start();
        f.Service.DebugTriggerClassOn();
        f.Clock.Advance(5);
        Assert.IsEmpty(screen.Sessions);
        f.Service.DebugTriggerClassOff();
        f.Clock.Advance(1.95);
        Assert.IsEmpty(screen.Sessions);
        f.Clock.Advance(.05);
        var playback = screen.Sessions.Single();
        Assert.AreEqual(1d, playback.LeftProgress);
        var ticks = f.Clock.Ticks;
        f.Clock.Advance(.05);
        Assert.AreEqual(1 - .05 / 20, playback.LeftProgress, 1e-9);
        f.Clock.Advance(9.95);
        Assert.AreEqual(.5, playback.LeftProgress, 1e-9);
        Assert.AreEqual(ticks, f.Clock.Ticks);
        f.Clock.ShiftWall(4);
        Assert.AreEqual(.5, playback.LeftProgress, 1e-9);
        f.Clock.ShiftWall(-4);
        f.Clock.Advance(10);
        Assert.HasCount(1, screen.Sessions);
        Assert.AreEqual(0d, playback.LeftProgress);
        f.Clock.Advance(600);
        Assert.AreEqual(0d, playback.LeftProgress);
    }

    [TestMethod]
    public void PreparingSamplesWallTimeFromTheBeginningOfTheDetail()
    {
        var screen = new PlaybackSurface();
        using var f = new Fixture(new Clock(7, 56, 59), primary: screen);
        f.Lesson("数学", "数", "08:00", "08:45");
        f.Service.Start();
        f.Clock.Advance(1);
        Assert.IsEmpty(screen.Sessions);
        f.Clock.Advance(3);
        var playback = screen.Sessions.Single();
        Assert.AreEqual(1d, playback.LeftProgress);
        f.Clock.ShiftWall(17.7);
        Assert.AreEqual(.9, playback.LeftProgress, 1e-9);
        f.Clock.ShiftWall(-17.7);
        f.Clock.Advance(88.5);
        Assert.AreEqual(.5, playback.LeftProgress, 1e-9);
        f.Service.Stop();
        f.Clock.ShiftWall(120);
        Assert.AreEqual(.5, playback.LeftProgress, 1e-9);
    }

    [TestMethod]
    public void PreviewUsesItsOwnDeadlineAndReplacementCannotResumeIt()
    {
        var screen = new PlaybackSurface();
        using var f = new Fixture(new Clock(7, 20), primary: screen);
        f.Lesson("科学", "科", "08:00", "08:45");
        f.Service.Start();
        f.Service.DebugTriggerClassPreparing();
        f.Clock.Advance(3);
        Assert.AreEqual("距上课还剩 2397 秒", screen.Overlays.Single().Caption);
        var preview = screen.Sessions.Single();
        f.Clock.ShiftWall(10);
        Assert.AreEqual(.5, preview.LeftProgress, 1e-9);
        f.Service.DebugTriggerTestNotification();
        f.Clock.Advance(1.2);
        Assert.HasCount(2, screen.Sessions);
        var replacement = screen.Sessions.Last();
        Assert.AreEqual(1d, replacement.LeftProgress);
        f.Clock.Advance(3.4);
        Assert.AreEqual(.5, replacement.LeftProgress, 1e-9);
        Assert.AreEqual(.5, preview.LeftProgress, 1e-9);
        f.Clock.Advance(3.4);
        Assert.AreEqual(0d, replacement.LeftProgress);
        Assert.AreEqual(.5, preview.LeftProgress, 1e-9);
    }

    [TestMethod]
    public void PreparingSecondsChangeAfterTheHalfSecondBoundaryIndependentlyOfRailProgress()
    {
        var screen = new PlaybackSurface();
        using var f = new Fixture(new Clock(7, 59, 44), primary: screen);
        f.Lesson("科学", "科", "08:00", "08:45");
        f.Settings.Notification.IsClassOnPreparingNotificationEnabled = false;
        f.Settings.Notification.IsClassOnNotificationEnabled = false;
        f.Clock.Advance(.25);
        f.Service.Start();
        f.Service.DebugTriggerClassPreparing();
        f.Clock.Advance(3);
        Assert.AreEqual("距上课还剩 13 秒", screen.Overlays.Single().Caption);
        var body = screen.Sessions.Single();
        f.Clock.Advance(.25);
        Assert.IsEmpty(screen.Countdowns);
        f.Clock.Advance(.001);
        Assert.AreEqual("距上课还剩 12 秒", screen.Countdowns.Single());
        f.Clock.Advance(12);
        Assert.AreEqual("距上课还剩 0 秒", screen.Countdowns.Last());
        Assert.IsGreaterThan(0d, body.LeftProgress);
        Assert.AreEqual(0, screen.Hides);
        f.Clock.Advance(.499);
        Assert.AreEqual(1, screen.Hides);
    }

    [TestMethod]
    public void APluginsCountdownLabelDoesNotChangeItsPlaybackClock()
    {
        var screen = new PlaybackSurface();
        using var f = new Fixture(primary: screen);
        f.Service.Start();
        var request = f.Request("plugin", duration: 12) with
        {
            MaskDuration = TimeSpan.FromSeconds(2), Overlay = new("", "", "", "", CountdownTo: f.Clock.GetUtcNow().AddHours(1))
        };
        Assert.IsTrue(f.Service.Publish(request));
        f.Clock.Advance(2);
        var playback = screen.Sessions.Single();
        f.Clock.ShiftWall(3);
        Assert.AreEqual(1d, playback.LeftProgress);
        f.Clock.Advance(2);
        Assert.AreEqual(.8, playback.LeftProgress, 1e-9);
        Assert.IsTrue(f.Service.Cancel(request.Id));
        f.Clock.Advance(5);
        Assert.AreEqual(.8, playback.LeftProgress, 1e-9);
    }
}
