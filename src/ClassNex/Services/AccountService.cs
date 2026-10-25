using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassNex.Models;

namespace ClassNex.Services;

public static class AccountService
{
    private const string OrganizationName = "ClassNEX";
    private const string ApplicationName = "app-classnex";

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

    public static async Task<(bool Ok, string Message)> SignInAsync(string account, string password)
    {
        try
        {
            var endpoint = AppServices.Settings.AccountEndpoint.TrimEnd('/');
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = AppServices.Settings.AccountClientId,
                ["username"] = account.Trim(),
                ["password"] = password,
                ["scope"] = "openid profile email",
            });

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using var response = await http.PostAsync($"{endpoint}/api/login/oauth/access_token", form);
            var text = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (root.TryGetProperty("access_token", out var accessToken) &&
                accessToken.GetString() is { Length: > 0 } token)
            {
                var profile = await FetchProfileAsync(token);
                if (profile is null)
                    return (false, "获取用户信息失败，请重试");

                Current = profile;
                Save();
                return (true, "");
            }

            var message = root.TryGetProperty("error_description", out var description) &&
                          description.GetString() is { Length: > 0 } desc
                ? desc
                : "用户名或密码错误";
            return (false, message);
        }
        catch (Exception ex)
        {
            return (false, "网络错误：" + ex.Message);
        }
    }

    public static async Task<(bool Ok, string Message)> SendSignupCodeAsync(string email)
    {
        try
        {
            var endpoint = AppServices.Settings.AccountEndpoint.TrimEnd('/');
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["dest"] = email.Trim(),
                ["type"] = "email",
                ["applicationId"] = $"admin/{ApplicationName}",
                ["method"] = "signup",
                ["captchaType"] = "none",
            });

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using var response = await http.PostAsync($"{endpoint}/api/send-verification-code", form);
            return await ReadResultAsync(response);
        }
        catch (Exception ex)
        {
            return (false, "网络错误：" + ex.Message);
        }
    }

    public static async Task<(bool Ok, string Message)> SignUpAsync(
        string username, string displayName, string email, string password, string code)
    {
        try
        {
            var endpoint = AppServices.Settings.AccountEndpoint.TrimEnd('/');
            var body = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["organization"] = OrganizationName,
                ["application"] = ApplicationName,
                ["username"] = username.Trim(),
                ["name"] = displayName.Trim(),
                ["email"] = email.Trim(),
                ["password"] = password,
                ["emailCode"] = code.Trim(),
                ["agreement"] = true,
            }, JsonOptions);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync($"{endpoint}/api/signup", content);
            return await ReadResultAsync(response);
        }
        catch (Exception ex)
        {
            return (false, "网络错误：" + ex.Message);
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
        Current = null;

        if (File.Exists(FilePath))
            File.Delete(FilePath);
        if (File.Exists(AvatarPath))
            File.Delete(AvatarPath);

        Changed?.Invoke();
    }

    private static async Task<(bool Ok, string Message)> ReadResultAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            var status = root.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString() ?? ""
                : "";
            var message = root.TryGetProperty("msg", out var msgElement)
                ? msgElement.GetString() ?? ""
                : "";

            if (status == "ok")
                return (true, "");
            return (false, string.IsNullOrWhiteSpace(message) ? "操作失败" : message);
        }
        catch
        {
            return (false, "服务器返回了无法识别的响应");
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
