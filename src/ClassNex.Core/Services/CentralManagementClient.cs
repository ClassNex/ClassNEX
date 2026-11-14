using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClassNex.Models;

namespace ClassNex.Services;

public sealed class CentralManagementClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<CentralRegistrationResponse> RegisterAsync(
        string serverUrl,
        CentralRegistrationRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync(
            BuildUri(serverUrl, "/api/v1/client/register"),
            request,
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CentralRegistrationResponse>(
                   JsonOptions,
                   cancellationToken)
               ?? throw new InvalidOperationException("集控服务没有返回注册结果");
    }

    public async Task<CentralConfigEnvelope> PullAsync(
        string serverUrl,
        string clientToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildUri(serverUrl, "/api/v1/client/config"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clientToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CentralConfigEnvelope>(
                   JsonOptions,
                   cancellationToken)
               ?? throw new InvalidOperationException("集控服务没有返回配置");
    }

    public async Task ReportAsync(
        string serverUrl,
        string clientToken,
        CentralReportRequest report,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(serverUrl, "/api/v1/client/report"))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(report, JsonOptions),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clientToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static Uri BuildUri(string serverUrl, string path) =>
        new(serverUrl.TrimEnd('/') + path, UriKind.Absolute);

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException(
                string.IsNullOrWhiteSpace(body) ? "集控客户端令牌无效" : body);

        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(body)
                ? $"集控服务返回 {(int)response.StatusCode}"
                : body);
    }

    public void Dispose() => _http.Dispose();
}
