using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClassNex.WinUI;

internal static class DesktopRelease
{
    internal static async Task<string?> LatestTagAsync(HttpClient client, string installed, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/ClassNex/ClassNEX/releases?per_page=1");
        request.Headers.UserAgent.ParseAdd(string.IsNullOrEmpty(installed) ? "ClassNEX-Desktop" : $"ClassNEX-Desktop/{installed}");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
        var releases = document.RootElement;
        if (releases.ValueKind == JsonValueKind.Array)
        {
            if (releases.GetArrayLength() == 0) return null;
            if (releases[0].ValueKind == JsonValueKind.Object && releases[0].TryGetProperty("tag_name", out var tag) &&
                tag.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(tag.GetString())) return tag.GetString();
        }
        throw new JsonException("发布记录缺少版本标签。");
    }

    internal static string Describe(string installed, string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return "仓库还没有发布记录。";
        var published = tag.EndsWith("_Alpha", StringComparison.OrdinalIgnoreCase) ? tag[..^6] : tag;
        var order = string.Compare(published, installed, StringComparison.OrdinalIgnoreCase);
        if (order == 0) return $"当前版本已与发布页一致（{tag}）。";
        if (!IsSnapshot(installed) || !IsSnapshot(published)) return $"发布页版本：{tag}。";
        return order > 0 ? $"有可用更新：{tag}。" : $"本机版本 {installed} 比发布页版本 {tag} 更新。";
    }

    private static bool IsSnapshot(string value) => Regex.IsMatch(value, @"\A[0-9]{2}[wW][0-9]{2}[a-zA-Z]\z");
}
