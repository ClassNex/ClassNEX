using ClassNex.Models;
using ClassNex.Services;
using ClassNex.TestFixtures;

namespace ClassNex.Core.Tests;

[TestClass]
public sealed class ClassIslandImportTests
{
    private DirectoryInfo _temporary = null!;
    private string _exe = "", _data = "", _target = "", _layout = "", _profiles = "";
    private const string Schedule = "1db2017d-e374-4bc6-9d57-0b4adf03a6b8", Clock = "9e1af71d-8f77-4b21-a342-448787104dd9", Date = "df3f8295-21f6-482e-bada-fa0e5f14bb66";

    [TestInitialize]
    public void CreateSource()
    {
        _temporary = Directory.CreateTempSubdirectory("ClassNex-2x-import-");
        var install = Directory.CreateDirectory(Path.Combine(_temporary.FullName, "ClassIsland")).FullName;
        _exe = Path.Combine(install, "ClassIsland.exe");
        File.Copy(typeof(ClassIsland2Identity).Assembly.Location, _exe);
        File.WriteAllText(Path.Combine(install, "PackageType"), "folder");
        _data = Directory.CreateDirectory(Path.Combine(install, "data")).FullName;
        _profiles = Directory.CreateDirectory(Path.Combine(_data, "Profiles")).FullName;
        _layout = Path.Combine(Directory.CreateDirectory(Path.Combine(_data, "Config", "ComponentLayouts")).FullName, "Default.json");
        _target = Directory.CreateDirectory(Path.Combine(_temporary.FullName, "ClassNexData")).FullName;
        File.WriteAllText(Path.Combine(_data, "Settings.json"), """
        {"Scale":1.75,"Opacity":0.32,"Theme":1,"ColorSource":0,"PrimaryColor":"#1188AAFF","IsCustomForegroundColorEnabled":true,"CustomForegroundColor":{"R":3,"G":4,"B":5,"A":128},
         "WindowDockingLocation":5,"WindowDockingOffsetX":-19,"WindowDockingOffsetY":-8,"WindowDockingMonitorIndex":1,"RadiusX":12.5,"SingleWeekStartTime":"2026-09-01","MultiWeekRotationOffset":[-1,-1,1],
         "UserName":"source-ignored","NexApiBaseUrl":"invalid","IsOobeCompleted":true}
        """);
        File.WriteAllText(_layout, """
        {"Lines":[{"Children":[{"Id":"{{Clock}}","IsCustomForegroundColorEnabled":true,"ForegroundColor":"#33445580","Settings":{"ShowSeconds":true}},
          {"Id":"c911d762-107f-40c6-84cc-0146ab3c86b1","Settings":{"Children":[{"Id":"{{Schedule}}","Settings":{"ScheduleSpacing":1.4,"FadeCompletedClasses":false,"ShowCurrentLessonOnlyOnClass":true,"HideFinishedClass":true,"IsCountdownEnabled":false}} ,{"Id":"{{Date}}"}]}},
          {"Id":"abcd1111-0000-0000-0000-000000000000","NameCache":"时钟","Settings":{"Children":[{"Id":"{{Clock}}"}]}},
          {"Id":"{{Clock}}","IsFixedWidthEnabled":true,"FixedWidth":56,"IsCustomMarginEnabled":true,"MarginRight":3,"Settings":{"FlashTimeSeparator":false}}]}]}
        """.Replace("{{Clock}}", Clock).Replace("{{Schedule}}", Schedule).Replace("{{Date}}", Date));
    }

    [TestCleanup] public void Clean() => _temporary.Delete(true);

