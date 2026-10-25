using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassNex.Models;

namespace ClassNex.Services;

public static class AccountService
{
    private const string RedirectUri = "http://localhost:17171/callback";

    private static HttpListener? _listener;
    private static string _state = "";
    private static string? _receivedCode;

    public static AccountProfile? Current { get; private set; }

    public static event Action? Changed;

    private static string FilePath =>
        Path.Combine(SettingsService.DataDirectory, "account.json");

    private static string AvatarPath =>
        Path.Combine(SettingsService.DataDirectory, "account_avatar.png");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return;

            var json = File.ReadAllText(FilePath);
            if (!string.IsNullOrWhiteSpace(json))
            {
                Current = JsonSerializer.Deserialize<AccountProfile>(json, JsonOptions);
                if (Current is not null && !string.IsNullOrWhiteSpace(Current.AvatarUrl))
                    _ = RefreshAvatarAsync();
            }
        }
        catch
        {
            Current = null;
        }
    }

    public static bool FlowActive => _listener is { IsListening: true };

    public static string? AvatarImagePath =>
        File.Exists(AvatarPath) ? AvatarPath : null;

    public static void CancelLogin()
    {
        _receivedCode = null;
        StopListener();
    }

    public static string BuildLoginUrl()
    {
        var settings = AppServices.Settings;
        var endpoint = settings.AccountEndpoint.TrimEnd('/');
        _state = Guid.NewGuid().ToString("N");
        _receivedCode = null;
        StartListener();

        return $"{endpoint}/login/oauth/authorize" +
               $"?client_id={Uri.EscapeDataString(settings.AccountClientId)}" +
               "&response_type=code" +
               "&scope=openid%20profile%20email" +
               $"&state={_state}" +
               "&prompt=login" +
               $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}";
    }

    public static bool TryTakeCode(out string? code)
    {
        code = _receivedCode;
        _receivedCode = null;
        return code is not null;
    }

    public static async Task<bool> CompleteLoginAsync(string code)
    {
        var settings = AppServices.Settings;
        var endpoint = settings.AccountEndpoint.TrimEnd('/');
        var url = $"{endpoint}/api/login/oauth/access_token";

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = settings.AccountClientId,
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
        });

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var response = await http.PostAsync(url, form);
        var text = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (!root.TryGetProperty("access_token", out var accessToken) ||
            accessToken.GetString() is not { Length: > 0 } token)
            return false;

        var profile = await FetchProfileAsync(token);
        if (profile is null)
            return false;

        StopListener();
        Current = profile;
        Save();
        return true;
    }

    public static async Task<string?> RefreshAvatarAsync()
    {
        if (Current is null)
            return null;

        return await DownloadAvatarAsync(Current.AvatarUrl);
    }

    public static void Logout()
    {
        StopListener();
        Current = null;

        if (File.Exists(FilePath))
            File.Delete(FilePath);
        if (File.Exists(AvatarPath))
            File.Delete(AvatarPath);

        Changed?.Invoke();
    }

    private static void StartListener()
    {
        StopListener();

        var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:17171/callback/");
        listener.Start();
        _listener = listener;
        _ = Task.Run(() => ListenLoopAsync(listener));
    }

    private static void StopListener()
    {
        try
        {
            _listener?.Close();
        }
        catch
        {
        }

        _listener = null;
    }

    private static async Task ListenLoopAsync(HttpListener listener)
    {
        try
        {
            while (listener.IsListening)
            {
                var context = await listener.GetContextAsync();
                _ = Task.Run(() => HandleCallback(listener, context));
            }
        }
        catch
        {
        }
    }

    private static void HandleCallback(HttpListener listener, HttpListenerContext context)
    {
        try
        {
            var query = context.Request.Url?.Query.TrimStart('?') ?? "";
            var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));

            var state = parts.GetValueOrDefault("state") ?? "";
            var code = parts.GetValueOrDefault("code") ?? "";

            var ok = state == _state && code.Length > 0;
            var html = ok
                ? "<html><head><meta charset=\"utf-8\"><title>ClassNEX</title></head><body style=\"font-family:sans-serif;text-align:center;padding-top:80px\"><h2>登录成功</h2><p>可以关闭这个页面，回到 ClassNEX。</p></body></html>"
                : "<html><head><meta charset=\"utf-8\"><title>ClassNEX</title></head><body style=\"font-family:sans-serif;text-align:center;padding-top:80px\"><h2>登录未完成</h2><p>请回到 ClassNEX 重试。</p></body></html>";

            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.Close();

            if (ok)
            {
                _receivedCode = code;
                try
                {
                    listener.Close();
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static async Task<AccountProfile?> FetchProfileAsync(string accessToken)
    {
        var settings = AppServices.Settings;
        var endpoint = settings.AccountEndpoint.TrimEnd('/');
        var url = $"{endpoint}/api/userinfo?access_token={Uri.EscapeDataString(accessToken)}";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var text = await http.GetStringAsync(url);

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;

        static string Get(JsonElement root, string key) =>
            root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";

        var username = Get(root, "preferred_username");
        var displayName = Get(root, "name");
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = username;

        var profile = new AccountProfile
        {
            Id = Get(root, "sub"),
            Name = string.IsNullOrWhiteSpace(username) ? displayName : username,
            DisplayName = displayName,
            Email = Get(root, "email"),
            AvatarUrl = Get(root, "picture"),
            AccessToken = accessToken,
        };

        if (string.IsNullOrWhiteSpace(profile.Name))
            return null;

        await DownloadAvatarAsync(profile.AvatarUrl);
        return profile;
    }

    private static async Task<string?> DownloadAvatarAsync(string avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl))
            return null;

        try
        {
            var url = avatarUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? avatarUrl
                : AppServices.Settings.AccountEndpoint.TrimEnd('/') + "/" + avatarUrl.TrimStart('/');

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var bytes = await http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(AvatarPath, bytes);
            return AvatarPath;
        }
        catch
        {
            return null;
        }
    }

    private static void Save()
    {
        try
        {
            if (Current is null)
                return;

            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOptions));
            Changed?.Invoke();
        }
        catch
        {
        }
    }
}
