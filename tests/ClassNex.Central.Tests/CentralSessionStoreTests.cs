using System.Diagnostics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassNex.Central.Tests;

[TestClass]
public sealed class CentralSessionStoreTests
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("classnex-session-test-");

    [TestCleanup]
    public void CleanUp()
    {
        foreach (var file in _folder.EnumerateFiles()) file.Delete();
        _folder.Delete();
    }

    [TestMethod]
    public async Task BrowserAccountPreconditionAllowsRenewalButNotAccountOrganizationOrIssuerChanges()
    {
        using var auth = new CentralAuthService("https://accounts.example.invalid", "http://identity", "client", "", "http://localhost/Control");
        using var otherIssuer = new CentralAuthService("https://different.example.invalid", "http://identity", "client", "", "http://localhost/Control");
        var store = new CentralSessionStore(_folder.FullName);
        var owner = new CentralUser("teacher-1", "Teacher", "teacher@example.invalid", "teacher") { Organization = "school-1" };
        var renamed = owner with { Name = "Renamed", Email = "new@example.invalid" };
        var changed = owner with { Subject = "teacher-2" };
        var otherSchool = owner with { Organization = "school-2" };
        var future = DateTimeOffset.UtcNow.AddHours(1);
        await store.SetAsync("initial", owner, future);
        await store.SetAsync("renewed", renamed, future);
        await store.SetAsync("changed", changed, future);
        await store.SetAsync("other-school", otherSchool, future);

        var initial = (await store.GetAsync("initial"))!;
        var key = auth.BrowserAccountKey(initial);
        Assert.IsTrue(auth.MatchesBrowserAccount((await store.GetAsync("renewed"))!, key));
        Assert.IsFalse(auth.MatchesBrowserAccount((await store.GetAsync("changed"))!, key));
        Assert.IsFalse(auth.MatchesBrowserAccount((await store.GetAsync("other-school"))!, key));
        Assert.IsFalse(auth.MatchesBrowserAccount(owner with { Issuer = "https://accounts.example.invalid/orgs/other" }, key));
        Assert.IsTrue(auth.MatchesBrowserAccount(owner with { Issuer = auth.CasdoorUrl }, key));
        Assert.IsTrue(auth.MatchesBrowserAccount(owner with { Issuer = auth.CasdoorInternalUrl }, key));
        Assert.IsFalse(otherIssuer.MatchesBrowserAccount(initial, key));
        Assert.IsFalse(auth.MatchesBrowserAccount(initial, ""));
        Assert.IsTrue(auth.MatchesBrowserAccount(initial, null), "Older request formats still use the actual logged-in account and normal authorization.");
    }

    [TestMethod, DoNotParallelize]
    public async Task RestartPrunesOnlyExpiredSessionsAndKeepsTheStoreWritableWithoutLoggingCredentials()
    {
        var active = Guid.NewGuid().ToString("N");
        var expired = Guid.NewGuid().ToString("N");
        var added = Guid.NewGuid().ToString("N");
        var user = new CentralUser("classroom", "Classroom", "teacher@example.invalid", "teacher");
        var secondUser = user with { Subject = "second-classroom" };
        var now = DateTimeOffset.UtcNow;
        using var output = new StringWriter();
        using var listener = new TextWriterTraceListener(output);
        var stdout = Console.Out;
        var stderr = Console.Error;
        try
        {
            Console.SetOut(output);
            Console.SetError(output);
            Trace.Listeners.Add(listener);
            var store = new CentralSessionStore(_folder.FullName);
            await store.SetAsync(active, user, now.AddDays(1));
            await store.SetAsync(expired, secondUser, now.AddDays(-1));
            Assert.IsTrue((await ReadPersistedKeysAsync()).SetEquals([active, expired]), "Both sessions must exist before restart.");

            var restarted = new CentralSessionStore(_folder.FullName);
            await restarted.InitializeAsync();
            Assert.IsTrue((await ReadPersistedKeysAsync()).SetEquals([active]), "Startup must persist the cleanup before any lookup.");
            Assert.AreEqual(user, await restarted.GetAsync(active));
            Assert.IsNull(await restarted.GetAsync(expired));

            await restarted.SetAsync(added, secondUser, now.AddDays(2));
            await restarted.RemoveAsync(active);
            var reloaded = new CentralSessionStore(_folder.FullName);
            await reloaded.InitializeAsync();
            Assert.AreEqual(secondUser, await reloaded.GetAsync(added));
            Assert.IsNull(await reloaded.GetAsync(active));
            Assert.IsNull(await reloaded.GetAsync(expired));
            Assert.IsTrue((await ReadPersistedKeysAsync()).SetEquals([added]), "Post-restart changes must survive another restart.");
            CollectionAssert.AreEqual(new[] { "central-sessions.json" }, _folder.GetFiles().Select(file => file.Name).ToArray());
        }
        finally
        {
            Trace.Listeners.Remove(listener);
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }

        listener.Flush();
        var logs = output.ToString();
        Assert.IsFalse(new[] { active, expired, added }.Any(token => logs.Contains(token, StringComparison.Ordinal)),
            "Session credentials must not appear in console or trace output.");
    }

    private async Task<HashSet<string>> ReadPersistedKeysAsync()
    {
        var path = Path.Combine(_folder.FullName, "central-sessions.json");
        using var json = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
        return json.RootElement.EnumerateObject().Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
    }
}
