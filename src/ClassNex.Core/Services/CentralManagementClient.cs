using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>与集控服务通信的可复用 HTTP 客户端；保留基地址的部署子路径。</summary>
public sealed class CentralManagementClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    /// <summary>Uses direct API responses; a redirect must not forward a classroom payload to another host.</summary>
    public CentralManagementClient() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }

    /// <summary>Uses a caller-supplied transport, owned and disposed by this client.</summary>
    public CentralManagementClient(HttpMessageHandler transport) => _http = new(transport) { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Request a short code from the built-in service without a desktop account.</summary>
    public async Task<CentralPairingTicket> StartPairingAsync(string serverUrl, CentralPairingStart device, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(serverUrl, "/api/v1/pairing/start", HttpMethod.Post, "", JsonSerializer.Serialize(device, JsonOptions), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CentralPairingTicket>(JsonOptions, cancellationToken) ?? throw new InvalidOperationException("未收到配对码");
    }

    /// <summary>Read this device's enrollment state without granting it any owner privileges.</summary>
    public async Task<CentralPairingStatus> PairingStatusAsync(string serverUrl, CentralPairingTicket ticket, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(serverUrl, "/api/v1/pairing/status?clientId=" + Uri.EscapeDataString(ticket.ClientId), HttpMethod.Get, AsDevice(ticket), null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CentralPairingStatus>(JsonOptions, cancellationToken) ?? throw new InvalidOperationException("未收到配对状态");
    }

    /// <summary>Revoke only the classroom grant held by this client, including an unclaimed code.</summary>
    public async Task RevokePairingAsync(string serverUrl, CentralPairingTicket ticket, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(serverUrl, "/api/v1/pairing/device?clientId=" + Uri.EscapeDataString(ticket.ClientId), HttpMethod.Delete, AsDevice(ticket), null, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized) await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>Formats the non-OAuth credential used only for a paired device's own sync endpoints.</summary>
    public static string AsDevice(CentralPairingTicket ticket) => "ClassNexDevice " + ticket.DeviceToken;

    /// <summary>使用当前账户令牌登记或刷新设备的身份资料。</summary>
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

    /// <summary>获取设备的班级配置；服务端没有分配可用配置时返回 null。</summary>
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

    /// <summary>发送设备的当前配置与同步结果，不创建本地账户会话。</summary>
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

    /// <summary>发布设备侧课堂改动，并返回服务端确认的新修订号。</summary>
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
        if (!string.IsNullOrEmpty(accessToken)) request.Headers.Authorization = accessToken.StartsWith("ClassNexDevice ", StringComparison.Ordinal)
            ? AuthenticationHeaderValue.Parse(accessToken) : new AuthenticationHeaderValue("Bearer", accessToken);
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
                : body, null, response.StatusCode);
    }

    /// <summary>释放此客户端持有的 HTTP 连接资源。</summary>
    public void Dispose() => _http.Dispose();
}
