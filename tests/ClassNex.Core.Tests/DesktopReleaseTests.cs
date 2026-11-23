using ClassNex.WinUI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassNex.Core.Tests;

[TestClass]
public sealed class DesktopReleaseTests
{
    [TestMethod]
    [DataRow("25w52b", null, "仓库还没有发布记录。")]
    [DataRow("25w52b", "25w52b_Alpha", "当前版本已与发布页一致（25w52b_Alpha）。")]
    [DataRow("25w52b", "25w52b", "当前版本已与发布页一致（25w52b）。")]
    [DataRow("25w52b", "26w01a_Alpha", "有可用更新：26w01a_Alpha。")]
    [DataRow("25w52b", "25w52a_Alpha", "本机版本 25w52b 比发布页版本 25w52a_Alpha 更新。")]
    [DataRow("25w52b", "25.0Beta1", "发布页版本：25.0Beta1。")]
    [DataRow("development", "26w01a_Alpha", "发布页版本：26w01a_Alpha。")]
    public void ReportsPublishedSnapshotWithoutRecommendingADowngrade(string installed, string? tag, string expected) =>
        Assert.AreEqual(expected, DesktopRelease.Describe(installed, tag));
}
