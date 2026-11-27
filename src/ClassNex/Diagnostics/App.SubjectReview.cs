#if DEBUG
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ClassNex.Models;
using ClassNex.Services;

namespace ClassNex;

public partial class App
{
    private async Task ReviewSubjectRenameAsync(Action<bool, string> check)
    {
        var path = AppServices.Schedule.FilePath;
        var work = Path.GetDirectoryName(path)!;
        if (!File.Exists(Path.Combine(work, "..", ".classnex-review")))
            throw new InvalidOperationException("Subject review requires the isolated classroom fixture");
        var pathToExample = Path.Combine(work, "SubjectReview.yaml");
        var schedule = AppServices.LocalSchedule;
        var day = DateOnly.FromDateTime(AppServices.ClassroomNow);
        try
        {
            schedule.Load(pathToExample);
            schedule.AddSubject("体育");
            OpenProfileEditor(2);
            var editor = _profileEditor!;
            await Dispatcher.UIThread.InvokeAsync(editor.UpdateLayout, DispatcherPriority.Background);
            T Element<T>(string name) where T : Control => editor.FindControl<T>(name)!;
            void Click(string name) => Element<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Click("AddSubjectButton");
            var add = schedule.Profile.Subjects.Single(s => s.Name == "新科目");
            schedule.AddCourse(1, "all", new Course { Subject = add.Name, StartTime = "10:00", EndTime = "10:40" });
            schedule.TemporarySchedules.Apply(new(day, "体育课调课", [new(add.Name, TimeSpan.FromHours(10), TimeSpan.FromHours(11))]));
            var input = Element<TextBox>("SubjectNameBox");
            input.Text = "体育";
            Click("SaveSubjectsButton");
            check(add.Name == "新科目" && Element<TextBlock>("SubjectSaveStatus").Text?.Contains("重名") == true,
                "The independent subject editor rejects duplicate names without touching the current course");
            input.Text = "体育与健康";
            Element<TextBox>("SubjectTeacherBox").Text = "陈老师";
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            check(add.Name == "新科目" && Element<Button>("SaveSubjectsButton").IsEnabled,
                $"Entering a subject name keeps all live lesson references unchanged before save: subject={add.Name}, save enabled={Element<Button>("SaveSubjectsButton").IsEnabled}");
            Click("SaveSubjectsButton");
            check(add.Name == "体育与健康" && Element<TextBlock>("SubjectSaveStatus").Text == "已保存科目信息。",
                "Saving the independent editor applies the rename and shows success inline");
            schedule.Reload();
            check(schedule.Profile.GetCourses(1, "odd").Single().Subject == "体育与健康" &&
                schedule.Profile.TemporarySchedules[day].Lessons.Single().Subject == "体育与健康" &&
                schedule.Profile.FindSubject("体育与健康")?.Teacher == "陈老师",
                "Reloading the fixture preserves the renamed subject, its teacher, the base course and the applied day course");
        }
        finally
        {
            _profileEditor?.Close();
            schedule.Load(path);
        }
    }
}
#endif
