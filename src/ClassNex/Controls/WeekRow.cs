using System.ComponentModel;
using ClassNex.Models;
using ClassNex.Services;

namespace ClassNex.Controls;






public sealed class WeekRow : INotifyPropertyChanged
{
    private string _sunday = "";
    private string _monday = "";
    private string _tuesday = "";
    private string _wednesday = "";
    private string _thursday = "";
    private string _friday = "";
    private string _saturday = "";

    public string StartText { get; init; } = "";

    public string EndText { get; init; } = "";

    
    public string TimeText => $"{StartText}-{EndText}";

    public string Sunday { get => _sunday; set => Set(ref _sunday, value); }

    public string Monday { get => _monday; set => Set(ref _monday, value); }

    public string Tuesday { get => _tuesday; set => Set(ref _tuesday, value); }

    public string Wednesday { get => _wednesday; set => Set(ref _wednesday, value); }

    public string Thursday { get => _thursday; set => Set(ref _thursday, value); }

    public string Friday { get => _friday; set => Set(ref _friday, value); }

    public string Saturday { get => _saturday; set => Set(ref _saturday, value); }

    
    public string this[int column]
    {
        get => column switch
        {
            0 => Sunday,
            1 => Monday,
            2 => Tuesday,
            3 => Wednesday,
            4 => Thursday,
            5 => Friday,
            6 => Saturday,
            _ => "",
        };
        set
        {
            switch (column)
            {
                case 0: Sunday = value; break;
                case 1: Monday = value; break;
                case 2: Tuesday = value; break;
                case 3: Wednesday = value; break;
                case 4: Thursday = value; break;
                case 5: Friday = value; break;
                case 6: Saturday = value; break;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (field == value)
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}


public static class WeekRowsBuilder
{
    
    public static readonly int[] ColumnDays = { 7, 1, 2, 3, 4, 5, 6 };

    public static List<WeekRow> Build(ScheduleProfile profile, string parity)
    {
        
        var points = new SortedDictionary<TimeSpan, TimeSpan>();
        foreach (var time in AppServices.TimeLayout.Layout.Times)
        {
            if (time.Kind == ClassTimeKind.Break)
                continue;
            if (TimeSpan.TryParse(time.Start, out var s) && TimeSpan.TryParse(time.End, out var e) && e > s)
                points.TryAdd(s, e);
        }

        
        foreach (var schedule in profile.Schedules)
        {
            if (!ScheduleProfile.MatchesParity(schedule.Weeks, parity))
                continue;
            foreach (var course in schedule.Classes)
            {
                if (TimeSpan.TryParse(course.StartTime, out var s)
                    && TimeSpan.TryParse(course.EndTime, out var e) && e > s)
                    points.TryAdd(s, e);
            }
        }

        var rows = new List<WeekRow>();

        foreach (var (start, end) in points)
        {
            var row = new WeekRow
            {
                StartText = start.ToString(@"hh\:mm"),
                EndText = end.ToString(@"hh\:mm"),
            };

            for (var c = 0; c < 7; c++)
            {
                var day = ColumnDays[c];
                row[c] = FindText(profile, day, parity, start);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static string FindText(ScheduleProfile profile, int day, string parity, TimeSpan start)
    {
        foreach (var schedule in profile.Schedules)
        {
            if (schedule.EnableDay != day)
                continue;
            if (!ScheduleProfile.MatchesParity(schedule.Weeks, parity))
                continue;

            foreach (var course in schedule.Classes)
            {
                if (TimeSpan.TryParse(course.StartTime, out var s) && s == start)
                {
                    
                    var subject = profile.FindSubject(course.Subject);
                    return subject?.Name ?? course.Subject;
                }
            }
        }

        return "";
    }
}
