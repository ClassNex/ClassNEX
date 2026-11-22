using System.Text.Json;
using ClassNex.Models;
using ClassNex.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassNex.Core.Tests;

[TestClass]
public sealed class WidgetLayoutTests
{
    [TestMethod]
    public void LegacyCombinedWidgetsSplitAtTheirDisplayedPositionsOnce()
    {
        var dir = Directory.CreateTempSubdirectory("classnex-clock-");
        try
        {
            var path = Path.Combine(dir.FullName, "Settings.json");
            File.WriteAllText(path, """
                {"Widgets":[{"Type":"date","Order":7,"IsEnabled":false,"FontScale":1.6,"UseCustomFontColor":true,"FontColor":"#ABCDEF"},
                    {"Type":"text","Order":2,"Text":"before"},{"Type":"countdown","Order":9},{"Type":"date","Order":10}]}
                """);
            var store = new SettingsStore(path);
            var first = store.Load();
            var expected = new[] { "text", "date", "clock", "countdown", "date", "clock" };
            CollectionAssert.AreEqual(expected, first.Widgets.Select(w => w.Type).ToArray());
            CollectionAssert.AreEqual(Enumerable.Range(0, 6).ToArray(), first.Widgets.Select(w => w.Order).ToArray());
            var clock = first.Widgets[2];
            Assert.IsFalse(clock.IsEnabled); Assert.AreEqual(1.6, clock.FontScale);
            Assert.IsTrue(clock.UseCustomFontColor); Assert.AreEqual("#ABCDEF", clock.FontColor);
            Assert.IsFalse(clock.ShowSeconds); Assert.IsFalse(clock.FlashTimeSeparator);
            Assert.IsTrue(first.Widgets[^1].IsEnabled);
            var saved = File.ReadAllText(path);
            using var document = JsonDocument.Parse(saved);
            Assert.IsGreaterThanOrEqualTo(1, document.RootElement.GetProperty("WidgetLayoutVersion").GetInt32());
            CollectionAssert.AreEqual(expected, store.Load().Widgets.Select(w => w.Type).ToArray());
            Assert.AreEqual(saved, File.ReadAllText(path));
            first.Widgets.RemoveAll(w => w.Type == "clock");
            store.Save(first);
            CollectionAssert.AreEqual(new[] { "text", "date", "countdown", "date" }, store.Load().Widgets.Select(w => w.Type).ToArray());
        }
        finally { dir.Delete(true); }
    }

    [TestMethod]
    [DataRow("[]", "")]
    [DataRow("[{\"type\":\"schedule\"}]", "schedule")]
    [DataRow("[{\"type\":\"clock\"}]", "clock")]
    [DataRow("null", "date,clock,schedule")]
    public void LegacyRemoteLayoutsRespectRemovalAndAbsentDefaults(string widgets, string expected)
    {
        var local = JsonSerializer.Deserialize<AppSettings>("{\"widgets\":" + widgets + "}", JsonSerializerOptions.Web)!;
        Assert.AreEqual(expected, string.Join(',', local.Widgets.Select(w => w.Type)));
        var wire = CentralSettingsPayload.Serialize(local);
        using var json = JsonDocument.Parse(wire);
        Assert.AreEqual(local.WidgetLayoutVersion, json.RootElement.GetProperty("widgetLayoutVersion").GetInt32());
        Assert.AreEqual(expected, string.Join(',', JsonSerializer.Deserialize<AppSettings>(wire, JsonSerializerOptions.Web)!.Widgets.Select(w => w.Type)));
    }

    [TestMethod]
    public void ExplicitClocksRetainTheirOrderAndParametersEvenIfDisabled()
    {
        var json = """{"widgets":[{"type":"clock","order":1,"showSeconds":true,"flashTimeSeparator":true,"isEnabled":false},{"type":"divider","order":2},{"type":"date","order":3}]}""";
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonSerializerOptions.Web)!;
        CollectionAssert.AreEqual(new[] { "clock", "divider", "date" }, settings.Widgets.Select(w => w.Type).ToArray());
        Assert.IsFalse(settings.Widgets[0].IsEnabled); Assert.IsTrue(settings.Widgets[0].ShowSeconds); Assert.IsTrue(settings.Widgets[0].FlashTimeSeparator);
        var noClock = new AppSettings { Widgets = [new() { Type = "date" }] };
        var saved = JsonSerializer.Deserialize<AppSettings>(CentralSettingsPayload.Serialize(noClock), JsonSerializerOptions.Web)!;
        Assert.HasCount(1, saved.Widgets); Assert.AreEqual("date", saved.Widgets[0].Type);
        var missing = JsonSerializer.Deserialize<AppSettings>("{}", JsonSerializerOptions.Web)!;
        CollectionAssert.AreEqual(new[] { "date", "clock", "schedule" }, missing.Widgets.Select(w => w.Type).ToArray());
    }
}
