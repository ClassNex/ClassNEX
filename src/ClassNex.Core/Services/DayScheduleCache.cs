namespace ClassNex.Services;

public sealed class DayScheduleCache : IDisposable
{
    private readonly ITimeService _time;
    private readonly IScheduleService _schedule;
    private readonly object _gate = new();
    private TodaySummary? _cached;
    private DateTime _day;

    public DayScheduleCache(ITimeService time, IScheduleService schedule)
    {
        _time = time;
        _schedule = schedule;
        schedule.ProfileChanged += Invalidate;
    }

    public TodaySummary Read(DateTime now)
    {
        lock (_gate)
        {
            if (_cached is null || _day != now.Date || _cached.Parity != _time.GetCurrentParity(now))
            {
                _day = now.Date;
                return _cached = _time.GetTodaySummary(_schedule.Profile, now);
            }
            var current = _time.GetCurrentCourse(_cached.Slots, now.TimeOfDay);
            var next = _time.GetNextCourse(_cached.Slots, now.TimeOfDay);
            if (_cached.Current != current || _cached.Next != next) _cached = _cached with { Current = current, Next = next };
            return _cached;
        }
    }

    private void Invalidate() { lock (_gate) _cached = null; }
    public void Dispose() => _schedule.ProfileChanged -= Invalidate;
}
