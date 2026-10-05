using System.Text.Json;
using Avalonia.Controls;
using ClassNex.Controls;
using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>
/// 开发自检（仅 DEBUG 使用）：按档案编辑器各按钮的真实调用顺序演练一遍，
/// 把每一步的结果/异常写进 _verify.log，用来定位「编辑时闪退」。
/// 演练前对课表文件与时间表做快照，结束后恢复 —— 绝不污染真实数据。
/// </summary>
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

        // ---- 快照 ----
        var yamlPath = AppServices.TimetablePath;
        var yamlSnapshot = File.Exists(yamlPath) ? File.ReadAllText(yamlPath) : null;
        var layoutJson = JsonSerializer.Serialize(AppServices.Settings.TimeLayout);

        try
        {
            var profile = AppServices.Schedule.Profile;

            Step("课表网格 Render(全部周次)", () =>
            {
                var grid = new Grid();
                TimetableGridBuilder.Render(grid, profile, "all");
            });
            Step("课表网格 Render(单周)", () =>
            {
                var grid = new Grid();
                TimetableGridBuilder.Render(grid, profile, "odd");
            });
            Step("课表网格 Render(双周)", () =>
            {
                var grid = new Grid();
                TimetableGridBuilder.Render(grid, profile, "even");
            });
            Step("课表网格 Render(选中某课程)", () =>
            {
                var grid = new Grid();
                var schedule = profile.Schedules.FirstOrDefault();
                if (schedule?.Classes.Count > 0)
                {
                    var courseRef = new CourseRef(schedule.EnableDay, schedule.Weeks, schedule, schedule.Classes[0]);
                    TimetableGridBuilder.Render(grid, profile, "all", courseRef);
                }
            });

            // 模拟「点空格 → 点科目 → 直接排课」+ 自动移动到下一个课程
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

            // 时间表编辑链路（改时间会同步课表）
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

            // 科目编辑链路
            Step("AddSubject + RemoveSubject", () =>
            {
                var subject = AppServices.Schedule.AddSubject("自检科目");
                AppServices.Schedule.RemoveSubject(subject);
            });

            // 课程更新链路（编辑器 UpdateCourse 的真实路径）
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

            // 时间服务（浮窗链路）
            Step("Time.GetTodaySummary", () =>
            {
                _ = AppServices.Time.GetTodaySummary(profile, DateTime.Now);
            });
        }
        finally
        {
            // ---- 恢复快照：自检绝不留痕 ----
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
