using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassNex.Models;

namespace ClassNex.Services;

public static partial class AccountService
{
    private const string RedirectUri = "http://localhost:17171/callback";

    private static readonly object CodeLock = new();
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    private static string _verifier = "";
    private static HttpListener? _listener;
    private static string _state = "";
    private static string? _receivedCode;
    private static string? _receivedError;
    private static bool _callbackReceived;

    public static AccountProfile? Current { get; private set; }

    public static event Action? Changed;

    private static string FilePath =>
        Path.Combine(SettingsService.DataDirectory, "account.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Load()
    {
        AccountProfile? profile = null;
        try
        {
            if (File.Exists(FilePath)) profile = JsonSerializer.Deserialize<AccountProfile>(File.ReadAllText(FilePath), JsonOptions);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        ChangeProfile(profile is { IsLoggedIn: true } ? profile : null);
        RemoveCachedFile(Path.Combine(SettingsService.DataDirectory, "account_avatar.png"));
        if (_ownedAvatarPath is not null && AvatarImagePath is null) _ = RefreshAvatarAsync();
        Changed?.Invoke();
    }

    public static bool FlowActive => _listener is { IsListening: true };

    public static string BuildLoginUrl()
    {
        var settings = AppServices.Settings;
        var endpoint = settings.AccountEndpoint.TrimEnd('/');
        _state = Guid.NewGuid().ToString("N");
        _verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(_verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        lock (CodeLock) { _receivedCode = _receivedError = null; _callbackReceived = false; }

        StartListener();

        return $"{endpoint}/login/oauth/authorize" +
               $"?client_id={Uri.EscapeDataString(settings.AccountClientId)}" +
               "&response_type=code" +
               "&scope=openid%20profile%20email" +
               $"&state={_state}" +
               $"&code_challenge_method=S256&code_challenge={challenge}" +
               "&prompt=login" +
               $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}";
    }

    internal static bool TryTakeCallback(out string? code, out string? error)
    {
        lock (CodeLock)
        {
            code = _receivedCode;
            error = _receivedError;
            _receivedCode = _receivedError = null;
            return code is not null || error is not null;
        }
    }

    public static void CancelLogin()
    {
        lock (CodeLock)
        {
            _state = _verifier = "";
            StopListener();
            _receivedCode = _receivedError = null;
            _callbackReceived = false;
        }
    }

    public static async Task<(bool Ok, string Message)> CompleteLoginAsync(string code)
    {
        var expectedState = _state;
        if (expectedState.Length == 0) return (false, "登录已取消");
        try
        {
            var settings = AppServices.Settings;
            var endpoint = settings.AccountEndpoint.TrimEnd('/');
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = settings.AccountClientId,
                ["code"] = code,
                ["code_verifier"] = _verifier,
                ["redirect_uri"] = RedirectUri,
            });

            using var response = await Http.PostAsync(
                $"{endpoint}/api/login/oauth/access_token", form);
            var text = await response.Content.ReadAsStringAsync();

            if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
                return (false, $"账户服务未返回授权结果（HTTP {(int)response.StatusCode}），请检查校园网连接后重试。");
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

            var profile = await FetchProfileAsync(endpoint, token);
            if (profile is null)
                return (false, "无法获取账户资料");

            var previous = Current;
            lock (CodeLock)
            {
                if (expectedState != _state) return (false, "登录已取消");
                SaveProfile(profile);
                ChangeProfile(profile);
                _state = _verifier = "";
                StopListener();
            }
            ClearLegacyIdentity(previous);
            Changed?.Invoke();
            _ = RefreshAvatarAsync();
            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static void Logout()
    {
        CancelLogin();
        var previous = Current;
        if (File.Exists(FilePath)) File.Delete(FilePath);
        ChangeProfile(null);
        ClearLegacyIdentity(previous);
        Changed?.Invoke();
    }

    private static void ClearLegacyIdentity(AccountProfile? previous)
    {
        if (previous is null) return;
        var settings = AppServices.Settings;
        var changed = false;
        if (settings.UserName.Length > 0 && settings.UserName == previous.DisplayName) { settings.UserName = ""; changed = true; }
        if (settings.Email.Length > 0 && settings.Email == previous.Email) { settings.Email = ""; changed = true; }
        if (changed) AppServices.SaveSettings();
    }

    private static void StartListener()
    {
        StopListener();

        var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:17171/");
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
                HandleCallback(listener, context);
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
            if (!context.Request.IsLocal || context.Request.HttpMethod != "GET" || context.Request.Url?.AbsolutePath.TrimEnd('/') != "/callback")
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }
            var state = context.Request.QueryString["state"] ?? "";
            var code = context.Request.QueryString["code"] ?? "";
            var error = context.Request.QueryString["error"];
            var hasCode = code.Length != 0;
            var hasError = !string.IsNullOrEmpty(error);
            bool valid;
            lock (CodeLock)
            {
                valid = ReferenceEquals(listener, _listener) && _state.Length != 0 && state == _state &&
                        hasCode != hasError;
                if (valid && !_callbackReceived)
                {
                    _callbackReceived = true;
                    if (hasCode) _receivedCode = code;
                    else _receivedError = error == "access_denied" ? "已取消浏览器授权。" : "账户服务未完成授权，请重新登录。";
                }
            }
            var html = valid && hasCode
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
            context.Response.StatusCode = valid ? 200 : 400;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.Close();

        }
        catch
        {
        }
    }

    private static async Task<AccountProfile?> FetchProfileAsync(string issuer, string accessToken)
    {
        async Task<AccountProfile?> Read(string url)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new("Bearer", accessToken);
            using var response = await Http.SendAsync(request);
            return response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true
                ? ParseProfile(await response.Content.ReadAsStringAsync(), accessToken) : null;
        }

        var uri = issuer + "/api/userinfo";
        if (!string.Equals(issuer, AppSettings.DefaultAccountEndpoint, StringComparison.OrdinalIgnoreCase))
            return await Read(uri);
        try
        {
            if (await Read(uri) is { } profile) return profile;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { }
        return await Read(AppSettings.DefaultCentralEndpoint + "/api/v1/auth/profile");
    }

    private static AccountProfile? ParseProfile(string text, string accessToken)
    {
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

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
            Email = Get(root, "email").Trim(),
            AvatarUrl = Get(root, "picture"),
            AccessToken = accessToken,
        };

        if (string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.Name))
            return null;

        return profile;
    }

    private static void SaveProfile(AccountProfile profile)
    {
        SettingsService.EnsureDataDirectory();
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(profile, JsonOptions));
            File.Move(temporary, FilePath, true);
        }
        finally { RemoveCachedFile(temporary); }
    }
}
