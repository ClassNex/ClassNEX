using System.Text;
using System.Text.Json;
using ClassNex.Models;
using ClassNex.Services;

namespace ClassNex.Core.Tests;

[TestClass]
public sealed class WidgetLayoutPresetTests
{
    private const string Original = "默认方案";
    private DirectoryInfo _directory = null!;
    private string _file = null!;
    private WidgetLayoutPresetStore _store = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("ClassNex-widget-presets-");
        _file = Path.Combine(_directory.FullName, "WidgetLayoutPresets.json");
        _store = new(_file);
    }

    [TestCleanup] public void CleanUp() => _directory.Delete(recursive: true);

    private static List<WidgetConfig> Desktop() =>
    [
        new() { Type = "text", Row = 1, Order = 3, Text = "班级公告", DisplayWhen = WidgetDisplayWhen.Lesson, Presentation = new(Margin: [1, 2, 3, 4], Opacity: .7) },
        new() { Type = "text", Row = 0, Order = 1, Text = "值日生", DisplayWhen = WidgetDisplayWhen.NoLessonsToday, IsEnabled = false },
        new() { Type = "future-plugin", Row = 1, Order = 4, FontScale = 1.5 }
    ];

    [TestMethod]
    public void VisitingDoesNotMaterializeTheCatalogOrAlterAnExistingDesktop()
    {
        var live = Desktop();
        var before = JsonSerializer.Serialize(live);
        foreach (var widgets in new[] { live, [] })
        {
            var index = _store.Read(widgets);
            Assert.AreEqual(Original, index.Active);
            CollectionAssert.AreEqual(new[] { Original }, index.Names.ToArray());
        }
        Assert.IsEmpty(_directory.EnumerateFileSystemInfos().ToArray());
        Assert.AreEqual(before, JsonSerializer.Serialize(live));
    }

    [TestMethod]
    public void NewLayoutStartsFromDefaultsAndSwitchBackRestoresTheLastEditsToBothDesktops()
    {
        var live = Desktop(); var original = JsonSerializer.Serialize(live);
        _store.Create(Original, "考试周", live, target => live = target);
        Assert.AreEqual(JsonSerializer.Serialize(AppSettings.CreateDefaultWidgets()), JsonSerializer.Serialize(live));
        Assert.AreEqual("考试周", _store.Read(live).Active);
        live[1].Row = 3; live[1].ShowSeconds = true; live.RemoveAt(0);
        var exam = JsonSerializer.Serialize(live);
        _store.Switch("考试周", Original, live, target => live = target);
        Assert.AreEqual(original, JsonSerializer.Serialize(live));
        live[0].Text = "刚刚修改的文本";
        var reopened = new WidgetLayoutPresetStore(_file);
        reopened.Switch(Original, "考试周", live, target => live = target);
        Assert.AreEqual(exam, JsonSerializer.Serialize(live));
        reopened.Switch("考试周", Original, live, target => live = target);
        Assert.AreEqual("刚刚修改的文本", live[0].Text);
        Assert.AreEqual("future-plugin", live[2].Type);
        CollectionAssert.AreEqual(new[] { 1, 0, 1 }, live.Select(w => w.Row).ToArray());
    }

    [TestMethod]
    public void SavingACopyDoesNotSwitchAndNestedStylesAndDuplicateTypesStayIndependent()
    {
        var live = Desktop(); var first = live[0];
        _store.SaveCopy(Original, "副本", live);
        Assert.AreEqual(Original, _store.Read(live).Active);
        Assert.AreSame(first, live[0]);
        first.Text = "只在原件"; first.Presentation!.Margin![1] = 9; first.DisplayWhen = WidgetDisplayWhen.Break;
        _store.Switch(Original, "副本", live, target => live = target);
        Assert.AreEqual("班级公告", live[0].Text); Assert.AreEqual(2, live[0].Presentation!.Margin![1]);
        Assert.AreEqual(WidgetDisplayWhen.Lesson, live[0].DisplayWhen);
        Assert.AreEqual(WidgetDisplayWhen.NoLessonsToday, live[1].DisplayWhen);
        Assert.AreNotSame(first.Presentation.Margin, live[0].Presentation!.Margin);
        live[0].Presentation!.Margin![1] = 6; live[1].IsEnabled = true; live[1].DisplayWhen = WidgetDisplayWhen.Activity;
        var reopened = new WidgetLayoutPresetStore(_file);
        reopened.Switch("副本", Original, live, target => live = target);
        Assert.AreEqual("只在原件", live[0].Text); Assert.AreEqual(9, live[0].Presentation!.Margin![1]); Assert.IsFalse(live[1].IsEnabled);
        Assert.AreEqual(WidgetDisplayWhen.Break, live[0].DisplayWhen);
        Assert.AreEqual(WidgetDisplayWhen.NoLessonsToday, live[1].DisplayWhen);
        reopened.Switch(Original, "副本", live, target => live = target);
        Assert.AreEqual(WidgetDisplayWhen.Activity, live[1].DisplayWhen);
    }

    [TestMethod]
    public void RenameAndDeleteProtectTheActiveLayoutAndMakeNoChangesOnInvalidInput()
    {
        var live = Desktop();
        _store.Rename(Original, "  CLASS  ", live);
        _store.SaveCopy("CLASS", "副本", live);
        _store.Rename("CLASS", "Class", live);
        Assert.AreEqual("Class", _store.Read(live).Active);
        var before = File.ReadAllBytes(_file);
        Assert.ThrowsExactly<InvalidOperationException>(() => _store.Delete("class", "CLASS", live));
        Assert.ThrowsExactly<InvalidOperationException>(() => _store.Delete("old", "副本", live));
        Assert.ThrowsExactly<InvalidOperationException>(() => _store.Rename("Class", "副本", live));
        Assert.ThrowsExactly<InvalidOperationException>(() => _store.SaveCopy("Class", "class", live));
        Assert.ThrowsExactly<ArgumentException>(() => _store.Rename("Class", "  ", live));
        Assert.ThrowsExactly<ArgumentException>(() => _store.SaveCopy("Class", "X\nY", live));
        Assert.ThrowsExactly<ArgumentException>(() => _store.SaveCopy("Class", new string('x', 65), live));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(_file));
        _store.Delete("Class", "副本", live);
        CollectionAssert.AreEqual(new[] { "Class" }, _store.Read(live).Names.ToArray());
    }

    [TestMethod]
    public void EmptyLayoutsSurviveRestartWithoutCopyingUnrelatedSettings()
    {
        var settings = new AppSettings { ThemeAccentColor = "#112233", Widgets = [], ThemeMode = "dark" };
        var settingsPath = Path.Combine(_directory.FullName, "Settings.json");
        var preferences = new SettingsStore(settingsPath);
        preferences.Save(settings); var oldSettings = File.ReadAllBytes(settingsPath);
        _store.SaveCopy(Original, "空/布局", settings.Widgets);
        CollectionAssert.AreEqual(oldSettings, File.ReadAllBytes(settingsPath));
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(_file)))
        {
            foreach (var preset in doc.RootElement.GetProperty("Presets").EnumerateArray())
                CollectionAssert.AreEquivalent(new[] { "Name", "Widgets" }, preset.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.IsFalse(doc.RootElement.TryGetProperty("ThemeMode", out _)); Assert.IsFalse(doc.RootElement.TryGetProperty("Account", out _));
        }
        void Apply(List<WidgetConfig> widgets) { settings.Widgets = widgets; preferences.Save(settings); }
        _store.Create(Original, "日常", settings.Widgets, Apply);
        var reloaded = new WidgetLayoutPresetStore(_file);
        reloaded.Switch("日常", "空/布局", preferences.Load().Widgets, Apply);
        var restored = preferences.Load();
        Assert.IsEmpty(restored.Widgets); Assert.AreEqual("dark", restored.ThemeMode); Assert.AreEqual("#112233", restored.ThemeAccentColor);
        Assert.AreEqual("空/布局", reloaded.Read(restored.Widgets).Active);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("null")]
    [DataRow("{broken")]
    [DataRow("""{"Version":2,"Active":"A","Presets":[{"Name":"A","Widgets":[]}]}""")]
    [DataRow("""{"Version":1,"Active":"A","Presets":[{"Name":"A","Widgets":[null]}]}""")]
    [DataRow("""{"Version":1,"Active":"A","Presets":[{"Name":"A","Widgets":[]},{"Name":"a","Widgets":[]}]}""")]
    [DataRow("""{"Version":1,"Active":"A","Presets":[{"Name":"A","Widgets":[]}],"SwitchingTo":"missing"}""")]
    public void ACorruptOrUnsupportedCatalogRequiresExplicitRecoveryAndKeepsTheOriginalBytes(string broken)
    {
        var original = Encoding.UTF8.GetBytes(broken); File.WriteAllBytes(_file, original);
        var live = Desktop(); var before = JsonSerializer.Serialize(live);
        void Never(List<WidgetConfig> _) => Assert.Fail("不能替换桌面");
        Assert.ThrowsExactly<InvalidDataException>(() => _store.Read(live));
        Assert.ThrowsExactly<InvalidDataException>(() => _store.Create(Original, "新建", live, Never));
        Assert.ThrowsExactly<InvalidDataException>(() => _store.Switch(Original, "副本", live, Never));
        Assert.ThrowsExactly<InvalidDataException>(() => _store.SaveCopy(Original, "副本", live));
        Assert.ThrowsExactly<InvalidDataException>(() => _store.Rename(Original, "新名", live));
        Assert.ThrowsExactly<InvalidDataException>(() => _store.Delete(Original, "副本", live));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(_file));
        Assert.IsEmpty(_directory.GetFiles("*.bak"));
        var backup = _store.Recover(live);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(backup));
        Assert.AreEqual(Original, _store.Read(live).Active);
        Assert.ThrowsExactly<InvalidOperationException>(() => _store.Recover(live));
        Assert.HasCount(1, _directory.GetFiles("*.bak"));
        Assert.AreEqual(before, JsonSerializer.Serialize(live));
        _store.Create(Original, "新建", live, target => live = target);
        _store.Switch("新建", Original, live, target => live = target);
        Assert.AreEqual(before, JsonSerializer.Serialize(live));
    }

    [TestMethod]
    public void FailingToAcquireTheCatalogOrWriteThePendingRecordNeverAppliesAReplacement()
    {
        var live = Desktop(); _store.SaveCopy(Original, "保留", live);
        var before = File.ReadAllBytes(_file);
        void Never(List<WidgetConfig> _) => Assert.Fail("写入失败后不能切换桌面");
        using (File.Open(_file + ".lock", FileMode.Open, FileAccess.Write, FileShare.None))
            Assert.Throws<IOException>(() => _store.Create(Original, "新建", live, Never));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(_file));
        live[0].Text = new string('x', 4 * 1024 * 1024);
        Assert.ThrowsExactly<InvalidOperationException>(() => _store.Switch(Original, "保留", live, Never));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(_file));
        var bad = new WidgetLayoutPresetStore(Path.Combine(_file, "not-a-directory", "book.json"));
        Assert.Throws<IOException>(() => bad.Create(Original, "新建", live, Never));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ReopeningAnInterruptedSwitchUsesTheDesktopThatWasActuallySaved(bool saved)
    {
        var live = Desktop(); var original = JsonSerializer.Serialize(live);
        Assert.ThrowsExactly<IOException>(() => _store.Create(Original, "临时", live, target =>
        {
            if (saved) live = target;
            throw new IOException("模拟保存中断");
        }));
        var reopened = new WidgetLayoutPresetStore(_file);
        var active = reopened.Read(live).Active;
        Assert.AreEqual(saved ? "临时" : Original, active);
        var other = saved ? Original : "临时";
        reopened.Switch(active, other, live, target => live = target);
        Assert.AreEqual(saved ? original : JsonSerializer.Serialize(AppSettings.CreateDefaultWidgets()), JsonSerializer.Serialize(live));
        Assert.AreEqual(other, new WidgetLayoutPresetStore(_file).Read(live).Active);
    }

    [TestMethod]
    public void AnInterruptedSwitchAndUnrelatedDesktopKeepAllThreeLayouts()
    {
        var live = Desktop(); var outgoing = JsonSerializer.Serialize(live);
        Assert.ThrowsExactly<IOException>(() => _store.Create(Original, "预期目标", live, _ => throw new IOException()));
        live = [new() { Type = "text", Row = 2, Text = "独立更改" }];
        var reopened = new WidgetLayoutPresetStore(_file);
        var recovery = reopened.Read(live);
        Assert.HasCount(3, recovery.Names);
        Assert.AreNotEqual(Original, recovery.Active); Assert.AreNotEqual("预期目标", recovery.Active);
        reopened.Switch(recovery.Active, Original, live, target => live = target);
        Assert.AreEqual(outgoing, JsonSerializer.Serialize(live));
        reopened.Switch(Original, "预期目标", live, target => live = target);
        Assert.AreEqual(JsonSerializer.Serialize(AppSettings.CreateDefaultWidgets()), JsonSerializer.Serialize(live));
        reopened.Switch("预期目标", recovery.Active, live, target => live = target);
        Assert.AreEqual("独立更改", live.Single().Text); Assert.AreEqual(2, live.Single().Row);
    }

    [TestMethod]
    public void InterruptedIdenticalCopiesKeepTheLastConfirmedSelection()
    {
        var live = Desktop(); _store.SaveCopy(Original, "同一布局的副本", live);
        Assert.ThrowsExactly<IOException>(() => _store.Switch(Original, "同一布局的副本", live, _ => throw new IOException()));
        var reopened = new WidgetLayoutPresetStore(_file);
        Assert.AreEqual(Original, reopened.Read(live).Active);
        reopened.Switch(Original, "同一布局的副本", live, target => live = target);
        Assert.AreEqual("同一布局的副本", reopened.Read(live).Active);
    }

    [TestMethod]
    public void AnOutdatedWindowCannotSwitchOrRenameOverAnotherWindow()
    {
        var live = Desktop();
        var other = new WidgetLayoutPresetStore(_file);
        var stale = other.Read(live).Active;
        _store.Create(Original, "日常", live, target => live = target);
        Assert.ThrowsExactly<InvalidOperationException>(() => other.Rename(stale, "不要覆盖", live));
        Assert.ThrowsExactly<InvalidOperationException>(() => other.Switch(stale, Original, live, _ => Assert.Fail()));
        var current = other.Read(live); Assert.AreEqual("日常", current.Active);
        CollectionAssert.AreEqual(new[] { Original, "日常" }, current.Names.ToArray());
    }
}
