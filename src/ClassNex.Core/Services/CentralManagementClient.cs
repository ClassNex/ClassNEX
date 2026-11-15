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

    public async Task RegisterDeviceAsync(
        string serverUrl,
        string accessToken,
        CentralDeviceRegistrationRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            serverUrl,
            "/api/v1/client/device",
            HttpMethod.Post,
            accessToken,
            JsonSerializer.Serialize(request, JsonOptions),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<CentralConfigEnvelope?> PullAsync(
        string serverUrl,
        string accessToken,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            serverUrl,
            "/api/v1/client/assignment?clientId=" + Uri.EscapeDataString(clientId),
            HttpMethod.Get,
            accessToken,
            null,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
            return null;
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CentralConfigEnvelope>(
            JsonOptions,
            cancellationToken);
    }

    public async Task ReportAsync(
        string serverUrl,
        string accessToken,
        CentralReportRequest report,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            serverUrl,
            "/api/v1/client/report",
            HttpMethod.Post,
            accessToken,
            JsonSerializer.Serialize(report, JsonOptions),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<CentralClassRevision> PublishAsync(
        string serverUrl,
        string accessToken,
        CentralClientPublishRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            serverUrl,
            "/api/v1/client/publish",
            HttpMethod.Post,
            accessToken,
            JsonSerializer.Serialize(request, JsonOptions),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CentralClassRevision>(
                   JsonOptions,
                   cancellationToken)
               ?? throw new InvalidOperationException("集控服务没有返回发布结果");
    }

    private async Task<HttpResponseMessage> SendAsync(
        string serverUrl,
        string path,
        HttpMethod method,
        string accessToken,
        string? json,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            method,
            new Uri(serverUrl.TrimEnd('/') + path, UriKind.Absolute));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await _http.SendAsync(request, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException(
                string.IsNullOrWhiteSpace(body) ? "ClassNEX ID 登录已失效" : body);

        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(body)
                ? $"集控服务返回 {(int)response.StatusCode}"
                : body);
    }

    public void Dispose() => _http.Dispose();
}
