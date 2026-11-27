using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using static ClassNex.WinUI.NativeSettingsKeys;

namespace ClassNex.WinUI;

public sealed partial class NativeSettingsWindow
{
    private Action? _leavePlugins;
    private CancellationTokenSource? _releaseCheck;
    private (DateTimeOffset Time, string Status)? _lastReleaseCheck;
    private static readonly HttpClient Releases = new() { Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = 2 * 1024 * 1024 };

    private void PluginsPage()
    {
        var host = _bridge.ReadPlugins();
        var page = _page;
        var list = new StackPanel { Spacing = 6 };
        var status = Target("PluginStatus", "插件状态", Text(""));
        var reload = Target("PluginReload", "重新扫描", new Button { Content = "重新扫描", IsEnabled = host != null });
        Row("本机插件", "只安装来自可信开发者的插件。", reload);
        CommandRow("PluginFolder", "插件目录", "打开文件夹", PluginFolder);
        _body.Children.Add(status); _body.Children.Add(list);
        bool busy = false;
        void Refresh()
        {
            if (_closed || _page != page || busy) return;
            list.Children.Clear();
            var plugins = host?.LoadedPlugins ?? [];
            status.Text = host is null ? "插件服务尚未就绪。" : plugins.Count == 0 ? "尚未安装插件。将插件放入目录后重新扫描。" : $"已发现 {plugins.Count} 个插件";
            foreach (var plugin in plugins)
            {
                var group = new StackPanel { Spacing = 8 };
                var toggle = new ToggleSwitch { IsOn = plugin.Enabled, IsEnabled = plugin.CanToggle, OnContent = "开", OffContent = "关" };
                Identify(toggle, "PluginEnabled_" + plugin.Id, "启用 " + plugin.Name);
                toggle.Toggled += async (_, _) => { if (host != null && plugin.CanToggle && plugin.Enabled != toggle.IsOn) await Change(() => host.SetEnabledAsync(plugin.Id, toggle.IsOn)); };
                group.Children.Add(MakeSettingRow("启用插件", "", toggle, false));
                group.Children.Add(Text("标识：" + plugin.Id));
                group.Children.Add(Text("权限：" + (plugin.Permissions.Count == 0 ? "未声明" : string.Join("、", plugin.Permissions))));
                var title = new StackPanel { Spacing = 2 };
                title.Children.Add(Text($"{plugin.Name}　{plugin.Version}")); title.Children.Add(Text(plugin.Status, "CaptionTextBlockStyle"));
                list.Children.Add(new Expander { Header = title, Content = group, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            }
        }
        async Task Change(Func<Task> operation)
        {
            if (busy || _closed || _page != page) return;
            busy = true; reload.IsEnabled = false;
            foreach (var item in list.Children.OfType<Expander>()) item.IsEnabled = false;
            status.Text = "正在更新插件…";
            string? error = null;
            try { await operation(); } catch (Exception ex) { error = "未能更新插件：" + ex.Message; }
            finally
            {
                busy = false;
                if (!_closed && _page == page) { reload.IsEnabled = true; Refresh(); if (error != null) status.Text = error; }
            }
        }
        if (host != null)
        {
            void Changed() => DispatcherQueue.TryEnqueue(Refresh);
            host.Changed += Changed;
            _leavePlugins = () => host.Changed -= Changed;
            reload.Click += async (_, _) => await Change(() => host.LoadAllAsync());
        }
        Refresh();
    }

    private void LeavePageServices()
    {
        _leavePlugins?.Invoke(); _leavePlugins = null;
        var check = _releaseCheck;
        _releaseCheck = null;
        check?.Cancel();
    }

    private void UpdatePage()
    {
        var check = Target("CheckUpdate", "检查更新", new Button { Content = "检查更新", Style = ButtonStyle("AccentButtonStyle") });
        var result = Target("UpdateResult", "版本检查结果", Text(_lastReleaseCheck?.Status ?? "尚未检查更新"));
        AutomationProperties.SetName(result, "");
        AutomationProperties.SetLiveSetting(result, AutomationLiveSetting.Polite);
        var detail = SettingCaption(_lastReleaseCheck is { } last ? $"上次检查：{last.Time.LocalDateTime:g}" : "手动连接 ClassNEX 官方发布页，不会自动安装。");
        var labels = new StackPanel { Spacing = 4 }; labels.Children.Add(result); labels.Children.Add(detail);
        var symbol = new FontIcon { Glyph = "\uE895", FontSize = 34 };
        var progress = new ProgressRing { Width = 32, Height = 32, IsActive = false, Visibility = Visibility.Collapsed };
        var icon = new Grid(); icon.Children.Add(symbol); icon.Children.Add(progress);
        _body.Children.Add(SummaryCard(icon, labels, check));
        Section("本机版本");
        StatusRow("InstalledVersion", "已安装版本", _ => Or(_bridge.Status(NativeSettingsKeys.Version), "未提供版本号"));
        Section("发布记录");
        _body.Children.Add(NavigationCard("OpenReleases", "更新说明与下载", "\uE896", SettingCaption("在浏览器查看官方发布记录和安装包"), () => _bridge.Command(ReleasePage), true));
        var page = _page;
        check.Click += async (_, _) =>
        {
            if (_releaseCheck != null || _closed) return;
            using var cts = new CancellationTokenSource();
            _releaseCheck = cts;
            check.IsEnabled = false; symbol.Visibility = Visibility.Collapsed; progress.Visibility = Visibility.Visible; progress.IsActive = true;
            result.Text = "正在检查更新…"; detail.Text = "正在连接 GitHub 官方发布页。";
            try
            {
                var installed = _bridge.Status(NativeSettingsKeys.Version);
                var tag = await DesktopRelease.LatestTagAsync(Releases, installed, cts.Token);
                if (_closed || _page != page) return;
                result.Text = DesktopRelease.Describe(installed, tag);
                var now = DateTimeOffset.Now;
                _lastReleaseCheck = (now, result.Text);
                detail.Text = $"上次检查：{now.LocalDateTime:g}";
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or JsonException or InvalidOperationException)
            {
                if (!_closed && _page == page && !cts.IsCancellationRequested)
                {
                    result.Text = "暂时无法检查更新";
                    detail.Text = "请检查网络后重试，也可以打开下方的官方发布页。";
                }
            }
            finally
            {
                if (ReferenceEquals(_releaseCheck, cts)) _releaseCheck = null;
                if (!_closed && _page == page)
                {
                    check.IsEnabled = true; progress.IsActive = false; progress.Visibility = Visibility.Collapsed; symbol.Visibility = Visibility.Visible;
                }
            }
        };
    }
}
