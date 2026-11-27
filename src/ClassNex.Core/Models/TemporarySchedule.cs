using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;

namespace ClassNex.Models;

/// <summary>本地临时课表中的一节课，起止均为该日的本地时刻。</summary>
public sealed record TemporaryLesson(string Subject, TimeSpan Start, TimeSpan End);

/// <summary>在指定教学日替代常规课表的独立副本；空课表表示该日停课。</summary>
public sealed record TemporarySchedule(DateOnly Date, string Name, ImmutableArray<TemporaryLesson> Lessons)
{
    /// <summary>本日独立的非课程作息；null 沿用基础作息，空数组则明确不安排课间或活动。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<TemporaryTimeItem>? TimeLayout { get; init; }
}

/// <summary>一个课间、活动或分隔点。课程时段由当天课程唯一决定；定点项目不占用时长。</summary>
public sealed record TemporaryTimeItem(ClassTimeKind Kind, string Name, TimeSpan Start, TimeSpan End)
{
    /// <summary>读取基础模板的时钟，支持秒以及表示日末的 24:00。</summary>
    public static bool TryClock(string? value, out TimeSpan clock)
    {
        var text = value?.Trim();
        if (text is "24:00" or "24:00:00") { clock = TimeSpan.FromDays(1); return true; }
        var valid = TimeOnly.TryParseExact(text, ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss", "H:mm:ss.FFFFFFF", "HH:mm:ss.FFFFFFF"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var time);
        clock = time.ToTimeSpan();
        return valid;
    }

    /// <summary>读取基础作息为只读时段，不修改设置或来源档案。</summary>
    public static IEnumerable<TemporaryTimeItem> FromBase(TimeLayout layout)
    {
        foreach (var item in layout.Times)
            if (TryClock(item.Start, out var start) && TryClock(item.End, out var end) && start <= end)
                yield return new(item.Kind, item.Name, start, end);
    }
}
