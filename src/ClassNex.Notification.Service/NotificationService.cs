using ClassNex.Core.Abstractions;
using ClassNex.Models;

namespace ClassNex.Services;

public sealed class NotificationService : INotificationService, INotificationPublisher, IDisposable
{
    private readonly Dictionary<string, INotificationSurface> _channels;
    private readonly Func<AppSettings> _settings;
    private readonly ITimeService _time;
    private readonly IScheduleService _schedule;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly NotificationQueue _queue = new();
    private readonly Dictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);
    private readonly ClassroomReminders _classroom = new();
    private ITimer? _timer;
    private bool _running, _disposed, _hasOverlay, _previewHasOverlayDeadline;
    private NotificationRequest? _active, _preview;
    private NotificationOverlayPlayback? _overlayPlayback;
    private DateTimeOffset _overlayAt, _end, _nextScheduleCheck;
    private int _lastSeconds = -1;

    public NotificationService(INotificationSurface surface, Func<AppSettings> settings, ITimeService time, IScheduleService schedule,
        TimeProvider? clock = null) : this(new Dictionary<string, INotificationSurface> { ["classroom"] = surface }, settings, time, schedule, clock) { }

    public NotificationService(IReadOnlyDictionary<string, INotificationSurface> channels, Func<AppSettings> settings, ITimeService time,
        IScheduleService schedule, TimeProvider? clock = null)
    {
        _channels = new(channels, StringComparer.Ordinal);
        _settings = settings;
        _time = time;
        _schedule = schedule;
        _clock = clock ?? TimeProvider.System;
    }

    public event Action<string, Exception>? Failed;

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_running) return;
            _running = true;
            _classroom.Reset();
            _nextScheduleCheck = DateTimeOffset.MinValue;
            _schedule.ProfileChanged += OnScheduleChanged;
            _timer ??= _clock.CreateTimer(_ => Tick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            Tick();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running) return;
            _running = false;
            _schedule.ProfileChanged -= OnScheduleChanged;
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _queue.Clear();
            _seen.Clear();
            Finish();
        }
    }

    public void RefreshSchedule()
    {
        lock (_gate)
        {
            if (!_running) return;
            _nextScheduleCheck = DateTimeOffset.MinValue;
            Tick();
        }
    }

    private void OnScheduleChanged()
    {
        lock (_gate)
        {
            _classroom.Reset();
            _queue.RemovePrefix(ClassroomReminders.Prefix);
            ResetPreparing();
            RefreshSchedule();
        }
    }

    public bool Publish(NotificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (!_running || !_settings().AllowNotification || request.Id?.StartsWith(ClassroomReminders.Prefix, StringComparison.Ordinal) == true) return false;
            var now = _clock.GetLocalNow();
            if (!Enqueue(request, now)) return false;
            Dispatch(now);
            ArmTimer(now);
            return true;
        }
    }

    private bool Enqueue(NotificationRequest request, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Channel) || !_channels.TryGetValue(request.Channel, out var surface) ||
            _active?.Id == request.Id || (_seen.TryGetValue(request.Id, out var until) && until > now)) return false;
        try { if (!surface.IsAvailable) return false; }
        catch (Exception ex) { Report(request.Channel, ex); return false; }
        if (!_queue.Add(request, now)) return false;
        if (_seen.Count >= 256)
        {
            foreach (var key in _seen.Where(item => item.Value <= now).Select(item => item.Key).ToArray()) _seen.Remove(key);
            if (_seen.Count >= 256) _seen.Remove(_seen.MinBy(item => item.Value).Key);
        }
        _seen[request.Id] = request.ExpiresAt;
        if (_active is not null && (ReferenceEquals(_active, _preview) || _active.Priority < request.Priority)) Finish();
        return true;
    }

    public bool Cancel(string id)
    {
        lock (_gate)
        {
            if (_queue.Remove(id)) return true;
            if (_active is null || _active.Id != id) return false;
            Finish();
            var now = _clock.GetLocalNow();
            Dispatch(now);
            ArmTimer(now);
            return true;
        }
    }

    private void Tick()
    {
        lock (_gate)
        {
            if (!_running) return;
            var now = _clock.GetLocalNow();
            try
            {
                var settings = _settings();
                if (!settings.AllowNotification || !settings.AllowMinorNotification || !settings.Notification.IsClassOnPreparingNotificationEnabled)
                    ResetPreparing();
                if (now >= _nextScheduleCheck)
                {
                    var day = _time.GetTodaySummary(_schedule.Profile, now.DateTime);
                    var dismissal = day.Next is null && settings.Notification.IsAfterSchoolNotificationEnabled
                        ? ClassroomReminders.DismissalTime(day, settings.TimeLayout) : null;
                    _classroom.Evaluate(day, settings, now, dismissal, request => Enqueue(request, now));
                    _nextScheduleCheck = now.AddSeconds(30);
                    if (dismissal is { } end) CheckAt(now, end - now.TimeOfDay);
                    if (day.Current is { } current) CheckAt(now, current.End - now.TimeOfDay);
                    if (day.Next is { } next)
                    {
                        CheckAt(now, next.Start - now.TimeOfDay);
                        var lead = next.IsOutDoor ? settings.Notification.OutDoorClassPreparingDeltaTime : (long)settings.Notification.ClassOnBreakReminderMinutes * 60;
                        CheckAt(now, next.Start - now.TimeOfDay - TimeSpan.FromSeconds(Math.Clamp(lead, 0, 86400)));
                    }
                    CheckAt(now, TimeSpan.FromDays(1) - now.TimeOfDay);
                }
                if (!settings.AllowNotification) { _queue.Clear(); Finish(); }
                else Dispatch(now);
            }
            catch (Exception ex)
            {
                _nextScheduleCheck = now.AddSeconds(5);
                Report("schedule", ex);
                Finish();
            }
            if (_running) ArmTimer(now);
        }
    }

    private void CheckAt(DateTimeOffset now, TimeSpan remaining)
    {
        if (remaining > TimeSpan.Zero && now + remaining < _nextScheduleCheck) _nextScheduleCheck = now + remaining;
    }

    private void ResetPreparing()
    {
        _queue.RemovePrefix(ClassroomReminders.PreparingPrefix);
        foreach (var id in _seen.Keys.Where(id => id.StartsWith(ClassroomReminders.PreparingPrefix, StringComparison.Ordinal)).ToArray()) _seen.Remove(id);
        if (_active?.Id.StartsWith(ClassroomReminders.PreparingPrefix, StringComparison.Ordinal) == true) Finish();
    }

    private void Dispatch(DateTimeOffset now)
    {
        while (_running)
        {
            var currentChannel = _active?.Channel ?? "dispatch";
            try
            {
                if (_active != null && (now >= _end || !_channels[_active.Channel].IsAvailable)) Finish();
                if (_active == null)
                {
                    if (_queue.Take(now) is not { } next) return;
                    currentChannel = next.Channel;
                    var surface = _channels[next.Channel];
                    if (!surface.IsAvailable) continue;
                    _active = next;
                    _hasOverlay = false;
                    _lastSeconds = -1;
                    var duration = next.ExpiresAt - now < next.Duration ? next.ExpiresAt - now : next.Duration;
                    _end = now + duration;
                    _overlayAt = now + (next.MaskDuration < duration ? next.MaskDuration : duration);
                    surface.ShowMask(next.Mask, next.LeftGlyph, next.RightGlyph, _settings().AllowNotificationEffect, duration);
                }
                if (_active is not { Overlay: { } overlay } request || now < _overlayAt) return;
                var channel = _channels[request.Channel];
                var preparing = IsPreparingPresentation(request);
                var seconds = overlay.CountdownTo is { } target ? CountdownSeconds(target - now, preparing) : -1;
                if (!_hasOverlay)
                {
                    _hasOverlay = true;
                    var duration = _end - now;
                    _overlayPlayback = new(_clock, now, duration, preparing ? _end : null);
                    channel.ShowOverlay(overlay, seconds >= 0 ? seconds : null, duration, _overlayPlayback);
                }
                else if (seconds >= 0 && seconds != _lastSeconds) channel.UpdateCountdown(seconds);
                _lastSeconds = seconds;
                return;
            }
            catch (Exception ex)
            {
                Report(currentChannel, ex);
                Finish();
            }
        }
    }

    private bool IsPreparingPresentation(NotificationRequest request) =>
        request.Id.StartsWith(ClassroomReminders.PreparingPrefix, StringComparison.Ordinal) ||
        (ReferenceEquals(request, _preview) && _previewHasOverlayDeadline);

    private static int CountdownSeconds(TimeSpan remaining, bool preparing) =>
        (int)Math.Clamp(preparing ? Math.Round(remaining.TotalSeconds, MidpointRounding.AwayFromZero) :
            Math.Ceiling(remaining.TotalSeconds), 0, int.MaxValue);

    private void ArmTimer(DateTimeOffset now)
    {
        if (!_running) return;
        var due = _nextScheduleCheck;
        if (_active is not null)
        {
            if (_end < due) due = _end;
            if (!_hasOverlay && _active.Overlay != null && _overlayAt < due) due = _overlayAt;
            if (_hasOverlay && _lastSeconds > 0 && _active.Overlay?.CountdownTo is { } target)
            {
                var next = IsPreparingPresentation(_active)
                    ? target - TimeSpan.FromSeconds(_lastSeconds - 0.5) + TimeSpan.FromTicks(1)
                    : target - TimeSpan.FromSeconds(_lastSeconds - 1);
                if (next > now && next < due) due = next;
            }
        }
        var delay = due - _clock.GetLocalNow();
        _timer?.Change(delay < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : delay, Timeout.InfiniteTimeSpan);
    }

    private void Finish()
    {
        if (_active is not { } active) return;
        _overlayPlayback?.Stop();
        _overlayPlayback = null;
        _active = null;
        _preview = null;
        _previewHasOverlayDeadline = false;
        try { _channels[active.Channel].Hide(); } catch (Exception ex) { Report(active.Channel, ex); }
    }

    private void Report(string origin, Exception error)
    {
        System.Diagnostics.Trace.TraceError("Notification {0}: {1}", origin, error.Message);
        try { Failed?.Invoke(origin, error); } catch (Exception ex) { System.Diagnostics.Trace.TraceError("Notification observer: {0}", ex.Message); }
    }

    private static string PreviewId => "preview/" + Guid.NewGuid().ToString("N");
    private void Preview(Func<DateTimeOffset, NotificationRequest> create, bool hasOverlayDeadline = false)
    {
        lock (_gate)
        {
            if (!_running || !_settings().AllowNotification) return;
            Tick();
            if (_active is not null && !ReferenceEquals(_active, _preview)) return;
            var now = _clock.GetLocalNow();
            var request = create(now);
            if (!Enqueue(request, now)) return;
            _preview = request;
            _previewHasOverlayDeadline = hasOverlayDeadline;
            Dispatch(now);
            ArmTimer(now);
        }
    }

    public void DebugTriggerClassPreparing() => Preview(now =>
    {
        var day = _time.GetTodaySummary(_schedule.Profile, now.DateTime);
        if (day.Next is null && !day.IsEmpty)
        {
            var duration = TimeSpan.FromSeconds(3);
            return new(PreviewId, day.IsFinished ? "今天的课程已结束" : "当前是今天最后一节课", now + duration, duration, duration, NotificationPriority.Low);
        }
        var next = day.Next ?? ClassroomReminders.SampleCourse(now);
        var request = ClassroomReminders.Preparing(now, _settings().Notification, next, next.Start - now.TimeOfDay, PreviewId);
        var limit = TimeSpan.FromSeconds(23);
        return request.Duration <= limit ? request : request with { Duration = limit, ExpiresAt = now + limit };
    }, hasOverlayDeadline: true);

    public void DebugTriggerClassOn() => Preview(now => ClassroomReminders.ClassOn(now, _settings().Notification, PreviewId));

    public void DebugTriggerClassOff() => Preview(now =>
    {
        var day = _time.GetTodaySummary(_schedule.Profile, now.DateTime);
        var start = day.Current?.End ?? day.Slots.LastOrDefault(slot => slot.End <= now.TimeOfDay)?.End ?? now.TimeOfDay;
        return ClassroomReminders.ClassOff(now, _settings().Notification, day.IsEmpty ? ClassroomReminders.SampleCourse(now) : day.Next, start, PreviewId, _settings().TimeLayout);
    });

    public void DebugTriggerAfterSchool() => Preview(now => ClassroomReminders.AfterSchool(now, _settings().Notification, PreviewId));

    public void DebugTriggerTestNotification() => Preview(now => new(PreviewId, "测试提醒", now.AddSeconds(8), TimeSpan.FromMilliseconds(1200),
        TimeSpan.FromSeconds(8), Overlay: new("这是一条测试提醒", "通知系统工作正常", "", now.ToString("HH:mm"))));

    public void Dispose()
    {
        lock (_gate)
        {
            Stop();
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
