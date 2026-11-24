using System.Diagnostics;
using ClassNex.Services;
using ClassNex.TestFixtures;

namespace ClassNex.Core.Tests;

[TestClass]
public sealed class MigrationCleanupTests
{
    private DirectoryInfo _temp = null!;
    private string _source = null!, _data = null!, _exe = null!, _target = null!, _payload = null!;

    [TestInitialize]
    public void SetUp()
    {
        _temp = Directory.CreateTempSubdirectory("ClassNex-recycle-test-");
        _source = Path.Combine(_temp.FullName, "ClassIsland-test-" + Guid.NewGuid().ToString("N"));
        _data = Directory.CreateDirectory(Path.Combine(_source, "data")).FullName;
        _payload = Directory.CreateDirectory(Path.Combine(_source, "app-2.1.0.1-0")).FullName;
        _target = Directory.CreateDirectory(Path.Combine(_temp.FullName, "target")).FullName;
        _exe = Path.Combine(_source, "ClassIsland.exe");
        foreach (var path in new[] { _exe, Path.Combine(_payload, "ClassIsland.Desktop.exe") }) File.Copy(typeof(ClassIsland2Identity).Assembly.Location, path);
        foreach (var path in new[] { Path.Combine(_data, "Settings.json"), Path.Combine(_payload, "ClassIsland.dll") }) File.WriteAllText(path, "{}");
        File.WriteAllText(Path.Combine(_source, "PackageType"), "folder");
        File.WriteAllText(Path.Combine(_source, "files.json"), """{"Components":{"app":{"Files":{"ClassIsland.Desktop.exe":{},"ClassIsland.dll":{}}}}}""");
    }

    [TestCleanup]
    public void TearDown() => _temp.Delete(true);

    [TestMethod]
    public async Task SharedRootAndInstallerRemainIntact()
    {
        if (!OperatingSystem.IsWindows()) return;
        var other = Path.Combine(_source, "我的文件.txt");
        File.WriteAllText(other, "keep");
        var message = await ClassIslandSourceCleanup.RecycleAsync(ClassIslandImporter.Inspect(_exe), _target, CancellationToken.None);
        Assert.Contains("其他文件", message);
        Assert.AreEqual("keep", File.ReadAllText(other));
        File.WriteAllText(Path.Combine(_source, "PackageType"), "installer");
        Assert.IsNotNull(ClassIslandSourceCleanup.RetainReason(_source, _data, _exe, _target));
        Assert.IsNotNull(ClassIslandSourceCleanup.RetainReason(_source, _target, _exe, _data));
    }

