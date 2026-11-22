using System.Text.Json;
using ClassNex.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassNex.Core.Tests;

[TestClass]
public sealed class ManagementPayloadTests
{
    [TestMethod]
    public void UploadIncludesClassroomPreferencesButNotDeviceCredentials()
    {
        var data = new AppSettings { ThemeMode = "dark", NexApiKey = "fixture-local-only", TimetablePath = "private-path" };
        using var json = JsonDocument.Parse(CentralSettingsPayload.Serialize(data));
        Assert.AreEqual("dark", json.RootElement.GetProperty("themeMode").GetString());
        foreach (var property in new[] { "nexApiKey", "nexPermission", "centralManagement", "timetablePath", "userName", "email" })
            Assert.IsFalse(json.RootElement.TryGetProperty(property, out _), property);
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
}
