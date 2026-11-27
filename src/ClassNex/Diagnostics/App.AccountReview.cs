#if DEBUG
using Avalonia.Controls.ApplicationLifetimes;
using System.Net.Http.Json;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassNex.Models;
using ClassNex.Services;
using Native = Microsoft.UI.Xaml.Controls;
using Dependencies = Microsoft.UI.Xaml.Media.VisualTreeHelper;
using Automation = Microsoft.UI.Xaml.Automation.AutomationProperties;

namespace ClassNex;

public partial class App
{
    private static async Task VerifyLegacyPluginAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var status = 1;
        try
        {
            var host = (Core.Abstractions.IPluginManager)AppServices.Plugins!;
            for (var i = 0; i < 80 && host.LoadedPlugins.Count == 0; i++) await Task.Delay(50);
            if (host.LoadedPlugins.Single().Status != "已加载") throw new InvalidOperationException(host.LoadedPlugins.Single().Status);
            await host.SetEnabledAsync("compat", false);
            status = 0;
        }
        catch (Exception ex) { await File.WriteAllTextAsync(Path.Combine(SettingsService.DataDirectory, "plugin-review-error.txt"), ex.ToString()); }
        finally { desktop.Shutdown(status); }
    }

    private async Task VerifyAccountAsync(string issuer, string control, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var status = 1;
        try
        {
            if (AccountService.Current is not null || !new Uri(issuer).IsLoopback || !new Uri(control).IsLoopback)
                throw new InvalidOperationException("Account review requires a clean loopback fixture");
            AppServices.Settings.AccountEndpoint = issuer;
            AppServices.Settings.CentralManagement.ServerUrl = control;
            AppServices.Settings.AccountClientId = "fixture";
            using var browser = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(6) };
            using var response = await browser.GetAsync(AccountService.BuildLoginUrl());
            response.EnsureSuccessStatusCode();
            string? code = null;
            for (var i = 0; i < 40 && !AccountService.TryTakeCallback(out code, out _); i++) await Task.Delay(50);
            if (code is null) throw new InvalidOperationException("No desktop callback");
            var (ok, message) = await AccountService.CompleteLoginAsync(code);
            if (!ok || AccountService.Current is not { Name: "fixture", Email: "fixture@example.invalid" })
                throw new InvalidOperationException("Desktop token/profile exchange: " + message);
            var account = AccountService.Current;
            OpenSettings("account");
            var settingsWindow = _desktop!.Settings!;
            await VerifyIdentityAsync(account.DisplayName, account.Email, true);
            AppServices.Settings.NexApiKey = "local-test-value";
            var remote = new AppSettings { ThemeMode = "dark" };
            AppServices.ApplyManagedSettings(remote, null);
            if (!ReferenceEquals(account, AccountService.Current) || AppServices.Settings.NexApiKey != "local-test-value" ||
                CentralSettingsPayload.Serialize(AppServices.Settings).Contains("local-test-value", StringComparison.Ordinal) ||
                SettingsService.Load().ThemeMode != "dark") throw new InvalidOperationException("Managed settings lost account, local credentials or theme");
            var accountFile = Path.Combine(SettingsService.DataDirectory, "account.json");
            static bool ProtectedRecord(string path, AccountProfile expected)
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(path));
                return !document.RootElement.TryGetProperty(nameof(AccountProfile.AccessToken), out _) &&
                    document.RootElement.TryGetProperty("ProtectedAccessToken", out var encrypted) && encrypted.TryGetBytesFromBase64(out var bytes) && bytes.Length > 0 &&
                    !File.ReadAllText(path).Contains(expected.AccessToken, StringComparison.Ordinal);
            }
            if (!ProtectedRecord(accountFile, account)) throw new InvalidOperationException("Account credential not protected on disk");
            File.WriteAllText(accountFile, JsonSerializer.Serialize(account));
            AccountService.Load();
            if (!ProtectedRecord(accountFile, account) || AccountService.Current is not { } restored || restored.AccessToken != account.AccessToken)
                throw new InvalidOperationException("Old account did not migrate without losing its session");
            account = restored;
            await VerifyLiveSyncAsync(control, account.AccessToken);
            AppServices.Settings.NexApiBaseUrl = issuer;
            var agent = AppServices.Ai.CreateAgent(Services.NexIntelligence.NexPermission.ReadOnly);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await agent.RunAsync("测试课堂查询", timeout.Token);
            if (!agent.History.Any(m => m.Content == "本地课表联测通过")) throw new InvalidOperationException("NEX did not receive the local streaming response");
            await CheckSwitchesAsync();
            status = 0;

            async Task CheckSwitchesAsync()
            {
                string? location = null;
                var login = new Views.LoginWindow(value => { location = value; return true; });
                _loginWindow = login;
                var path = Path.Combine(SettingsService.DataDirectory, "account.json");
                var cached = AccountService.AvatarImagePath ?? throw new InvalidOperationException("The first avatar was not cached");
                var pixels = await File.ReadAllBytesAsync(cached);
                login.Show();
                try
                {
                    Require(!AccountService.FlowActive && location is null, "Opening the existing identity must not start an authorization");
                    AppServices.Settings.UserName = account.DisplayName;
                    AppServices.Settings.Email = account.Email;
                    AppServices.SaveSettings();
                    await StartSwitch();
                    Cancel();
                    Require(ReferenceEquals(account, AccountService.Current) && !AccountService.FlowActive && AppServices.Settings.Email == account.Email && File.Exists(cached), "Cancel must keep the first identity and avatar");

                    await Command("denied");
                    await StartSwitch();
                    using (var declined = await browser.GetAsync(location)) declined.EnsureSuccessStatusCode();
                    await Until(() => login.FindControl<TextBlock>("FlowStatusText")!.Text == "已取消浏览器授权。", "Provider cancellation left the login window waiting");
                    Require(ReferenceEquals(account, AccountService.Current) && !AccountService.FlowActive, "Provider cancellation must retain the signed-in account");
                    Cancel();

                    await Command("fail");
                    await StartSwitch();
                    using (var failure = await browser.GetAsync(location)) failure.EnsureSuccessStatusCode();
                    await Until(() => login.FindControl<TextBlock>("FlowStatusText")!.Text?.Contains("失败", StringComparison.Ordinal) == true, "Token rejection did not reach the actual login window");
                    Require(ReferenceEquals(account, AccountService.Current) && File.Exists(cached), "Refused reauthorization must not replace the active identity");
                    Cancel();

                    await Command("hold");
                    var late = AccountService.RefreshAvatarAsync();
                    for (int i = 0; i < 80 && !await browser.GetFromJsonAsync<bool>(issuer + "/fixture/avatar-status"); i++) await Task.Delay(50);
                    Require(await browser.GetFromJsonAsync<bool>(issuer + "/fixture/avatar-status"), "First avatar request did not reach the delay gate");
                    await CompleteSwitch("second");
                    await VerifyIdentityAsync("课表测试乙", "", false);
                    await Command("release");
                    Require(await late.WaitAsync(TimeSpan.FromSeconds(5)) is null && !File.Exists(cached) && AccountService.AvatarImagePath is null,
                        "Old delayed avatar repopulated the new identity");
                    Require(SettingsService.Load().UserName.Length == 0 && SettingsService.Load().Email.Length == 0, "Identity mirrors remained after replacement");
                    Require(JsonSerializer.Deserialize<AccountProfile>(await File.ReadAllTextAsync(path))?.Name == "second", "New identity not persisted");
                    var legacy = Path.Combine(SettingsService.DataDirectory, "account_avatar.png");
                    await File.WriteAllBytesAsync(legacy, pixels);
                    AccountService.Load();
                    Require(AccountService.Current is { Name: "second", AccessToken: "fixture-second" }, "The protected credential did not survive reopening");
                    await VerifyIdentityAsync("课表测试乙", "", false);
                    Require(!File.Exists(legacy) && AccountService.AvatarImagePath is null, "Unowned legacy avatar was reused at cold load");
                    AppServices.Settings.UserName = "单独保存的本机名称";
                    AppServices.Settings.Email = "local-only@example.invalid";
                    AppServices.SaveSettings();
                    await CompleteSwitch("broken");
                    Require(await AccountService.RefreshAvatarAsync() is null, "Undecodable picture was accepted into the cache");
                    await VerifyIdentityAsync("头像测试", "", false);
                    AccountService.Logout();
                    await VerifyIdentityAsync("未登录", "登录 ClassNEX ID", false);
                    var saved = SettingsService.Load();
                    Require(AccountService.Current is null && !AccountService.FlowActive && !File.Exists(path) && AccountService.AvatarImagePath is null &&
                        saved.UserName == "单独保存的本机名称" && saved.Email == "local-only@example.invalid", "Logout did not isolate the independently stored local preferences");
                }
                finally { await Command("release"); login.Close(); _loginWindow = null; }

                async Task StartSwitch()
                {
                    location = null;
                    Native.Button? button = null;
                    await Until(() => (button = Find<Native.Button>(Walk(settingsWindow.Content), "SwitchAccount")) is
                        { IsLoaded: true, IsEnabled: true, Visibility: Microsoft.UI.Xaml.Visibility.Visible },
                        "SwitchAccount did not become available", () => button is null ? "missing" : $"loaded={button.IsLoaded}, enabled={button.IsEnabled}, visibility={button.Visibility}");
                    new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button!).Invoke();
                    await Until(() => AccountService.FlowActive && location != null && login.FindControl<StackPanel>("StateFlow")!.IsVisible, "Switch account did not start the browser flow");
                }
                async Task CompleteSwitch(string fixture)
                {
                    await Command(fixture); await StartSwitch();
                    using var callback = await browser.GetAsync(location);
                    callback.EnsureSuccessStatusCode();
                    await Until(() => AccountService.Current?.Name == fixture && login.FindControl<StackPanel>("StateDone")!.IsVisible, "Account replacement failed: " + fixture);
                }
                void Cancel() => login.FindControl<Button>("CancelFlowButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                async Task Command(string value) { using var result = await browser.GetAsync(issuer + "/fixture/" + value); result.EnsureSuccessStatusCode(); }
            }

            static T? Find<T>(IEnumerable<Microsoft.UI.Xaml.DependencyObject> controls, string id) where T : Microsoft.UI.Xaml.FrameworkElement =>
                controls.OfType<T>().FirstOrDefault(x => Automation.GetAutomationId(x) == id);

            async Task VerifyIdentityAsync(string name, string email, bool photo)
            {
                var pending = new List<string>(4);
                await Until(() =>
                {
                    pending.Clear();
                    var tree = Walk(settingsWindow.Content).ToArray();
                    Check<Native.TextBlock>("SidebarAccountName", label => label.Text == name);
                    Check<Native.TextBlock>("SidebarAccountEmail", mail => mail.Text == email && (mail.Visibility == Microsoft.UI.Xaml.Visibility.Visible) == (email.Length > 0));
                    Check<Native.PersonPicture>("SidebarAccountPhoto", picture => (picture.ProfilePicture is not null) == photo && (photo || picture.DisplayName == (name == "未登录" ? "" : name)));
                    Check<Native.TextBlock>("AccountEmail", mail => mail.Visibility == (name != "未登录" && email.Length > 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed));
                    return pending.Count == 0;

                    void Check<T>(string id, Func<T, bool> matches) where T : Microsoft.UI.Xaml.FrameworkElement
                    {
                        var element = Find<T>(tree, id);
                        if (element is null) pending.Add(id + ": missing");
                        else if (!matches(element)) pending.Add(id + ": value or visibility differs");
                    }
                }, "Identity, missing email or avatar did not reach the native controls: " + name, () => $"page={settingsWindow.CurrentPageKey}; {string.Join(", ", pending)}");
            }
            static IEnumerable<Microsoft.UI.Xaml.DependencyObject> Walk(Microsoft.UI.Xaml.DependencyObject root)
            {
                yield return root;
                for (var i = 0; i < Dependencies.GetChildrenCount(root); i++)
                    foreach (var child in Walk(Dependencies.GetChild(root, i))) yield return child;
            }
            static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
            static async Task Until(Func<bool> ready, string reason, Func<string>? details = null)
            {
                for (int i = 0; i < 120; i++)
                {
                    await Task.Delay(50);
                    if (ready()) return;
                }
                throw new TimeoutException(details is null ? reason : $"{reason}: {details()}");
            }
        }
        catch (Exception ex) { await File.WriteAllTextAsync(Path.Combine(SettingsService.DataDirectory, "account-review-error.txt"), ex.ToString()); }
        finally { AccountService.CancelLogin(); desktop.Shutdown(status); }
    }

    private static async Task VerifyLiveSyncAsync(string endpoint, string token)
    {
        using var control = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(endpoint + "/api/v1/"), Timeout = TimeSpan.FromSeconds(6) };
        control.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var settings = AppServices.Settings;
        settings.CentralManagement.IsEnabled = true;
        var client = AppServices.Management!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await client.ConnectAndSyncAsync(timeout.Token);
        using var created = await control.PostAsJsonAsync("classes", new { name = "桌面双向验证" }, timeout.Token);
        created.EnsureSuccessStatusCode();
        using var cls = JsonDocument.Parse(await created.Content.ReadAsStringAsync(timeout.Token));
        var id = cls.RootElement.GetProperty("classId").GetString();
        using var paired = await control.PutAsJsonAsync("devices/" + settings.CentralManagement.ClientId, new { classId = id }, timeout.Token);
        paired.EnsureSuccessStatusCode();
        const string yaml = "version: 1\nsubjects: [{name: 双向课程}]\nschedules: []\n";
        using var update = await control.PutAsJsonAsync("classes/" + id, new { settingsJson = "{\"themeMode\":\"light\"}", timetableYaml = yaml, message = "fixture" }, timeout.Token);
        update.EnsureSuccessStatusCode();
        while (AppServices.Settings.ThemeMode != "light" || !AppServices.Schedule.Profile.Subjects.Any(s => s.Name == "双向课程"))
            await Task.Delay(100, timeout.Token);
        AppServices.Settings.ThemeMode = "dark";
        AppServices.SaveSettings();
        while (true)
        {
            using var revision = JsonDocument.Parse(await control.GetStringAsync("classes/" + id, timeout.Token));
            using var config = JsonDocument.Parse(revision.RootElement.GetProperty("settingsJson").GetString()!);
            if (config.RootElement.GetProperty("themeMode").GetString() == "dark")
            {
                if (config.RootElement.TryGetProperty("nexApiKey", out _) || AccountService.Current?.AccessToken != token)
                    throw new InvalidOperationException("Live sync leaked or lost local credentials");
                break;
            }
            await Task.Delay(100, timeout.Token);
        }
        client.Disconnect();
    }
}
#endif
