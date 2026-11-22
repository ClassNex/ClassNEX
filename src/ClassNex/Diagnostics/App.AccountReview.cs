#if DEBUG
using Avalonia.Controls.ApplicationLifetimes;
using System.Net.Http.Json;
using System.Text.Json;
using ClassNex.Models;
using ClassNex.Services;

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
            for (var i = 0; i < 40 && !AccountService.TryTakeCode(out code); i++) await Task.Delay(50);
            if (code is null) throw new InvalidOperationException("No desktop callback");
            var (ok, message) = await AccountService.CompleteLoginAsync(code);
            if (!ok || AccountService.Current is not { Name: "fixture", Email: "fixture@example.invalid" })
                throw new InvalidOperationException("Desktop token/profile exchange: " + message);
            var account = AccountService.Current;
            OpenSettings("account");
            var settingsWindow = _desktop!.Settings!;
            await VerifyRailAsync(account.DisplayName, account.Email);
            AppServices.Settings.NexApiKey = "local-test-value";
            var remote = new AppSettings { ThemeMode = "dark" };
            AppServices.ApplyManagedSettings(remote, null);
            if (!ReferenceEquals(account, AccountService.Current) || AppServices.Settings.NexApiKey != "local-test-value" ||
                CentralSettingsPayload.Serialize(AppServices.Settings).Contains("local-test-value", StringComparison.Ordinal) ||
                SettingsService.Load().ThemeMode != "dark") throw new InvalidOperationException("Managed settings lost account, local credentials or theme");
            if (!File.Exists(Path.Combine(SettingsService.DataDirectory, "account.json"))) throw new InvalidOperationException("Account not persisted");
            await VerifyLiveSyncAsync(control, account.AccessToken);
            AppServices.Settings.NexApiBaseUrl = issuer;
            var agent = AppServices.Ai.CreateAgent(Services.NexIntelligence.NexPermission.ReadOnly);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await agent.RunAsync("测试课堂查询", timeout.Token);
            if (!agent.History.Any(m => m.Content == "本地课表联测通过")) throw new InvalidOperationException("NEX did not receive the local streaming response");
            AccountService.Logout();
            if (AccountService.Current != null || AccountService.FlowActive) throw new InvalidOperationException("Desktop sign-out incomplete");
            await VerifyRailAsync("未登录", null);
            status = 0;

            async Task VerifyRailAsync(string name, string? email)
            {
                for (var i = 0; i < 60; i++)
                {
                    await Task.Delay(50);
                    var elements = Walk(settingsWindow.Content).OfType<Microsoft.UI.Xaml.Controls.TextBlock>().ToList();
                    string? Value(string key) => elements.FirstOrDefault(x => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(x) == key)?.Text;
                    if (Value("SidebarAccountName") == name && (email != null ? Value("SidebarAccountEmail") == email : Value("SidebarAccountEmail") != "fixture@example.invalid")) return;
                }
                throw new InvalidOperationException("ClassNEX ID state did not reach the navigation rail");
            }
            static IEnumerable<Microsoft.UI.Xaml.DependencyObject> Walk(Microsoft.UI.Xaml.DependencyObject root)
            {
                yield return root;
                for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
                    foreach (var child in Walk(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
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
