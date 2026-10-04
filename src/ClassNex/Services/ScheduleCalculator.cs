using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>一节课的时间槽。</summary>
public sealed record ClassSlot(string Subject, TimeSpan Start, TimeSpan End);

/// <summary>根据课表与日期推算「今天/当前/下一节课」。</summary>
public static class ScheduleCalculator
{
    public static readonly string[] ChineseDays = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

    /// <summary>CSES 的 enable_day：1=周一 ... 7=周日。</summary>
    public static int DayOfWeekNumber(DateTime date) => ((int)date.DayOfWeek + 6) % 7 + 1;

    /// <summary>按 CSES 的 weeks 字段过滤：parity 为 "odd"/"even"/"all"。</summary>
    public static bool MatchesParity(string weeks, string parity) => parity switch
    {
        "odd" => weeks == "all" || weeks == "odd",
        "even" => weeks == "all" || weeks == "even",
        _ => true,
    };

    /// <summary>根据「单周开始日期」推算当前是单周还是双周。</summary>
    public static string CurrentParity(AppSettings settings, DateTime now)
    {
        var start = settings.SingleWeekStartTime.Date;
        var days = (now.Date - start).Days;
        if (days < 0)
            return "odd";

        var weeks = (days / 7) + settings.WeekRotationOffset;
        return weeks % 2 == 0 ? "odd" : "even";
    }

    /// <summary>取某一天（1-7）在当前周次下的所有课程，按开始时间排序。</summary>
    public static List<ClassSlot> GetDaySlots(CsesDocument doc, int dayOfWeek, string parity)
    {
        var result = new List<ClassSlot>();
        foreach (var sched in doc.Schedules)
        {
            if (sched.EnableDay != dayOfWeek)
                continue;
            if (!MatchesParity(sched.Weeks, parity))
                continue;

            foreach (var cls in sched.Classes)
            {
                if (TimeSpan.TryParse(cls.StartTime, out var st) && TimeSpan.TryParse(cls.EndTime, out var et))
                    result.Add(new ClassSlot(cls.Subject, st, et));
            }
        }

        return result.OrderBy(x => x.Start).ToList();
    }

    /// <summary>生成主界面要显示的「日期 + 课程信息」两段文本。</summary>
    public static (string DateText, string InfoText) Describe(AppSettings settings, CsesDocument doc, DateTime now)
    {
        var day = DayOfWeekNumber(now);
        var parity = CurrentParity(settings, now);
        var slots = GetDaySlots(doc, day, parity);
        var dateText = $"{ChineseDays[day - 1]} {now.Month}/{now.Day:00}";

        if (slots.Count == 0)
            return (dateText, "今天没有课程。");

        var nowTs = now.TimeOfDay;

        var current = slots.FirstOrDefault(s => s.Start <= nowTs && nowTs < s.End);
        if (current is not null)
            return (dateText, $"{current.Subject} 至 {Format(current.End)}");

        var next = slots.FirstOrDefault(s => s.Start > nowTs);
        if (next is not null)
        {
            var minutes = (int)Math.Ceiling((next.Start - nowTs).TotalMinutes);
            var tail = minutes > 0 ? $"，{minutes} 分钟后" : "";
            return (dateText, $"下节课 {next.Subject} {Format(next.Start)}{tail}");
        }

        return (dateText, "今日课程已全部结束。");
    }

    private static string Format(TimeSpan t) => t.ToString(@"hh\:mm");
}
