using ClassNex.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace ClassNex.Central.Tests;

[TestClass]
public sealed class PairingStoreTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private static readonly CentralPairingStart Info = new("Room A", "classroom", "fixture");
    private static readonly CentralUser Owner = new("owner-a", "A", "", "a");
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("classnex-test-pair-");

    [TestCleanup]
    public void Finish()
    {
        foreach (var name in new[] { "central-state.json", "central-state.json.tmp" }) File.Delete(Path.Combine(_folder.FullName, name));
        _folder.Delete();
    }

    [TestMethod]
    public async Task CodeExpiresExactlyAtFiveMinutes()
    {
        var store = new CentralStore(_folder.FullName);
        var ticket = (await store.StartPairingAsync(Info, "local", Start))!;
        Assert.AreEqual(Start.AddMinutes(5), ticket.ExpiresAt);
        Assert.AreEqual("pending", (await store.DeviceIdentityAsync(ticket.ClientId, ticket.DeviceToken, ticket.ExpiresAt.AddTicks(-1))).Status?.State);
        Assert.IsNull((await store.DeviceIdentityAsync(ticket.ClientId, ticket.DeviceToken, ticket.ExpiresAt)).Status);
        Assert.AreEqual(404, (await store.ClaimAsync(Owner, new(ticket.Code), "local", ticket.ExpiresAt)).Status);
        Assert.IsEmpty(await store.ListDevicesAsync(Owner.Subject));
    }

    [TestMethod]
    public async Task FailedClaimsAreBoundedAndQuotaReopensWithoutWaiting()
    {
        var store = new CentralStore(_folder.FullName);
        for (var i = 0; i < 10; i++) Assert.AreEqual(404, (await store.ClaimAsync(Owner, new("not-a-code"), "fixture", Start)).Status);
        Assert.AreEqual(429, (await store.ClaimAsync(Owner, new("not-a-code"), "fixture", Start.AddMinutes(4))).Status);
        Assert.AreEqual(404, (await store.ClaimAsync(Owner with { Subject = "other" }, new("not-a-code"), "fixture", Start.AddMinutes(4))).Status);
        Assert.AreEqual(404, (await store.ClaimAsync(Owner, new("not-a-code"), "fixture", Start.AddMinutes(5))).Status);
    }

    [TestMethod]
    public async Task OnlyClaimedBindingsSurviveRestartAndRevocationIsDurable()
    {
        var store = new CentralStore(_folder.FullName);
        var t = (await store.StartPairingAsync(Info, "local", Start))!;
        var other = (await store.StartPairingAsync(Info, "local", Start))!;
        var (status, link) = await store.ClaimAsync(Owner, new(t.Code), "local", Start);
        Assert.AreEqual(200, status);
        Assert.IsNotNull(link);
        var state = await File.ReadAllTextAsync(Path.Combine(_folder.FullName, "central-state.json"));
        Assert.DoesNotContain(t.DeviceToken, state);
        Assert.DoesNotContain(t.Code, state);
        store = new(_folder.FullName); await store.InitializeAsync();
        Assert.AreEqual(Owner.Subject, (await store.DeviceIdentityAsync(t.ClientId, t.DeviceToken, Start.AddDays(20))).Owner);
        Assert.IsNull((await store.DeviceIdentityAsync(other.ClientId, other.DeviceToken, Start)).Status);
        Assert.IsFalse(await store.RevokeAsync(t.ClientId, "other"));
        Assert.IsTrue(await store.RevokeAsync(t.ClientId, Owner.Subject));
        store = new(_folder.FullName); await store.InitializeAsync();
        Assert.IsNull((await store.DeviceIdentityAsync(t.ClientId, t.DeviceToken, Start)).Status);
        Assert.IsNotNull(await store.GetClassAsync(link.ClassId, Owner.Subject));
    }

    [TestMethod]
    public async Task ConcurrentClaimsProduceExactlyOneOwnerAndClass()
    {
        var store = new CentralStore(_folder.FullName);
        var ticket = (await store.StartPairingAsync(Info, "local", Start))!;
        var owners = new[] { Owner, Owner with { Subject = "other" } };
        var results = await Task.WhenAll(owners.Select(o => store.ClaimAsync(o, new(ticket.Code), "local", Start)));
        CollectionAssert.AreEquivalent(new[] { 200, 404 }, results.Select(r => r.Status).ToArray());
        var owner = (await store.DeviceIdentityAsync(ticket.ClientId, ticket.DeviceToken, Start)).Owner;
        Assert.HasCount(1, await store.ListClassesAsync(owner!));
        Assert.IsEmpty(await store.ListClassesAsync(owners.Single(o => o.Subject != owner).Subject));
    }

    [TestMethod]
    public async Task SeparateResourceWritesNeverReplaceEachOthersRevisions()
    {
        var store = new CentralStore(_folder.FullName);
        var c = await store.CreateClassAsync(Owner, new("Room"));
        const string yaml = "version: 1\nsubjects:\n  - name: Math\nschedules: []\n";
        await Task.WhenAll(store.UpdateClassAsync(c.ClassId, Owner.Subject, "{\"themeMode\":\"dark\"}", null, "theme"),
            store.UpdateClassAsync(c.ClassId, Owner.Subject, null, yaml, "course"));
        var saved = (await store.GetClassAsync(c.ClassId, Owner.Subject))!;
        Assert.AreEqual("{\"themeMode\":\"dark\"}", saved.SettingsJson);
        Assert.AreEqual(yaml, saved.TimetableYaml);
        Assert.AreEqual(c.Revision + 2, saved.Revision);
    }

    [TestMethod]
    public async Task OlderClassFilesDoNotExposePrivateDevicePreferences()
    {
        var path = Path.Combine(_folder.FullName, "central-state.json");
        var legacy = new { classes = new Dictionary<string, object> { ["room"] = new { classId = "room", ownerSubject = Owner.Subject,
            settingsJson = "{\"themeMode\":\"dark\",\"email\":\"local@example.invalid\",\"centralManagement\":{}}" } } };
        await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(legacy));
        var store = new CentralStore(_folder.FullName); await store.InitializeAsync();
        Assert.AreEqual("{\"themeMode\":\"dark\"}", (await store.GetClassAsync("room", Owner.Subject))!.SettingsJson);
        Assert.Contains("local@example.invalid", await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task DesktopAndBrowserMergeOnlyIndependentResourcesAndSurviveRestart()
    {
        var store = new CentralStore(_folder.FullName);
        var ticket = (await store.StartPairingAsync(Info, "local", Start))!;
        var (_, device) = await store.ClaimAsync(Owner, new(ticket.Code), "local", Start);
        var id = device!.ClassId;
        const string settings = "{\"themeMode\":\"light\"}";
        const string yaml = "version: 1\nsubjects:\n  - name: History\nschedules: []\n";
        var edit = new CentralResourceEdit(ticket.ClientId, id, 0, settings, null);
        await Task.WhenAll(store.UpdateClassAsync(id, Owner.Subject, null, yaml, "网页", 0), store.PublishEditAsync(Owner.Subject, edit));
        var result = (await store.GetClassAsync(id, Owner.Subject))!;
        Assert.AreEqual(2L, result.Revision);
        Assert.AreEqual(settings, result.SettingsJson); Assert.AreEqual(yaml, result.TimetableYaml);

        store = new(_folder.FullName); await store.InitializeAsync();
        var duplicate = await store.PublishEditAsync(Owner.Subject, edit);
        Assert.AreEqual(result.Revision, duplicate!.Revision);
        await Assert.ThrowsAsync<CentralEditConflict>(() => store.PublishEditAsync(Owner.Subject, edit with { SettingsJson = "{}" }));
        await Assert.ThrowsAsync<CentralEditConflict>(() => store.UpdateClassAsync(id, Owner.Subject, null, yaml + "# stale\n", "", 0));
        await Assert.ThrowsAsync<CentralEditConflict>(() => store.UpdateClassAsync(id, Owner.Subject, "{}", yaml + "# must not apply\n", "", 0));
        await Assert.ThrowsAsync<CentralEditConflict>(() => store.PublishEditAsync(Owner.Subject, edit with { BaseRevision = 10 }));
        await Assert.ThrowsAsync<ArgumentException>(() => store.PublishEditAsync(Owner.Subject, edit with { TimetableYaml = yaml }));
        Assert.IsNull(await store.PublishEditAsync("different-owner", edit));
        var next = await store.CreateClassAsync(Owner, new("另一间教室"));
        await store.AssignDeviceClassAsync(ticket.ClientId, Owner.Subject, next.ClassId);
        await Assert.ThrowsAsync<CentralEditConflict>(() => store.PublishEditAsync(Owner.Subject, edit));
        Assert.AreEqual(0L, (await store.GetClassAsync(next.ClassId, Owner.Subject))!.Revision);
        var unchanged = (await store.GetClassAsync(id, Owner.Subject))!;
        Assert.AreEqual(result.Revision, unchanged.Revision); Assert.AreEqual(settings, unchanged.SettingsJson); Assert.AreEqual(yaml, unchanged.TimetableYaml);
    }

    [TestMethod]
    public async Task LegacySeedAndLegacyPublishInvalidateOnlyTheResourceTheyModify()
    {
        var store = new CentralStore(_folder.FullName);
        var t = (await store.StartPairingAsync(Info, "local", Start))!;
        var (_, device) = await store.ClaimAsync(Owner, new(t.Code), "local", Start);
        var id = device!.ClassId;
        const string yaml = "version: 1\nsubjects:\n  - name: Math\nschedules: []\n";
        await store.ReportDeviceAsync(Owner.Subject, new(t.ClientId, "fixture", "room", "{}", yaml, null));
        var seed = (await store.GetClassAsync(id, Owner.Subject))!;
        var stale = new CentralResourceEdit(t.ClientId, id, 0, "{\"themeMode\":\"light\"}", null);
        await Assert.ThrowsAsync<CentralEditConflict>(() => store.PublishEditAsync(Owner.Subject, stale));
        await Assert.ThrowsAsync<CentralEditConflict>(() => store.PublishEditAsync(Owner.Subject, stale with { SettingsJson = null, TimetableYaml = yaml + "# old\n" }));
        await store.PublishDeviceConfigAsync(Owner.Subject, new(t.ClientId, "{}", yaml + "# newer\n"));
        await store.PublishEditAsync(Owner.Subject, stale with { BaseRevision = seed.Revision });
        await Assert.ThrowsAsync<CentralEditConflict>(() => store.UpdateClassAsync(id, Owner.Subject, null, yaml, "", seed.Revision));
        await store.ReportDeviceAsync(Owner.Subject, new(t.ClientId, "fixture", "room", "{}", yaml, null));
        var current = (await store.GetClassAsync(id, Owner.Subject))!;
        Assert.AreEqual(yaml + "# newer\n", current.TimetableYaml); Assert.AreEqual(stale.SettingsJson, current.SettingsJson);
    }

    [TestMethod]
    public async Task MissingPerResourceRevisionInLegacyFileRequiresCurrentBaseline()
    {
        var path = Path.Combine(_folder.FullName, "central-state.json");
        await File.WriteAllTextAsync(path, """{"classes":{"room":{"classId":"room","ownerSubject":"owner-a","revision":7,"settingsJson":"{}","timetableYaml":""}}}""");
        var store = new CentralStore(_folder.FullName); await store.InitializeAsync();
        const string update = "{\"themeMode\":\"dark\"}";
        await Assert.ThrowsAsync<CentralEditConflict>(() => store.UpdateClassAsync("room", Owner.Subject, update, null, "", 6));
        var saved = await store.UpdateClassAsync("room", Owner.Subject, update, null, "", 7);
        Assert.AreEqual(8L, saved!.Revision);
    }

    [TestMethod]
    public async Task NewClassNeverSendsEmptyScaffoldingOverTheFirstRealTimetable()
    {
        var store = new CentralStore(_folder.FullName);
        var ticket = (await store.StartPairingAsync(Info, "local", Start))!;
        var (_, device) = await store.ClaimAsync(Owner, new(ticket.Code), "local", Start);
        var id = device!.ClassId;
        const string empty = "version: 1\nsubjects: []\nschedules: []\n";
        const string real = "version: 1\nsubjects:\n  - name: Math\nschedules: []\n";
        await store.UpdateClassAsync(id, Owner.Subject, "{\"themeMode\":\"dark\"}", empty, "form defaults");
        Assert.AreEqual("", (await store.GetClientAssignmentAsync(ticket.ClientId, Owner.Subject))!.ClassRecord.TimetableYaml);
        await store.ReportDeviceAsync(Owner.Subject, new(ticket.ClientId, "test", "fixture", "{}", real, null));
        Assert.AreEqual(real, (await store.GetClassAsync(id, Owner.Subject))!.TimetableYaml);
        await store.ReportDeviceAsync(Owner.Subject, new(ticket.ClientId, "test", "fixture", "{}", empty, null));
        Assert.AreEqual(real, (await store.GetClassAsync(id, Owner.Subject))!.TimetableYaml);
        await store.UpdateClassAsync(id, Owner.Subject, null, empty, "intentional later change");
        Assert.AreEqual(empty, (await store.GetClassAsync(id, Owner.Subject))!.TimetableYaml);
    }
}
