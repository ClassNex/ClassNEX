using System.Globalization;
using System.Text.Json;
using ClassNex.Models;

namespace ClassNex.Services;

public static partial class ClassIslandImporter
{
    private sealed record DayPlan(Guid Layout, string Name, JsonElement[] Classes, int Day, int Parity, int Priority);

    private static void ReadProfile(JsonElement root, string name, bool active, ClassIslandImportPlan plan)
    {
        if (root.Field("Subjects").ValueKind != JsonValueKind.Object && root.Field("ClassPlans").ValueKind != JsonValueKind.Object && root.Field("TimeLayouts").ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"无法识别档案 {name} 的结构。");
        var profile = new ScheduleProfile { Name = name };
        var subjects = new Dictionary<Guid, Subject>();
        var names = new Dictionary<string, Subject>(StringComparer.Ordinal);
        foreach (var (id, subject) in root.Map("Subjects"))
        {
            if (id == Guid.Empty) continue;
            var label = subject.Str("Name");
            if (string.IsNullOrWhiteSpace(label)) { plan.Warn("未命名的科目不迁移。"); continue; }
            var entry = new Subject { Name = label, SimplifiedName = subject.Str("Initial"), Teacher = subject.Str("TeacherName"), IsOutDoor = subject.Flag("IsOutDoor") };
            if (names.TryGetValue(label, out var same))
            {
                if (same.Teacher == entry.Teacher && same.IsOutDoor == entry.IsOutDoor && same.SimplifiedName == entry.SimplifiedName) { subjects.Add(id, same); continue; }
                for (int index = 2; names.ContainsKey(entry.Name); index++) entry.Name = $"{label}（{index}）";
                plan.Warn("同名但教师或属性不同的科目已添加序号区分。");
            }
            names.Add(entry.Name, entry); subjects.Add(id, entry); profile.Subjects.Add(entry);
        }
        var layouts = new Dictionary<Guid, (TimeLayout Time, List<(ClassTime Time, Guid Subject, string Start, string End)> Lessons)>();
        foreach (var (id, origin) in root.Map("TimeLayouts"))
        {
            var time = new TimeLayout { Name = origin.Str("Name") ?? "时间表" };
            var lessons = new List<(ClassTime Time, Guid Subject, string Start, string End)>();
            var items = origin.Items("Layouts");
            if (origin.Field("Layouts").ValueKind != JsonValueKind.Array || items.Length > 2048) throw new InvalidDataException($"档案 {name} 的时间表无效。");
            foreach (var item in items)
            {
                var kind = item.Int("TimeType");
                if (kind is < 0 or > 3) { plan.Warn("有未识别的时间点类型，已跳过。"); continue; }
                var start = ReadTime(item, "Start", name);
                var end = kind is 2 or 3 ? start : ReadTime(item, "End", name);
                if (end < start) throw new InvalidDataException($"档案 {name} 的时段结束早于开始。");
                var label = kind switch { 0 => $"第{lessons.Count + 1}节", 1 => string.IsNullOrWhiteSpace(item.Str("BreakName")) ? "课间休息" : item.Str("BreakName")!, 2 => "分割线", _ => item.Str("ActionName") ?? "行动" };
                var entry = new ClassTime { Kind = (ClassTimeKind)kind, Name = label, Start = start.ToString(@"hh\:mm", CultureInfo.InvariantCulture), End = end.ToString(@"hh\:mm", CultureInfo.InvariantCulture) };
                time.Times.Add(entry);
                if (kind == 0) lessons.Add((entry, item.Id("DefaultClassId"), start.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture), end.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)));
            }
            layouts.Add(id, (time, lessons));
        }
        Guid defaultGroup = new("acaf4ef0-e261-4262-b941-34ea93cb4369");
        var group = root.Id("SelectedClassPlanGroupId", defaultGroup);
        var plans = new List<DayPlan>();
        if (root.Flag("IsTempClassPlanGroupEnabled") || root.Id("TempClassPlanId") != Guid.Empty) plan.Warn("临时调课与临时课表群不替换常规课表。");
        if (root.Field("OrderedSchedules") is { ValueKind: JsonValueKind.Object } orders)
        {
            var count = 0;
            var dates = new List<string>(5);
            foreach (var entry in orders.EnumerateObject())
            {
                count++;
                if (dates.Count < 5 && DateTime.TryParse(entry.Name, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
                    dates.Add(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
            if (count > 0)
            {
                var sample = dates.Count == 0 ? "" : $"（{string.Join("、", dates)}{(count > dates.Count ? " 等" : "")}）";
                plan.Warn($"档案 {name} 有 {count} 项按日期预排的临时课表{sample}，不会写成每周重复的课程；请保留原档案以备查阅。");
            }
        }
        foreach (var (_, cp) in root.Map("ClassPlans"))
        {
            if (!cp.Flag("IsEnabled", true)) continue;
            if (cp.Flag("IsOverlay")) { plan.Warn("叠加课表没有对应模式，不会替换常规课表。"); continue; }
            var belongs = cp.Id("AssociatedGroup", defaultGroup);
            if (belongs != group && belongs != Guid.Empty) { plan.Warn("只启用原先选中的常规课表群和全局课表，不合并互斥课表群。"); continue; }
            var rule = cp.Field("TimeRule");
            var d = rule.Int("WeekDay"); var parity = rule.Int("WeekCountDiv");
            if (d is < 0 or > 6 || parity is < 0 or > 2 || parity != 0 && rule.Int("WeekCountDivTotal", 2) != 2)
            { plan.Warn("ClassNEX 不支持此档案的多周轮换规则，对应课表已跳过。"); continue; }
            var layout = cp.Id("TimeLayoutId");
            if (!layouts.ContainsKey(layout)) throw new InvalidDataException($"档案 {name} 的课表引用了不存在的时间表。");
            var classes = cp.Items("Classes");
            if (classes.Length > 2048 || cp.Field("Classes").ValueKind != JsonValueKind.Array) throw new InvalidDataException($"档案 {name} 的课时列表无效。");
            plans.Add(new(layout, cp.Str("Name") ?? name, classes, d == 0 ? 7 : d, parity, belongs == group ? 2 : 1));
        }
        var used = new Dictionary<Guid, int>();
        for (var day = 1; day <= 7; day++)
        {
            var candidates = plans.Where(p => p.Day == day).OrderByDescending(p => p.Priority).ToArray();
            var odd = candidates.FirstOrDefault(p => p.Parity is 0 or 1); var even = candidates.FirstOrDefault(p => p.Parity is 0 or 2);
            if (odd != null && ReferenceEquals(odd, even)) Add(odd, "all");
            else { if (odd != null) Add(odd, "odd"); if (even != null) Add(even, "even"); }
        }
        void Add(DayPlan p, string weeks)
        {
            var lessons = layouts[p.Layout].Lessons;
            used[p.Layout] = used.GetValueOrDefault(p.Layout) + (weeks == "all" ? 2 : 1);
            var schedule = new Schedule { Name = p.Name, EnableDay = p.Day, Weeks = weeks };
            for (var i = 0; i < lessons.Count; i++)
            {
                var entry = i < p.Classes.Length ? p.Classes[i] : default;
                if (!entry.Flag("IsEnabled", true)) continue;
                var id = entry.Id("SubjectId"); if (id == Guid.Empty) id = lessons[i].Subject;
                if (id == Guid.Empty) continue;
                if (!subjects.TryGetValue(id, out var subject)) { plan.Warn("课表有缺失的科目引用，已保留时段并跳过该节课。"); continue; }
                schedule.Classes.Add(new Course { Subject = subject.Name, StartTime = lessons[i].Start, EndTime = lessons[i].End });
            }
            profile.Schedules.Add(schedule);
        }
        var defaultLayout = used.Count != 0 ? layouts[used.MaxBy(e => e.Value).Key].Time : layouts.Count != 0 ? layouts.First().Value.Time : null;
        if (used.Count > 1) plan.Warn("多个作息模板的课程均保留各自起止时间；编辑器使用最常用的作息模板。");
        profile.ImportedTimeLayout = defaultLayout;
        plan.Profiles.Add((name, profile, defaultLayout, active));
    }

    private static TimeSpan ReadTime(JsonElement item, string prefix, string name)
    {
        var text = item.Str(prefix + "Time");
        if (text == null && item.Str(prefix + "Second") is { } old && DateTime.TryParse(old, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)) return date.TimeOfDay;
        if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var time) && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1)) return time;
        throw new InvalidDataException($"档案 {name} 包含无效的本地时刻。");
    }
}
