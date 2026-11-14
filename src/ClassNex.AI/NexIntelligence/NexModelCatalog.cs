using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClassNex.Services.NexIntelligence;


public static class NexModelCatalog
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    
    public static async Task<List<string>> FetchAsync(string baseUrl, string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("服务地址为空。");

        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + "/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"接口返回 {(int)response.StatusCode}：{Shorten(body)}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return new List<string>();

        var list = new List<string>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                list.Add(id.GetString()!);
        }

        return list.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    
    public static async Task<string> TestAsync(string baseUrl, string apiKey, string model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("服务地址为空。");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("接口密钥为空。");

        var payload = JsonSerializer.Serialize(new
        {
            model,
            messages = new[] { new { role = "user", content = "只回复两个字：正常" } },
            max_tokens = 20,
            stream = false,
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/v1/chat/completions")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"接口返回 {(int)response.StatusCode}：{Shorten(body)}");

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var text)
            ? text.GetString() ?? ""
            : "";

        return string.IsNullOrWhiteSpace(content) ? "（模型没有返回正文）" : content.Trim();
    }

    private static string Shorten(string s) => s.Length <= 200 ? s : s[..200] + "…";
}
