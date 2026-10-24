using System.Text.Json;
using Avalonia.Controls;
using ClassNex.Controls;
using ClassNex.Models;

namespace ClassNex.Services;






public static class EditorSelfTest
{
    public static void Run()
    {
        var log = System.IO.Path.Combine(AppContext.BaseDirectory, "_verify.log");
        void Step(string name, Action action)
        {
            try
            {
                action();
                System.IO.File.AppendAllText(log, $"SELFTEST [OK]   {name}\n");
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText(log, $"SELFTEST [FAIL] {name}\n{ex}\n\n");
            }
        }

        
        var yamlPath = AppServices.TimetablePath;
        var yamlSnapshot = File.Exists(yamlPath) ? File.ReadAllText(yamlPath) : null;
        var layoutJson = JsonSerializer.Serialize(AppServices.Settings.TimeLayout);

        try
        {
            var profile = AppServices.Schedule.Profile;

            Step("周课表行构建(全部周次)", () =>
            {
                var rows = WeekRowsBuilder.Build(profile, "all");
                if (rows.Count == 0) throw new Exception("周课表没有行");
            });
            Step("周课表行构建(单周)", () =>
            {
                _ = WeekRowsBuilder.Build(profile, "odd");
            });
            Step("周课表行构建(双周)", () =>
            {
                _ = WeekRowsBuilder.Build(profile, "even");
            });
            Step("周课表行构建(带课程)", () =>
            {
                var rows = WeekRowsBuilder.Build(profile, "all");
                
                if (!rows.Any(r => Enumerable.Range(0, 7).Any(c => !string.IsNullOrEmpty(r[c]))))
                    throw new Exception("周课表行里没有任何科目文本");
            });

            
            Step("AddCourse(新课程)", () =>
            {
                var schedule = profile.Schedules.First(s => s.EnableDay == 1);
                AppServices.Schedule.AddCourse(1, "all", new Course
                {
                    Subject = "自习",
                    StartTime = "08:45:00",
                    EndTime = "09:00:00",
                });
            });
            Step("排课后重绘网格", () =>
            {
                var grid = new Grid();
                TimetableGridBuilder.Render(grid, profile, "all");
            });
            Step("RemoveCourse(刚加的课程)", () =>
            {
                var schedule = profile.Schedules.First(s => s.EnableDay == 1);
                var added = schedule.Classes.First(c =>
                    ClassTime.SameTime(ClassTime.ToShortTime(c.StartTime), "08:45"));
                AppServices.Schedule.RemoveCourse(1, "all", added);
            });

            
            Step("TimeLayout.Add", () =>
            {
                AppServices.TimeLayout.Add(new ClassTime
                {
                    Name = "自检节",
                    Start = "23:00",
                    End = "23:45",
                    Kind = ClassTimeKind.Class,
                });
            });
            Step("TimeLayout.Update(改时间→同步课表)", () =>
            {
                var t = AppServices.TimeLayout.Layout.Times.FirstOrDefault(x => x.Name == "自检节");
                if (t is not null)
                {
                    AppServices.TimeLayout.Update(t, new ClassTime
                    {
                        Name = t.Name,
                        Start = "22:00",
                        End = "22:45",
                        Kind = t.Kind,
                    });
                }
            });
            Step("TimeLayout.Remove(自检节)", () =>
            {
                var t = AppServices.TimeLayout.Layout.Times.FirstOrDefault(x => x.Name == "自检节");
                if (t is not null)
                    AppServices.TimeLayout.Remove(t);
            });
            Step("TimeLayout.InsertNext(选中项后插课间)", () =>
            {
                var layout = AppServices.TimeLayout.Layout.Times;
                var anchor = layout.FirstOrDefault();
                if (anchor is null) throw new Exception("时间表为空，无法自检插入");
                var before = layout.Count;
                var added = AppServices.TimeLayout.InsertNext(anchor, ClassTimeKind.Break, out var notice);
                if (added is null) throw new Exception("插入失败：" + notice);
                if (added.Kind != ClassTimeKind.Break) throw new Exception("插入的不是课间");
                if (layout.Count != before + 1) throw new Exception("插入后数量不对");
                var idx = layout.IndexOf(added);
                var anchorIdx = layout.IndexOf(anchor);
                var afterAnchor = idx == anchorIdx + 1;
                var frontInserted = idx == 0 && anchorIdx == 1 && notice.Contains("向前");
                if (!afterAnchor && !frontInserted)
                    throw new Exception($"新时间点位置不对（idx={idx}, anchor={anchorIdx}）");
            });
            Step("TimeLayout.Duplicate(创建副本)", () =>
            {
                var layout = AppServices.TimeLayout.Layout.Times;
                var anchor = layout.FirstOrDefault();
                if (anchor is null) throw new Exception("时间表为空，无法自检副本");
                var before = layout.Count;
                var copy = AppServices.TimeLayout.Duplicate(anchor, out var notice);
                if (copy is null) throw new Exception("复制失败：" + notice);
                if (layout.Count != before + 1) throw new Exception("复制后数量不对");
                AppServices.TimeLayout.Remove(copy);
            });
            Step("TimeLayout.InsertNext(分割线+重复检查)", () =>
            {
                var layout = AppServices.TimeLayout.Layout.Times;
                var anchor = layout.FirstOrDefault();
                if (anchor is null) throw new Exception("时间表为空，无法自检分割线");
                var sep = AppServices.TimeLayout.InsertNext(anchor, ClassTimeKind.Separator, out var notice);
                if (sep is null) throw new Exception("添加分割线失败：" + notice);
                if (sep.Kind != ClassTimeKind.Separator) throw new Exception("添加的不是分割线");
                var dup = AppServices.TimeLayout.InsertNext(sep, ClassTimeKind.Separator, out var dupNotice);
                if (dup is not null) throw new Exception("同一位置重复分割线没有被拦截");
                if (!dupNotice.Contains("分割线")) throw new Exception("重复提示文案不对：" + dupNotice);
                AppServices.TimeLayout.Remove(sep);
            });
            Step("TimeLayout.InsertNext(行动+重复检查)", () =>
            {
                var layout = AppServices.TimeLayout.Layout.Times;
                var anchor = layout.FirstOrDefault();
                if (anchor is null) throw new Exception("时间表为空，无法自检行动");
                var act = AppServices.TimeLayout.InsertNext(anchor, ClassTimeKind.Action, out var notice);
                if (act is null) throw new Exception("添加行动失败：" + notice);
                if (!ClassTime.SameTime(act.Start, act.End)) throw new Exception("行动必须是零时长");
                var dup = AppServices.TimeLayout.InsertNext(act, ClassTimeKind.Action, out var dupNotice);
                if (dup is not null) throw new Exception("同一位置重复行动没有被拦截");
                AppServices.TimeLayout.Remove(act);
            });

            
            Step("AddSubject + RemoveSubject", () =>
            {
                var subject = AppServices.Schedule.AddSubject("自检科目");
                AppServices.Schedule.RemoveSubject(subject);
            });

            
            Step("UpdateCourse 链路(删+增)", () =>
            {
                var schedule = profile.Schedules.FirstOrDefault();
                if (schedule?.Classes.Count > 0)
                {
                    var course = schedule.Classes[0];
                    var updated = new Course
                    {
                        Subject = course.Subject,
                        StartTime = course.StartTime,
                        EndTime = course.EndTime,
                    };
                    AppServices.Schedule.RemoveCourse(schedule.EnableDay, schedule.Weeks, course);
                    AppServices.Schedule.AddCourse(schedule.EnableDay, schedule.Weeks, updated);
                }
            });

            
            Step("Time.GetTodaySummary", () =>
            {
                _ = AppServices.Time.GetTodaySummary(profile, DateTime.Now);
            });
        }
        finally
        {
            
            try
            {
                if (yamlSnapshot is not null)
                    File.WriteAllText(yamlPath, yamlSnapshot);

                var restored = JsonSerializer.Deserialize<TimeLayout>(layoutJson);
                if (restored is not null)
                    AppServices.Settings.TimeLayout = restored;

                AppServices.Schedule.Reload();
                AppServices.TimeLayout.Save();
                System.IO.File.AppendAllText(log, "SELFTEST [RESTORED] 数据已恢复\n");
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText(log, $"SELFTEST [RESTORE-FAIL] {ex}\n");
            }
        }

        System.IO.File.AppendAllText(log, "SELFTEST [DONE]\n\n");
    }
}