    [TestMethod]
    public void RejectsOverlapsAndLookalikeInstallations()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.IsNotNull(ClassIslandSourceCleanup.RetainReason(_source, _data, _exe, _data));
        Assert.IsNotNull(ClassIslandSourceCleanup.RetainReason(_source, _data, _exe, _temp.FullName));
        Assert.IsNotNull(ClassIslandSourceCleanup.RetainReason(Path.GetPathRoot(_source)!, _data, _exe, _target));
        Assert.IsNotNull(ClassIslandSourceCleanup.RetainReason(_source, _data, Path.Combine(_data, "Settings.json"), _target));
        File.Delete(Path.Combine(_payload, "ClassIsland.dll"));
        Assert.IsNotNull(ClassIslandSourceCleanup.RetainReason(_source, _data, _exe, _target));
    }

    [TestMethod]
    public void ForeignPayloadIsNotPartOfThePackage()
    {
        if (!OperatingSystem.IsWindows()) return;
        File.WriteAllText(Path.Combine(_payload, "notes.txt"), "keep");
        Assert.Contains("不属于发行包", ClassIslandSourceCleanup.RetainReason(_source, _data, _exe, _target)!);
    }

    [TestMethod]
    public void NestedManifestFoldersAreAllowedButAnUnrelatedEmptyFolderIsKept()
    {
        if (!OperatingSystem.IsWindows()) return;
        var assets = Directory.CreateDirectory(Path.Combine(_payload, "assets", "fonts")).FullName;
        File.WriteAllText(Path.Combine(assets, "font.bin"), "example");
        File.WriteAllText(Path.Combine(_source, "files.json"), """{"Components":{"app":{"Files":{"ClassIsland.Desktop.exe":{},"ClassIsland.dll":{},"assets/fonts/font.bin":{}}}}}""");
        Assert.IsNull(ClassIslandSourceCleanup.RetainReason(_source, _data, _exe, _target));
        Directory.CreateDirectory(Path.Combine(assets, "my-stuff"));
        Assert.Contains("不属于发行包", ClassIslandSourceCleanup.RetainReason(_source, _data, _exe, _target)!);
    }

    [TestMethod]
    public async Task ShellCanRecycleAnIsolatedPortableFixtureWithoutTouchingItsSibling()
    {
        if (!OperatingSystem.IsWindows()) return;
        var marker = Path.Combine(_target, "keep.txt");
        File.WriteAllText(marker, "keep");
        var plan = ClassIslandImporter.Inspect(Path.Combine(_payload, "ClassIsland.Desktop.exe"));
        var message = await ClassIslandSourceCleanup.RecycleAsync(plan, _target, CancellationToken.None);
        Assert.IsFalse(Directory.Exists(_source), message);
        Assert.Contains("可从回收站还原", message);
        Assert.AreEqual("keep", File.ReadAllText(marker));
    }

    [TestMethod]
    public async Task ChangingTheMarkerCannotPromoteAClassicSourceToAutomaticCleanup()
    {
        if (!OperatingSystem.IsWindows()) return;
        var marker = Path.Combine(_source, "PackageType");
        File.WriteAllText(marker, "folderClassic");
        var settings = Path.Combine(_payload, "Settings.json");
        File.WriteAllText(settings, """{"Scale":1.2}""");
        var plan = ClassIslandImporter.Inspect(_exe);
        Assert.AreEqual(1.2, ClassIslandImporter.Apply(plan, new(), _target).Settings.MainWindowScale);
        File.WriteAllText(marker, "folder");
        var message = await ClassIslandSourceCleanup.RecycleAsync(plan, _target, CancellationToken.None);
        Assert.Contains("已保留原目录", message);
        Assert.IsTrue(File.Exists(settings));
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("payload")]
    [DataRow("marker")]
    public async Task LatePackageChangesRetainTheOriginalWithoutUndoingTheImport(string item)
    {
        if (!OperatingSystem.IsWindows()) return;
        var plan = ClassIslandImporter.Inspect(_exe);
        ClassIslandImporter.Apply(plan, new(), _target);
        var target = File.ReadAllBytes(Path.Combine(_target, "Settings.json"));
        var file = item switch
        {
            "root" => Path.Combine(_source, "我的笔记.txt"),
            "payload" => Path.Combine(_payload, "我的笔记.txt"),
            _ => Path.Combine(_source, "PackageType"),
        };
        var observed = new List<ClassIslandCleanupStage>();
        var message = await ClassIslandSourceCleanup.RecycleAsync(plan, _target, CancellationToken.None, current =>
        {
            observed.Add(current);
            if (current != ClassIslandCleanupStage.VerifyingSource) return;
            File.WriteAllText(file, item == "marker" ? "installer" : "keep");
        });
        CollectionAssert.AreEqual(new[] { ClassIslandCleanupStage.CheckingPackage, ClassIslandCleanupStage.VerifyingSource }, observed);
        Assert.Contains("已保留原目录", message);
        Assert.AreEqual(item == "marker" ? "installer" : "keep", File.ReadAllText(file));
        CollectionAssert.AreEqual(target, File.ReadAllBytes(Path.Combine(_target, "Settings.json")));
    }

    [TestMethod]
    public async Task JunctionAddedDuringVerificationKeepsBothInstallations()
    {
        if (!OperatingSystem.IsWindows()) return;
        var plan = ClassIslandImporter.Inspect(_exe);
        ClassIslandImporter.Apply(plan, new(), _target);
        var target = File.ReadAllBytes(Path.Combine(_target, "Settings.json"));
        var link = Path.Combine(_data, "external");
        try
        {
            var message = await ClassIslandSourceCleanup.RecycleAsync(plan, _target, CancellationToken.None, stage =>
            {
                if (stage != ClassIslandCleanupStage.VerifyingSource) return;
                var make = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    ArgumentList = { "/d", "/c", "mklink", "/J", link, _target }
                };
                using var child = Process.Start(make)!;
                Assert.IsTrue(child.WaitForExit(10000)); Assert.AreEqual(0, child.ExitCode);
            });
            Assert.Contains("链接", message);
            Assert.IsTrue(File.Exists(_exe));
            CollectionAssert.AreEqual(target, File.ReadAllBytes(Path.Combine(_target, "Settings.json")));
        }
        finally { if (Directory.Exists(link)) Directory.Delete(link); }
    }

    [TestMethod, DoNotParallelize]
    public async Task AProcessStartedFromTheOldFolderAfterTheFirstCheckKeepsTheFolder()
    {
        if (!OperatingSystem.IsWindows()) return;
        var ownFixture = Directory.CreateDirectory(Path.Combine(_data, "fixture-host")).FullName;
        var binaries = Path.GetDirectoryName(typeof(OnboardingHost).Assembly.Location)!;
        foreach (var file in new[] { "OnboardingHost.dll", "OnboardingHost.runtimeconfig.json", "OnboardingHost.deps.json", "ClassNex.Core.dll", "YamlDotNet.dll" })
            File.Copy(Path.Combine(binaries, file), Path.Combine(ownFixture, file));
        var executable = Path.Combine(ownFixture, "ClassIsland.Desktop.exe");
        File.Copy(Path.Combine(binaries, "OnboardingHost.exe"), executable);
        var plan = ClassIslandImporter.Inspect(_exe);
        ClassIslandImporter.Apply(plan, new(), _target);
        Process? host = null;
        try
        {
            var message = await ClassIslandSourceCleanup.RecycleAsync(plan, _target, CancellationToken.None, stage =>
            {
                if (stage != ClassIslandCleanupStage.VerifyingSource) return;
                host = Process.Start(new ProcessStartInfo(executable)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
                    ArgumentList = { Path.Combine(_temp.FullName, "host-settings", "Settings.json") }
                });
            });
            Assert.Contains("退出原程序", message);
            Assert.IsTrue(File.Exists(_exe)); Assert.IsTrue(File.Exists(Path.Combine(_target, "Settings.json")));
        }
        finally
        {
            if (host is not null)
            {
                using (host) { host.StandardInput.Close(); await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
            }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SourceEditedAfterThePackageScanStaysInPlace(bool addProfile)
    {
        if (!OperatingSystem.IsWindows()) return;
        var plan = ClassIslandImporter.Inspect(_exe);
        ClassIslandImporter.Apply(plan, new(), _target);
        var target = File.ReadAllBytes(Path.Combine(_target, "Settings.json"));
        var changed = addProfile ? Path.Combine(Directory.CreateDirectory(Path.Combine(_data, "Profiles")).FullName, "New.json") : Path.Combine(_data, "Settings.json");
        var stages = new List<ClassIslandCleanupStage>();
        var message = await ClassIslandSourceCleanup.RecycleAsync(plan, _target, CancellationToken.None, stage =>
        {
            stages.Add(stage);
            if (stage == ClassIslandCleanupStage.VerifyingSource) File.WriteAllText(changed, "{\"edited\":true}");
        });
        CollectionAssert.AreEqual(new[] { ClassIslandCleanupStage.CheckingPackage, ClassIslandCleanupStage.VerifyingSource }, stages);
        Assert.Contains("已保留原目录", message);
        Assert.AreEqual("{\"edited\":true}", File.ReadAllText(changed));
        CollectionAssert.AreEqual(target, File.ReadAllBytes(Path.Combine(_target, "Settings.json")));
    }

    [TestMethod]
    [DataRow("CheckingPackage")]
    [DataRow("VerifyingSource")]
    [DataRow("Recycling")]
    public async Task CancellationUpToTheShellHandoffKeepsBothSides(string atStage)
    {
        if (!OperatingSystem.IsWindows()) return;
        var stopAt = Enum.Parse<ClassIslandCleanupStage>(atStage);
        using var cancel = new CancellationTokenSource();
        var plan = ClassIslandImporter.Inspect(_exe);
        ClassIslandImporter.Apply(plan, new(), _target);
        var target = File.ReadAllBytes(Path.Combine(_target, "Settings.json"));
        var message = await ClassIslandSourceCleanup.RecycleAsync(plan, _target, cancel.Token, stage =>
        {
            if (stage == stopAt) cancel.Cancel();
        });
        Assert.IsTrue(cancel.IsCancellationRequested);
        Assert.Contains("已保留原目录", message);
        Assert.AreEqual("{}", File.ReadAllText(Path.Combine(_data, "Settings.json")));
        CollectionAssert.AreEqual(target, File.ReadAllBytes(Path.Combine(_target, "Settings.json")));
    }
}
