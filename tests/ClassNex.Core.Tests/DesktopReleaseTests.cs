using System.Net;
using System.Text.Json;
using ClassNex.WinUI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassNex.Core.Tests;

[TestClass]
public sealed class DesktopReleaseTests
{
    [TestMethod]
    [DataRow("[]", null)]
    [DataRow("[{\"tag_name\":\"26w48a_Alpha\",\"prerelease\":true}]", "26w48a_Alpha")]
    public async Task ReadsPublicReleaseMetadataIncludingClassroomPreviews(string payload, string? expected)
    {
        using var client = new HttpClient(new ReleaseReply(HttpStatusCode.OK, payload));
        Assert.AreEqual(expected, await DesktopRelease.LatestTagAsync(client, "26w48b", CancellationToken.None));
    }

    [TestMethod]
    public async Task DistinguishesRequestAndResponseErrorsFromAnEmptyReleaseList()
    {
        using var offline = new HttpClient(new ReleaseReply(HttpStatusCode.ServiceUnavailable, "[]"));
        using var missingTag = new HttpClient(new ReleaseReply(HttpStatusCode.OK, "[{}]"));
        await Assert.ThrowsAsync<HttpRequestException>(() => DesktopRelease.LatestTagAsync(offline, "", CancellationToken.None));
        await Assert.ThrowsAsync<JsonException>(() => DesktopRelease.LatestTagAsync(missingTag, "", CancellationToken.None));
        await Assert.ThrowsAsync<OperationCanceledException>(() => DesktopRelease.LatestTagAsync(offline, "", new CancellationToken(true)));
    }

    private sealed class ReleaseReply(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual("https://api.github.com/repos/ClassNex/ClassNEX/releases?per_page=1", request.RequestUri?.AbsoluteUri);
            Assert.IsNotEmpty(request.Headers.UserAgent);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

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
