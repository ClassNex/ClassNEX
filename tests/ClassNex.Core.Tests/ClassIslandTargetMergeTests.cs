using System.Text;
using System.Text.Json;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.TestFixtures;

namespace ClassNex.Core.Tests;

[TestClass]
public sealed class ClassIslandTargetMergeTests
{
    private DirectoryInfo _temp = null!;
    private string _exe = "", _target = "", _settings = "";

    [TestInitialize]
    public void SetUp()
    {
        _temp = Directory.CreateTempSubdirectory("ClassNex-migration-target-");
        var source = Directory.CreateDirectory(Path.Combine(_temp.FullName, "ClassIsland")).FullName;
        _exe = Path.Combine(source, "ClassIsland.exe");
        File.Copy(typeof(ClassIsland2Identity).Assembly.Location, _exe);
        var data = Directory.CreateDirectory(Path.Combine(source, "data"));
        File.WriteAllText(Path.Combine(data.FullName, "Settings.json"), "{\"Scale\":1.75}");
        _target = Directory.CreateDirectory(Path.Combine(_temp.FullName, "ClassNEX-data")).FullName;
        _settings = Path.Combine(_target, "Settings.json");
    }

    [TestCleanup] public void TearDown() => _temp.Delete(true);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void KeepsNewLocalValuesFromTheExactTargetSnapshot(bool utf16)
    {
        var startup = new AppSettings { UserName = "启动时", RunAtStartup = false, TimetablePath = "old.yaml" };
        new SettingsStore(_settings).Save(startup);
        var plan = ClassIslandImporter.Inspect(_exe);
        File.WriteAllText(_settings, """{"username":"预检后的名字","RunAtStartup":true,"TimetablePath":"edited.yaml","MainWindowScale":2.75,"Notification":{"SoundVolume":27}}""",
            utf16 ? Encoding.Unicode : Encoding.UTF8);

        var result = ClassIslandImporter.Apply(plan, startup, _target);
        var saved = new SettingsStore(_settings).Load();
        Assert.AreEqual("预检后的名字", result.Settings.UserName);
        Assert.AreEqual("预检后的名字", saved.UserName);
        Assert.IsTrue(saved.RunAtStartup);
        Assert.AreEqual("edited.yaml", saved.TimetablePath);
        Assert.AreEqual(27, saved.Notification.SoundVolume);
        Assert.AreEqual(1.75, saved.MainWindowScale);
        Assert.AreEqual("启动时", startup.UserName);
    }

    [TestMethod]
    public void DoesNotReplaceAnUnreadableUpdatedTarget()
    {
        var plan = ClassIslandImporter.Inspect(_exe);
        File.WriteAllText(_settings, "{not closed", Encoding.Unicode);
        var previous = File.ReadAllBytes(_settings);

        Assert.ThrowsExactly<JsonException>(() => ClassIslandImporter.Apply(plan, new(), _target));
        CollectionAssert.AreEqual(previous, File.ReadAllBytes(_settings));
        Assert.HasCount(1, Directory.GetFileSystemEntries(_target));
    }

    [TestMethod]
    public void DoesNotImportIntoADeviceThatAlreadyCompletedOnboarding()
    {
        var plan = ClassIslandImporter.Inspect(_exe);
        new SettingsStore(_settings).Save(new() { IsOobeCompleted = true, MainWindowScale = 2.75 });
        var previous = File.ReadAllBytes(_settings);

        var error = Assert.ThrowsExactly<IOException>(() => ClassIslandImporter.Apply(plan, new(), _target));
        Assert.Contains("首次设置状态已变化", error.Message);
        CollectionAssert.AreEqual(previous, File.ReadAllBytes(_settings));
    }
}
