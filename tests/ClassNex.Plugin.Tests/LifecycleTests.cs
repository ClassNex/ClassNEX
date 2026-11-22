using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using ClassNex.Core.Abstractions;
using ClassNex.Services;
using ClassNex.Services.NexIntelligence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace ClassNex.Plugin.Tests;

[TestClass]
public sealed class LifecycleTests
{
    private sealed class Host : INotificationService, IAiService
    {
        internal int Shown;
        public void DebugTriggerTestNotification() => Shown++;
        public INexAgent CreateAgent(NexPermission permission) => throw new NotSupportedException();
        public void Start() { }
        public void Stop() { }
        public void DebugTriggerClassPreparing() { }
        public void DebugTriggerClassOn() { }
        public void DebugTriggerClassOff() { }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("classnex-plugin-test-");
        internal readonly Host Host = new();
        internal PluginService Manager { get; }
        internal string Config => Path.Combine(_dir.FullName, "state");
        internal string Root => Path.Combine(_dir.FullName, "plugins");
        internal Fixture() => Manager = NewManager();
        internal PluginService NewManager() => new(Root, new PluginServiceProvider(), Host, Host, Config);
        internal string[] Trail(string id) => File.ReadAllLines(Path.Combine(Config, "plugins", id, "lifecycle.txt"));
        internal void Install(string id, string folder = "", int api = 1, string? dependency = null,
            string minimum = "1.0", string entry = "Entry", string[]? permissions = null, string version = "1.0", bool enabled = true)
        {
            var dir = Directory.CreateDirectory(Path.Combine(Root, folder == "" ? id : folder)).FullName;
            var fixture = Path.Combine(AppContext.BaseDirectory, "fixture", "ClassNex.SamplePlugin.dll");
            File.Copy(fixture, Path.Combine(dir, Path.GetFileName(fixture)));
            var m = new PluginManifest(id, id, version, Path.GetFileName(fixture), "ClassNex.SamplePlugin." + entry, enabled)
            {
                ApiVersion = api, Permissions = permissions, Dependencies = dependency is null ? [] : new() { [dependency] = minimum }
            };
            File.WriteAllText(Path.Combine(dir, "plugin.json"), JsonSerializer.Serialize(m));
        }
        public void Dispose() { Manager.StopAll(); _dir.Delete(true); }
    }

    [TestMethod]
    public async Task LoadsInDependencyOrderAndPersistsDisableAcrossRestart()
    {
        using var f = new Fixture();
        f.Install("extra", "aaa", dependency: "base");
        f.Install("base", "zzz", permissions: ["notifications"]);
        await f.Manager.LoadAllAsync();
        Assert.AreEqual(2, f.Host.Shown);
        CollectionAssert.AreEquivalent(new[] { "已加载", "已加载" }, f.Manager.LoadedPlugins.Select(p => p.Status).ToArray());
        await f.Manager.SetEnabledAsync("base", false);
        Assert.AreEqual("已禁用", f.Manager.LoadedPlugins.Single(p => p.Id == "base").Status);
        StringAssert.Contains(f.Manager.LoadedPlugins.Single(p => p.Id == "extra").Status, "依赖");
        CollectionAssert.AreEqual(new[] { "init", "stop" }, f.Trail("extra"));
        var afterRestart = f.NewManager();
        await afterRestart.LoadAllAsync();
        Assert.IsFalse(afterRestart.LoadedPlugins.Single(p => p.Id == "base").Enabled);
        afterRestart.StopAll();
    }

    [TestMethod]
    public async Task ChangingOnePluginOnlyRestartsItsDependencyChain()
    {
        using var f = new Fixture();
        f.Install("calendar"); f.Install("dependent", dependency: "calendar");
        f.Install("downstream", dependency: "dependent"); f.Install("independent");
        await f.Manager.LoadAllAsync();
        await f.Manager.SetEnabledAsync("calendar", false);
        CollectionAssert.AreEqual(new[] { "init", "stop" }, f.Trail("downstream"));
        await f.Manager.SetEnabledAsync("calendar", true);
        await f.Manager.SetEnabledAsync("calendar", true);
        foreach (var id in new[] { "calendar", "dependent", "downstream" })
            CollectionAssert.AreEqual(new[] { "init", "stop", "init" }, f.Trail(id));
        CollectionAssert.AreEqual(new[] { "init" }, f.Trail("independent"));
        Assert.IsTrue(f.Manager.LoadedPlugins.All(x => x.Status == "已加载"));
    }

