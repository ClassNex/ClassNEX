using System.Text.Json;
using ClassNex.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassNex.Core.Tests;

[TestClass]
public sealed class ManagementPayloadTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(99)]
    public void OldTrayActionsCannotOverrideOrBeSentToOtherClients(int previous)
    {
        var local = JsonSerializer.Deserialize<AppSettings>($$"""{"TrayClickBehavior":{{previous}},"IsMainWindowVisible":false}""")!;
        var remote = JsonSerializer.Deserialize<AppSettings>($$"""{"trayClickBehavior":{{previous}}}""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        local.TrayClickBehavior = previous;
        Assert.AreEqual(3, local.TrayClickBehavior);
        Assert.AreEqual(3, remote.TrayClickBehavior);
        Assert.IsFalse(local.IsMainWindowVisible);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(local));
        using var wire = JsonDocument.Parse(CentralSettingsPayload.Serialize(local));
        Assert.IsFalse(json.RootElement.TryGetProperty("TrayClickBehavior", out _));
        Assert.IsFalse(wire.RootElement.TryGetProperty("trayClickBehavior", out _));
    }

    [TestMethod]
    public void UploadIncludesClassroomPreferencesButNotDeviceCredentials()
    {
        var data = new AppSettings { ThemeMode = "dark", NexApiKey = "fixture-local-only", TimetablePath = "private-path", NexSystemPrompt = "private instruction" };
        data.CentralManagement.PairingCode = "ABCD-EFGH";
        using var json = JsonDocument.Parse(CentralSettingsPayload.Serialize(data));
        Assert.AreEqual("dark", json.RootElement.GetProperty("themeMode").GetString());
        foreach (var property in new[] { "nexApiKey", "nexPermission", "nexSystemPrompt", "centralManagement", "timetablePath", "userName", "email" })
            Assert.IsFalse(json.RootElement.TryGetProperty(property, out _), property);
        Assert.DoesNotContain("ABCD-EFGH", JsonSerializer.Serialize(data));
    }

    [TestMethod]
    public void DownloadPreservesIdentityWhileApplyingThemeAndWidgets()
    {
        var local = new AppSettings { UserName = "fixture", NexApiKey = "local", TimetablePath = "portable.yaml", IsOobeCompleted = true };
        var remote = new AppSettings { UserName = "remote", ThemeMode = "dark", NexPermission = "admin", Widgets = AppSettings.CreateDefaultWidgets() };
        CentralSettingsPayload.PreserveLocal(remote, local);
        Assert.AreEqual("dark", remote.ThemeMode);
        Assert.HasCount(3, remote.Widgets);
        Assert.AreEqual(local.UserName, remote.UserName);
        Assert.AreEqual(local.NexApiKey, remote.NexApiKey);
        Assert.AreEqual(local.NexPermission, remote.NexPermission);
        Assert.AreEqual(local.TimetablePath, remote.TimetablePath);
        Assert.IsTrue(remote.IsOobeCompleted);
        Assert.AreSame(local.CentralManagement, remote.CentralManagement);
    }

    [TestMethod]
    public void PartialRevisionsChangeOnlySuppliedClassroomFields()
    {
        var local = new AppSettings { FontScale = 1.25, NexSystemPrompt = "local", ThemeMode = "dark" };
        local.Notification.IsAfterSchoolNotificationEnabled = true;
        var patch = """{"THEMEMODE":"light","Notification":{"SoundVolume":22},"NexSystemPrompt":"discard","centralManagement":{"isEnabled":true},"unknownPreference":"unknown"}""";
        using var wire = JsonDocument.Parse(CentralSettingsPayload.FilterIncoming(patch));
        Assert.HasCount(2, wire.RootElement.EnumerateObject().ToArray());
        var merged = CentralSettingsPayload.Merge(CentralSettingsPayload.Serialize(local), patch);
        CentralSettingsPayload.PreserveLocal(merged, local);
        Assert.AreEqual("light", merged.ThemeMode);
        Assert.AreEqual(22, merged.Notification.SoundVolume);
        Assert.IsTrue(merged.Notification.IsAfterSchoolNotificationEnabled);
        Assert.AreEqual(1.25, merged.FontScale);
        Assert.AreEqual("local", merged.NexSystemPrompt);
        Assert.IsFalse(merged.CentralManagement.IsEnabled);
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("{\"fontScale\":[]}")]
    public void MalformedClassroomPreferencesAreNotStored(string json) =>
        Assert.Throws<JsonException>(() => CentralSettingsPayload.FilterIncoming(json));
}