    [TestMethod]
    public void OrderAppearanceAndLocalSettingsSurviveReadback()
    {
        var have = new AppSettings { UserName = "local", NexApiBaseUrl = "local-api", IsOobeCompleted = false, TimetablePath = "retained.yaml" };
        var plan = ClassIslandImporter.Inspect(_exe);
        Assert.AreEqual(4, plan.WidgetCount); Assert.AreEqual(1, plan.SkippedWidgetCount); Assert.AreEqual(0, plan.ProfileCount);
        Assert.IsFalse(File.Exists(Path.Combine(_target, "Settings.json")));
        var result = ClassIslandImporter.Apply(plan, have, _target);
        var s = new SettingsStore(Path.Combine(_target, "Settings.json")).Load();
        CollectionAssert.AreEqual(new[] { "clock", "schedule", "date", "clock" }, s.Widgets.Select(w => w.Type).ToArray());
        Assert.IsTrue(s.Widgets[0].ShowSeconds); Assert.IsTrue(s.Widgets[0].FlashTimeSeparator); Assert.AreEqual("#80334455", s.Widgets[0].FontColor);
        Assert.AreEqual(56d, s.Widgets[3].Presentation!.Width); Assert.AreEqual(3, s.Widgets[3].Presentation!.Margin![2]); Assert.IsFalse(s.Widgets[3].FlashTimeSeparator);
        Assert.IsTrue(s.Widgets[1].ShowCurrentLessonOnlyOnClass && s.Widgets[1].HideFinishedClass); Assert.AreEqual(0, s.Widgets[1].CountdownSeconds);
        Assert.AreEqual("#FF1188AA", s.ThemeAccentColor); Assert.AreEqual("#80030405", s.OverlayForegroundColor);
        Assert.AreEqual(1.75, s.MainWindowScale); Assert.AreEqual(.32, s.BackgroundOpacity); Assert.AreEqual("light", s.ThemeMode); Assert.AreEqual(1, s.WeekRotationOffset);
        Assert.AreEqual(new ImportedDockPosition(5, -19, -8, 1), s.ImportedDock); Assert.AreEqual(12.5,s.OverlayCornerRadius);
        Assert.AreEqual("local", s.UserName); Assert.AreEqual("local-api", s.NexApiBaseUrl); Assert.AreEqual("retained.yaml", s.TimetablePath); Assert.IsFalse(s.IsOobeCompleted);
        Assert.AreEqual(1.9, have.MainWindowScale); Assert.IsTrue(File.Exists(_exe)); Assert.IsNull(result.ActiveProfilePath);
    }

    private const string ProfileJson = """
    {"Subjects":{"00000000-0000-0000-0000-000000000001":{"Name":"体育与健康","Initial":"体","TeacherName":"陈","IsOutDoor":true},"00000000-0000-0000-0000-000000000002":{"Name":"音乐"}},
     "TimeLayouts":{"00000000-0000-0000-0000-000000000010":{"Name":"夏季","Layouts":[
        {"TimeType":0,"StartTime":"08:00:00","EndTime":"08:45:00"},{"TimeType":1,"StartTime":"08:45:00","EndTime":"09:00:00","BreakName":"大课间"},
        {"TimeType":2,"StartTime":"09:00:00"},{"TimeType":3,"StartTime":"09:00:00"},{"TimeType":0,"StartTime":"09:00:00","EndTime":"09:50:00","DefaultClassId":"00000000-0000-0000-0000-000000000002"}]}},
     "ClassPlans":{"00000000-0000-0000-0000-000000000020":{"Name":"全局","AssociatedGroup":"00000000-0000-0000-0000-000000000000","TimeRule":{"WeekDay":1},"TimeLayoutId":"00000000-0000-0000-0000-000000000010","Classes":[{"IsEnabled":false},{}]},
        "00000000-0000-0000-0000-000000000021":{"Name":"优先的单周","TimeRule":{"WeekDay":1,"WeekCountDiv":1},"TimeLayoutId":"00000000-0000-0000-0000-000000000010","Classes":[{"SubjectId":"00000000-0000-0000-0000-000000000001"},{}]},
        "00000000-0000-0000-0000-000000000022":{"Name":"周日","TimeRule":{"WeekDay":0},"TimeLayoutId":"00000000-0000-0000-0000-000000000010","Classes":[{"SubjectId":"00000000-0000-0000-0000-000000000001"},{"IsEnabled":false}]}}}
    """;

