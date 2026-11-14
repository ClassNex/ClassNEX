using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ClassNex.Central;

public sealed class CentralAuthService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    public CentralAuthService(
        string casdoorUrl,
        string casdoorInternalUrl,
        string clientId,
        string clientSecret,
        string publicBaseUrl)
    {
        CasdoorUrl = casdoorUrl.TrimEnd('/');
        CasdoorInternalUrl = casdoorInternalUrl.TrimEnd('/');
        ClientId = clientId;
        ClientSecret = clientSecret;
        PublicBaseUrl = publicBaseUrl.TrimEnd('/');
    }

    public string CasdoorUrl { get; }

    public string CasdoorInternalUrl { get; }

    public string ClientId { get; }

    public string ClientSecret { get; }

    public string PublicBaseUrl { get; }

    public string BuildLoginUrl(string state, string codeChallenge) =>
        $"{CasdoorUrl}/login/oauth/authorize" +
        $"?client_id={Uri.EscapeDataString(ClientId)}" +
        "&response_type=code" +
        "&scope=openid%20profile%20email" +
        $"&state={Uri.EscapeDataString(state)}" +
        "&prompt=login" +
        $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
        "&code_challenge_method=S256" +
        $"&redirect_uri={Uri.EscapeDataString(PublicBaseUrl + "/auth/callback")}";

    public async Task<CentralUser?> ExchangeCodeAsync(string code, string codeVerifier)
    {
        var values = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = ClientId,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = PublicBaseUrl + "/auth/callback",
        };
        if (!string.IsNullOrWhiteSpace(ClientSecret))
            values["client_secret"] = ClientSecret;

        using var form = new FormUrlEncodedContent(values);
        using var response = await _http.PostAsync(
            CasdoorInternalUrl + "/api/login/oauth/access_token",
            form);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Casdoor 授权码换取失败：" + text);

        using var document = JsonDocument.Parse(text);
        if (!document.RootElement.TryGetProperty("access_token", out var tokenElement) ||
            tokenElement.GetString() is not { Length: > 0 } token)
            throw new InvalidOperationException("Casdoor 没有返回 AccessToken");

        return await GetUserAsync(token);
    }

    public async Task<CentralUser?> GetUserAsync(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return null;

        if (_cache.TryGetValue(accessToken, out var cached) &&
            cached.ExpiresAt > DateTimeOffset.UtcNow)
            return cached.User;

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            CasdoorInternalUrl + "/api/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            using var fallback = await _http.GetAsync(
                CasdoorInternalUrl + "/api/userinfo?access_token=" +
                Uri.EscapeDataString(accessToken));
            if (!fallback.IsSuccessStatusCode)
                return null;
            return ParseUser(
                await fallback.Content.ReadAsStringAsync(),
                accessToken);
        }

        return ParseUser(
            await response.Content.ReadAsStringAsync(),
            accessToken);
    }

    private CentralUser? ParseUser(string text, string accessToken)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        string Get(string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";

        var subject = Get("sub");
        if (string.IsNullOrWhiteSpace(subject))
            return null;

        var user = new CentralUser(
            subject,
            Get("name"),
            Get("email"),
            Get("preferred_username"));
        _cache[accessToken] = new CacheEntry(
            user,
            DateTimeOffset.UtcNow.AddMinutes(2));
        return user;
    }

    private sealed record CacheEntry(CentralUser User, DateTimeOffset ExpiresAt);
}

public sealed record CentralUser(
    string Subject,
    string Name,
    string Email,
    string PreferredUsername);