    [TestMethod]
    public async Task InvalidEnablePreferencesDoNotStartCodeAndArePreservedOnRepair()
    {
        using var f = new Fixture();
        f.Install("chosen"); f.Install("other");
        Directory.CreateDirectory(f.Config);
        var path = Path.Combine(f.Config, "enabled.json");
        File.WriteAllText(path, "{incomplete");
        await f.Manager.LoadAllAsync();
        Assert.AreEqual(0, f.Host.Shown);
        Assert.IsTrue(f.Manager.LoadedPlugins.All(x => !x.Enabled && x.Status.Contains("损坏")));
        Assert.AreEqual("{incomplete", File.ReadAllText(path));
        await f.Manager.SetEnabledAsync("chosen", true);
        Assert.AreEqual(1, f.Host.Shown);
        Assert.AreEqual("已禁用", f.Manager.LoadedPlugins.Single(x => x.Id == "other").Status);
        Assert.AreEqual("{incomplete", File.ReadAllText(Directory.GetFiles(f.Config, "enabled.invalid-*.json").Single()));
        var afterRestart = f.NewManager();
        await afterRestart.LoadAllAsync();
        Assert.AreEqual("已加载", afterRestart.LoadedPlugins.Single(x => x.Id == "chosen").Status);
        Assert.IsFalse(afterRestart.LoadedPlugins.Single(x => x.Id == "other").Enabled);
        afterRestart.StopAll();
    }

    [TestMethod]
    public async Task RejectsVersionsMissingDependenciesCyclesAndDuplicateIdsWithoutExecutingThem()
    {
        using var f = new Fixture();
        f.Install("future", api: 99);
        f.Install("missing", dependency: "no-such-id");
        f.Install("a", dependency: "b"); f.Install("b", dependency: "a");
        f.Install("v2", dependency: "old", minimum: "2.0"); f.Install("old");
        f.Install("same", "dup1"); f.Install("same", "dup2");
        await f.Manager.LoadAllAsync();
        Assert.AreEqual(1, f.Host.Shown);
        Assert.IsTrue(f.Manager.LoadedPlugins.Where(p => p.Id != "old").All(p => p.Status != "已加载"));
        Assert.IsTrue(f.Manager.LoadedPlugins.Where(p => p.Id == "same").All(p => !p.CanToggle));
        StringAssert.Contains(f.Manager.LoadedPlugins.Single(p => p.Id == "future").Status, "API");
        StringAssert.Contains(f.Manager.LoadedPlugins.Single(p => p.Id == "v2").Status, "2.0");
    }

    [TestMethod]
    public async Task InitializationErrorsRunShutdownAndUndeclaredServicesAreDenied()
    {
        using var f = new Fixture();
        f.Install("partial", entry: "PartialFailure"); f.Install("denied", permissions: []);
        await f.Manager.LoadAllAsync();
        Assert.AreEqual(1, f.Host.Shown);
        Assert.IsTrue(f.Manager.LoadedPlugins.All(p => p.Status.StartsWith("加载失败")));
        CollectionAssert.AreEqual(new[] { "init", "stop" }, f.Trail("partial"));
        CollectionAssert.AreEqual(new[] { "init", "stop" }, f.Trail("denied"));
    }

    [TestMethod]
    public async Task StoppedPluginAssemblyContextCanActuallyBeCollected()
    {
        using var f = new Fixture();
        f.Install("collectible");
        await f.Manager.LoadAllAsync();
        var weak = CaptureContext(f.Manager.LoadedPlugins.Single().AssemblyPath);
        f.Manager.StopAll();
        for (var i = 0; i < 8 && weak.IsAlive; i++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            await Task.Delay(15);
        }
        Assert.IsFalse(weak.IsAlive, "The host must not keep plugin types, delegates or assemblies after shutdown.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CaptureContext(string name) => new(AssemblyLoadContext.All.Single(a => a.IsCollectible && a.Name == name));
}