    [TestMethod]
    public void AllProfilesAndRealCourseSlotsAreImportedWithoutReplacingAnyOldYaml()
    {
        File.WriteAllText(Path.Combine(_profiles, "Default.json"), ProfileJson);
        File.WriteAllText(Path.Combine(_profiles, "备用.json"), ProfileJson);
        var old = Path.Combine(_target, "timetable.yaml"); File.WriteAllText(old, "existing");
        var plan = ClassIslandImporter.Inspect(_exe); Assert.AreEqual(2, plan.ProfileCount);
        var result = ClassIslandImporter.Apply(plan, new(), _target);
        Assert.AreEqual("existing", File.ReadAllText(old));
        Assert.AreEqual("Default.yaml", Path.GetFileName(result.ActiveProfilePath));
        Assert.AreEqual(2, Directory.EnumerateFiles(Path.GetDirectoryName(result.ActiveProfilePath!)!, "*.yaml").Count());
        var profile = CsesCodec.Load(result.ActiveProfilePath!);
        var odd = profile.GetCourses(1, "odd"); var even = profile.GetCourses(1, "even");
        CollectionAssert.AreEqual(new[] { "体育与健康", "音乐" }, odd.Select(c => c.Subject).ToArray());
        Assert.AreEqual("09:00:00", odd[1].StartTime); Assert.HasCount(1, even); Assert.AreEqual("音乐", even[0].Subject);
        Assert.HasCount(1, profile.GetCourses(7,"even")); Assert.IsTrue(profile.Subjects[0].IsOutDoor); Assert.AreEqual("陈", profile.Subjects[0].Teacher);
        CollectionAssert.AreEqual(new[] { ClassTimeKind.Class, ClassTimeKind.Break, ClassTimeKind.Separator, ClassTimeKind.Action, ClassTimeKind.Class }, result.Settings.TimeLayout.Times.Select(t => t.Kind).ToArray());
        Assert.AreEqual("大课间", profile.ImportedTimeLayout!.Times[1].Name); Assert.DoesNotContain("x_classnex_time_layout", CsesCodec.Serialize(profile));
    }

