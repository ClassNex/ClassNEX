using ClassNex.Core.Abstractions;

namespace ClassNex.Services;

// ClassIsland NotificationWorkerService, 235914a2fac1: setup, LeftProgress and playback cleanup.
/// <summary>一条提醒正文的播放时钟，供展示端取样；不启动后台任务。</summary>
public sealed class NotificationOverlayPlayback : INotificationOverlayProgress
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _clock;
    private readonly NotificationPlayingSessionInfo _session = new();
    private readonly long _ticketTimestamp;
    private readonly TimeSpan _duration;
    private double _leftProgress = 1.0;

    /// <summary>在当前时刻开始按有效播放时长倒数，供仅接收时长的旧展示端使用。</summary>
    public NotificationOverlayPlayback(TimeSpan duration) : this(TimeProvider.System, TimeProvider.System.GetLocalNow(), duration, null) { }

    internal NotificationOverlayPlayback(TimeProvider clock, DateTimeOffset now, TimeSpan duration, DateTimeOffset? endTime)
    {
        _clock = clock;
        _duration = endTime.HasValue ? endTime.Value - now : duration;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_duration, TimeSpan.Zero);
        _session.SessionStartTime = _session.CurrentTicketStartTime = now.DateTime;
        _session.IsExplicitEndTime = endTime.HasValue;
        _ticketTimestamp = clock.GetTimestamp();
        _session.TimingStopwatch.Restart();
    }

    /// <inheritdoc/>
    public double LeftProgress
    {
        get
        {
            lock (_gate)
            {
                if (!_session.IsCompleted) _leftProgress = Sample(TicketElapsed);
                return _leftProgress;
            }
        }
    }

    private TimeSpan TicketElapsed => ReferenceEquals(_clock, TimeProvider.System)
        ? _session.TimingStopwatch.Elapsed : _clock.GetElapsedTime(_ticketTimestamp);

    private double Sample(TimeSpan elapsed) => _session.IsExplicitEndTime
        ? 1 - (_clock.GetLocalNow().DateTime - _session.SessionStartTime) / _duration
        : 1 - (_session.SessionPlayedTime + elapsed) / _duration;

    internal void Stop()
    {
        lock (_gate)
        {
            if (_session.IsCompleted) return;
            var playedTime = TicketElapsed;
            _session.TimingStopwatch.Reset();
            _session.SessionPlayedTime += playedTime;
            _leftProgress = Sample(TimeSpan.Zero);
            _session.IsCompleted = true;
        }
    }
}
