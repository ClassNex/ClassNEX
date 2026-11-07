using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassNex.Models;

namespace ClassNex.Services;

public static class AccountService
{
    private const string RedirectUri = "http://localhost:17171/callback";

    private static readonly object CodeLock = new();
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

    public static string? AvatarImagePath =>
        File.Exists(AvatarPath) ? AvatarPath : null;

    public static bool FlowActive => _listener is { IsListening: true };

    public static string BuildLoginUrl()
    {
        var settings = AppServices.Settings;
        var endpoint = settings.AccountEndpoint.TrimEnd('/');
        _state = Guid.NewGuid().ToString("N");
        lock (CodeLock)
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
        lock (CodeLock)
        {
            code = _receivedCode;
            _receivedCode = null;
            return code is not null;
        }
    }

    public static void CancelLogin()
    {
        StopListener();
        lock (CodeLock)
            _receivedCode = null;
    }

    public static async Task<(bool Ok, string Message)> CompleteLoginAsync(string code)
    {
        try
        {
            var settings = AppServices.Settings;
            var endpoint = settings.AccountEndpoint.TrimEnd('/');
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = settings.AccountClientId,
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
            });

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using var response = await http.PostAsync(
                $"{endpoint}/api/login/oauth/access_token", form);
            var text = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (!root.TryGetProperty("access_token", out var accessToken) ||
                accessToken.GetString() is not { Length: > 0 } token)
            {
                var message = root.TryGetProperty("error_description", out var description) &&
                              description.GetString() is { Length: > 0 } desc
                    ? desc
                    : "账户服务没有返回有效的访问令牌";
                return (false, message);
            }

            var profile = await FetchProfileAsync(token);
            if (profile is null)
                return (false, "无法获取账户资料");

            StopListener();
            Current = profile;
            Save();
            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
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
        var listener = _listener;
        _listener = null;
        try
        {
            listener?.Close();
        }
        catch
        {
        }
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
            var state = context.Request.QueryString["state"] ?? "";
            var code = context.Request.QueryString["code"] ?? "";
            var ok = state == _state && code.Length > 0;
            var html = ok
                ? """
                  <!doctype html><html lang="zh-CN"><head><meta charset="utf-8">
                  <meta name="viewport" content="width=device-width,initial-scale=1">
                  <title>ClassNEX 授权完成</title>
                  <style>body{margin:0;min-height:100vh;display:grid;place-items:center;background:#fdf7f9;color:#24171d;font-family:"Microsoft YaHei UI",sans-serif}.card{width:min(420px,calc(100% - 40px));padding:36px;border:1px solid #efd8e1;border-radius:20px;background:#fff;text-align:center;box-shadow:0 20px 60px #7f455315}h1{font-size:24px;margin:0 0 12px}p{line-height:1.7;margin:0;color:#705764}.mark{width:56px;height:56px;margin:0 auto 18px;border-radius:50%;display:grid;place-items:center;background:#ffe7ef;color:#d95c7c;font-size:28px}</style>
                  </head><body><main class="card"><div class="mark">✓</div><h1>浏览器授权完成</h1>
                  <p>正在把账户信息同步到 ClassNEX，请稍候并返回应用。</p>
                  </main></body></html>
                  """
                : """
                  <!doctype html><html lang="zh-CN"><head><meta charset="utf-8">
                  <meta name="viewport" content="width=device-width,initial-scale=1">
                  <title>ClassNEX 授权未完成</title>
                  <style>body{margin:0;min-height:100vh;display:grid;place-items:center;background:#fdf7f9;color:#24171d;font-family:"Microsoft YaHei UI",sans-serif}.card{width:min(420px,calc(100% - 40px));padding:36px;border:1px solid #efd8e1;border-radius:20px;background:#fff;text-align:center;box-shadow:0 20px 60px #7f455315}h1{font-size:24px;margin:0 0 12px}p{line-height:1.7;margin:0;color:#705764}.mark{width:56px;height:56px;margin:0 auto 18px;border-radius:50%;display:grid;place-items:center;background:#ffe7ef;color:#d95c7c;font-size:28px}</style>
                  </head><body><main class="card"><div class="mark">!</div><h1>授权未完成</h1>
                  <p>没有收到有效的登录结果，请返回 ClassNEX 重新发起登录。</p>
                  </main></body></html>
                  """;

            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.StatusCode = ok ? 200 : 400;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.Close();

            if (!ok)
                return;

            lock (CodeLock)
                _receivedCode = code;
        }
        catch
        {
        }
    }

    private static async Task<AccountProfile?> FetchProfileAsync(string accessToken)
    {
        var endpoint = AppServices.Settings.AccountEndpoint.TrimEnd('/');
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
