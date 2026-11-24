using System.Diagnostics;
using System.Text.Json;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.TestFixtures;

namespace ClassNex.Core.Tests;

[TestClass]
public sealed class OnboardingOwnershipTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private DirectoryInfo _directory = null!;
    private string SettingsPath => Path.Combine(_directory.FullName, "Settings.json");

    [TestInitialize] public void CreateDirectory() => _directory = Directory.CreateTempSubdirectory("ClassNex-oobe-lock-");
    [TestCleanup] public void RemoveFixture() => _directory.Delete(true);

    private Process Start()
    {
        var assembly = typeof(OnboardingOwnershipTests).Assembly.Location;
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            ArgumentList = { "exec", "--runtimeconfig", Path.ChangeExtension(assembly, ".runtimeconfig.json"), "--depsfile", Path.ChangeExtension(assembly, ".deps.json"), typeof(OnboardingHost).Assembly.Location, SettingsPath },
        };
        return Process.Start(info) ?? throw new InvalidOperationException("Could not start the isolated first-run process.");
    }

    private static Task<string?> Read(Process process) => process.StandardOutput.ReadLineAsync().WaitAsync(Timeout);

    private static async Task Stop(Process child)
    {
        if (!child.HasExited) child.Kill();
        await child.WaitForExitAsync().WaitAsync(Timeout);
    }

    [TestMethod]
    public async Task CompetingSetupsCannotWriteAndTheOperatingSystemReleasesACrashedOwner()
    {
        using var holder = Start();
        try
        {
            Assert.AreEqual("setup", await Read(holder));
            const string original = """{"AccountEndpoint":"https://auth.smart-teach.cn","ThemeMode":"dark","IsOobeCompleted":false}""";
            await File.WriteAllTextAsync(SettingsPath, original);
            using var competing = Start();
            try
            {
                Assert.AreEqual("busy", await Read(competing));
                await competing.WaitForExitAsync().WaitAsync(Timeout);
                Assert.AreEqual(0, competing.ExitCode);
                Assert.AreEqual(original, await File.ReadAllTextAsync(SettingsPath));
            }
            finally { await Stop(competing); }
            var other = new SettingsStore(Path.Combine(_directory.FullName, "other", "Settings.json"));
            Assert.IsTrue(other.TryLoadForStartup(out var independent, out var lease));
            using (lease) { Assert.IsNotNull(lease); Assert.IsFalse(independent.IsOobeCompleted); }
        }
        finally { await Stop(holder); }
        var recovered = new SettingsStore(SettingsPath);
        var release = Stopwatch.StartNew();
        AppSettings restored;
        IDisposable? next;
        while (!recovered.TryLoadForStartup(out restored, out next))
        {
            Assert.IsTrue(release.Elapsed < TimeSpan.FromSeconds(3), "The OS did not release the dead process's handle.");
            await Task.Delay(25);
        }
        using (next) { Assert.IsNotNull(next); Assert.AreEqual("dark", restored.ThemeMode); Assert.AreEqual(AppSettings.DefaultAccountEndpoint, restored.AccountEndpoint); }
    }

    [TestMethod]
    public async Task RestartSeesCommittedSettingsBeforeTheOldProcessExits()
    {
        using var holder = Start();
        try
        {
            Assert.AreEqual("setup", await Read(holder));
            await holder.StandardInput.WriteLineAsync("finish");
            Assert.AreEqual("saved", await Read(holder));
            using var restarted = Start();
            try
            {
                Assert.AreEqual("desktop", await Read(restarted));
                Assert.IsFalse(holder.HasExited);
                var store = new SettingsStore(SettingsPath);
                Assert.IsTrue(store.TryLoadForStartup(out var saved, out var lease));
                using (lease) { Assert.IsNull(lease); Assert.AreEqual("en-US", saved.Language); }
                await restarted.StandardInput.WriteLineAsync("close");
                await restarted.WaitForExitAsync().WaitAsync(Timeout);
                Assert.AreEqual(0, restarted.ExitCode);
            }
            finally { await Stop(restarted); }
            await holder.StandardInput.WriteLineAsync("close");
            await holder.WaitForExitAsync().WaitAsync(Timeout);
            Assert.AreEqual(0, holder.ExitCode);
        }
        finally { await Stop(holder); }
    }

    [TestMethod]
    public void NormalStartupDoesNotRequireAnOwnershipFileAndInvalidSettingsAreNotReplaced()
    {
        var store = new SettingsStore(SettingsPath);
        store.Save(new AppSettings { IsOobeCompleted = true });
        Assert.IsTrue(store.TryLoadForStartup(out var ready, out var lease));
        using (lease) { Assert.IsNull(lease); Assert.IsTrue(ready.IsOobeCompleted); }
        CollectionAssert.AreEqual(new[] { "Settings.json" }, _directory.GetFiles().Select(f => f.Name).ToArray());
        File.WriteAllText(SettingsPath, """{"IsOobeCompleted":true,"UserName":"旧配置"}""", System.Text.Encoding.Unicode);
        Assert.IsTrue(store.TryLoadForStartup(out var legacy, out var legacyLease));
        using (legacyLease) { Assert.IsNull(legacyLease); Assert.AreEqual("旧配置", legacy.UserName); }
        File.WriteAllText(SettingsPath, "unfinished json");
        Assert.Throws<JsonException>(() => store.TryLoadForStartup(out _, out _));
        Assert.AreEqual("unfinished json", File.ReadAllText(SettingsPath));
    }
}