    [TestMethod]
    public void ChangedSourceOrAddedProfileRequiresANewPreview()
    {
        var plan = ClassIslandImporter.Inspect(_exe);
        File.AppendAllText(_layout, " ");
        Assert.ThrowsExactly<InvalidDataException>(() => ClassIslandImporter.Apply(plan, new(), _target));
        plan = ClassIslandImporter.Inspect(_exe);
        File.WriteAllText(Path.Combine(_profiles, "Default.json"), ProfileJson);
        Assert.ThrowsExactly<InvalidDataException>(() => ClassIslandImporter.Apply(plan, new(), _target));
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(_target).Any());
    }

    [TestMethod]
    public void DefaultsDoNotInventAClockAndEmptyLayoutStaysEmpty()
    {
        File.Delete(_layout);
        var s = ClassIslandImporter.Apply(ClassIslandImporter.Inspect(_exe), new(), _target).Settings;
        CollectionAssert.AreEqual(new[] { "date", "schedule" }, s.Widgets.Select(w => w.Type).ToArray());
        File.WriteAllText(_layout, """{"Lines":[{"Children":[]}]}""");
        var empty = ClassIslandImporter.Apply(ClassIslandImporter.Inspect(_exe), new(), _target).Settings;
        Assert.IsEmpty(empty.Widgets);
        Assert.IsEmpty(Directory.GetFiles(_target,"*.tmp"));
    }

    [TestMethod]
    public void AnExeWithOnlyTheCorrectNameAndPathsOutsideTheDataDirectoryAreRejected()
    {
        var conf = Path.Combine(_data, "Settings.json");
        foreach (var text in new[] { """{"SelectedProfile":"../elsewhere.json"}""", """{"CurrentComponentConfig":"C:\\stuff"}""", """{"SelectedProfile":"missing.json"}""" })
        { File.WriteAllText(conf,text); Assert.ThrowsExactly<InvalidDataException>(() => ClassIslandImporter.Inspect(_exe)); }
        File.WriteAllText(conf,"{}"); File.Copy(typeof(AppSettings).Assembly.Location, _exe, true);
        Assert.ThrowsExactly<InvalidDataException>(() => ClassIslandImporter.Inspect(_exe));
    }

    [TestMethod]
    public void BadProfileFailsBeforeAnyDestinationWriteAndCancellationDoesNotAlterSource()
    {
        File.WriteAllText(Path.Combine(_profiles, "Default.json"), "{}");
        Assert.ThrowsExactly<InvalidDataException>(() => ClassIslandImporter.Inspect(_exe));
        File.WriteAllText(Path.Combine(_profiles, "Default.json"), ProfileJson);
        var plan = ClassIslandImporter.Inspect(_exe);
        using var stopped = new CancellationTokenSource(); stopped.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => ClassIslandImporter.Apply(plan, new(), _target, stopped.Token));
        Assert.ThrowsExactly<InvalidDataException>(() => ClassIslandImporter.Apply(plan, new(), _data));
        Assert.IsEmpty(Directory.GetFileSystemEntries(_target));
        Assert.AreEqual(ProfileJson, File.ReadAllText(Path.Combine(_profiles, "Default.json")));
    }

    [TestMethod]
    public void FailedSettingsCommitRollsBackOnlyNewFiles()
    {
        File.WriteAllText(Path.Combine(_profiles, "Default.json"), ProfileJson);
        var trap = Directory.CreateDirectory(Path.Combine(_target, "Settings.json"));
        var keep = Path.Combine(trap.FullName, "keep"); File.WriteAllText(keep,"keep");
        try { ClassIslandImporter.Apply(ClassIslandImporter.Inspect(_exe), new(), _target); Assert.Fail("Expected refused commit"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Assert.AreEqual("keep", File.ReadAllText(keep)); Assert.HasCount(1, Directory.GetFileSystemEntries(_target));
    }

    [TestMethod]
    public void AcceptsSelectedPayloadAndRefusesMalformedComponentLayout()
    {
        var payload = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(_exe)!, "app-2.1.0.1-0"));
        var selected = Path.Combine(payload.FullName, "ClassIsland.Desktop.exe"); File.Copy(_exe, selected);
        Assert.AreEqual(_data, ClassIslandImporter.Inspect(selected).Source.DataRoot);
        File.WriteAllText(_layout,"{}"); Assert.ThrowsExactly<InvalidDataException>(() => ClassIslandImporter.Inspect(selected));
    }

    [TestMethod]
    [DataRow("ClassIsland.exe")]
    [DataRow("ClassIsland.Desktop.exe")]
    public void ClassicFlatPackageUsesOnlySettingsBesideTheSelectedProgram(string program)
    {
        var root = Path.GetDirectoryName(_exe)!;
        File.WriteAllText(Path.Combine(root, "PackageType"), "folderClassic");
        var selected = Path.Combine(root, program);
        if (selected != _exe) File.Copy(_exe, selected);
        Assert.ThrowsExactly<InvalidDataException>(() => ClassIslandImporter.Inspect(selected));
        var original = Path.Combine(root, "Settings.json");
        File.WriteAllText(original, """{"Scale":1.25}""");
        var plan = ClassIslandImporter.Inspect(selected);
        Assert.AreEqual(root, plan.Source.DataRoot);
        Assert.AreEqual(1.25, ClassIslandImporter.Apply(plan, new(), _target).Settings.MainWindowScale);
        Assert.AreEqual("""{"Scale":1.25}""", File.ReadAllText(original));
        File.Delete(Path.Combine(root, "PackageType"));
        Assert.ThrowsExactly<InvalidDataException>(() => ClassIslandImporter.VerifySource(plan));
    }

    [TestMethod]
    public void ClassicLauncherSelectsOnlyAnUnambiguousRuntimeAndTheDirectExeUsesItsOwnData()
    {
        var root = Path.GetDirectoryName(_exe)!;
        File.WriteAllText(Path.Combine(root, "PackageType"), "folderClassic");
        File.WriteAllText(Path.Combine(root, "Settings.json"), "not the running program's data");
        var first = Directory.CreateDirectory(Path.Combine(root, "app-2.1.0.1-0")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(root, "app-2.1.0.2-0")).FullName;
        var desktop = Path.Combine(first, "ClassIsland.Desktop.exe");
        File.Copy(_exe, desktop);
        File.Copy(_exe, Path.Combine(second, "ClassIsland.Desktop.exe"));
        var unrelated = Directory.CreateDirectory(Path.Combine(root, "app-2.notes")).FullName;
        File.Copy(_exe, Path.Combine(unrelated, "ClassIsland.Desktop.exe"));
        File.WriteAllText(Path.Combine(first, "Settings.json"), "{}");
        File.WriteAllText(Path.Combine(second, ".partial"), "");
        Assert.AreEqual(first, ClassIslandImporter.Inspect(_exe).Source.DataRoot);
        File.Delete(Path.Combine(second, ".partial"));
        Assert.Contains("ClassIsland.Desktop.exe", Assert.ThrowsExactly<InvalidDataException>(() => ClassIslandImporter.Inspect(_exe)).Message);
        File.WriteAllText(Path.Combine(first, "PackageType"), "folderClassic");
        File.Delete(Path.Combine(root, "PackageType"));
        Assert.AreEqual(first, ClassIslandImporter.Inspect(desktop).Source.DataRoot);
    }

    [TestMethod]
    public void RemovingThePackageMarkerInvalidatesAPreviewEvenWhenTheDataPathStaysTheSame()
    {
        var plan = ClassIslandImporter.Inspect(_exe);
        File.Delete(Path.Combine(Path.GetDirectoryName(_exe)!, "PackageType"));
        Assert.AreEqual(plan.Source.DataRoot, ClassIslandImporter.Inspect(_exe).Source.DataRoot);
        Assert.ThrowsExactly<InvalidDataException>(() => ClassIslandImporter.Apply(plan, new(), _target));
        Assert.IsEmpty(Directory.GetFileSystemEntries(_target));
    }

    [TestMethod]
    public void DateOnlyZeroOpacityAndWallpaperPaletteSurviveRestart()
    {
        File.WriteAllText(Path.Combine(_data,"Settings.json"), """{"Theme":0,"ColorSource":3,"SelectedPlatteIndex":1,"WallpaperColorPlatte":["#000000ff","#aabbcc80"],"Opacity":0}""");
        File.WriteAllText(_layout, """{"Lines":[{"Children":[{"Id":"df3f8295-21f6-482e-bada-fa0e5f14bb66"}]}]}""");
        ClassIslandImporter.Apply(ClassIslandImporter.Inspect(_exe), new(), _target);
        var settings = new SettingsStore(Path.Combine(_target,"Settings.json")).Load();
        CollectionAssert.AreEqual(new[] { "date" }, settings.Widgets.Select(w => w.Type).ToArray());
        Assert.AreEqual(0d,settings.BackgroundOpacity); Assert.AreEqual("system",settings.ThemeMode); Assert.AreEqual("#80AABBCC",settings.ThemeAccentColor);
    }

    [TestMethod]
    public void DateOverridesAreVisibleDuringPreflightAndDoNotBecomeWeeklyLessons()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(ProfileJson)!.AsObject();
        json["OrderedSchedules"] = System.Text.Json.Nodes.JsonNode.Parse("""{"2026-12-01T00:00:00":{"ClassPlanId":"00000000-0000-0000-0000-000000000021"},"2026-12-02T00:00:00":{}}""");
        File.WriteAllText(Path.Combine(_profiles, "Default.json"), json.ToJsonString());
        var plan = ClassIslandImporter.Inspect(_exe);
        Assert.IsTrue(plan.Warnings.Any(w => w.Contains("2 项") && w.Contains("2026-12-01") && w.Contains("2026-12-02")));
        var result = ClassIslandImporter.Apply(plan, new(), _target);
        Assert.IsEmpty(CsesCodec.Load(result.ActiveProfilePath!).GetCourses(2, "odd"));
    }

    [TestMethod]
    public void ManagedInstallationUsesOnlyItsCachedProfileAndReadsLegacyTimes()
    {
        File.WriteAllText(Path.Combine(_data,"Settings.json"), """{"IsManagementEnabled":true,"SelectedProfile":"other.json"}""");
        var cached = ProfileJson.Replace("\"StartTime\":\"08:00:00\"","\"StartSecond\":\"2024-05-25T08:00:00\"").Replace("\"EndTime\":\"08:45:00\"","\"EndSecond\":\"2024-05-25T08:45:00\"");
        File.WriteAllText(Path.Combine(_profiles,"_management-profile.json"),cached);
        var result = ClassIslandImporter.Apply(ClassIslandImporter.Inspect(_exe), new(), _target);
        Assert.AreEqual("_management-profile.yaml",Path.GetFileName(result.ActiveProfilePath));
        Assert.AreEqual("08:00:00",CsesCodec.Load(result.ActiveProfilePath!).GetCourses(1,"odd")[0].StartTime);
        Assert.IsFalse(result.Settings.CentralManagement.IsEnabled);
    }
}
