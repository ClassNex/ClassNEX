using System.Text.Json;
using ClassNex.Core.Abstractions;
using ClassNex.Models;
using ClassNex.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static ClassNex.WinUI.NativeSettingsKeys;

namespace ClassNex.WinUI;

public sealed partial class NativeSettingsWindow
{
    private Action? _leavePlugins;
    private CancellationTokenSource? _releaseCheck;
    private static readonly HttpClient Releases = new() { Timeout = TimeSpan.FromSeconds(12) };

    private void SchedulePage()
    {
        StatusRow("ScheduleFile", "当前档案", _ => Or(_bridge.Status(NativeSettingsKeys.Schedule), "尚未选择档案"));
        StatusRow("SchedulePath", "文件位置", s => Or(s.TimetablePath, SettingsService.TimetableStoragePath));
        var today = Target("TodayCourses", "今日课程", Text(""));
        Section("今天的课表"); _body.Children.Add(today);
        _bindings.Add(_ =>
        {
            var day = _bridge.ReadOverlay().Today;
            var summary = day.IsEmpty ? "今天没有安排课程。" : string.Join('\n', day.Slots.Select(slot => $"{slot.TimeRange}　{Or(slot.Subject, slot.DisplayName)}{(string.IsNullOrWhiteSpace(slot.Detail) ? "" : "　" + slot.Detail)}"));
            if (today.Text != summary) today.Text = summary;
        });
        Section("编辑档案");
        CommandRow("ProfileInfo", "档案信息", "管理档案", ProfileTab(-1));
        CommandRow("ProfileWeek", "周课表", "编辑课程", ProfileTab(0));
        CommandRow("ProfileTimes", "时间表与课间", "编辑时间表", ProfileTab(1));
        CommandRow("ProfileSubjects", "科目", "管理科目", ProfileTab(2));
        CommandRow("ProfileSwap", "临时调课", "调课", ProfileTab(3));
        AsyncRow("ProfileOpenFile", "切换档案", "打开课表文件…", PickTimetable);
        ActionRow("ProfileFolder", "本机数据", "打开数据目录", () => RunUserAction(() => OpenFolder(SettingsService.DataDirectory)));
    }

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
                var toggle = new ToggleSwitch { Header = "启用插件", IsOn = plugin.Enabled, IsEnabled = plugin.CanToggle, OnContent = "开", OffContent = "关" };
                Identify(toggle, "PluginEnabled_" + plugin.Id, "启用 " + plugin.Name);
                toggle.Toggled += async (_, _) => { if (host != null && plugin.CanToggle && plugin.Enabled != toggle.IsOn) await Change(() => host.SetEnabledAsync(plugin.Id, toggle.IsOn)); };
                group.Children.Add(toggle);
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
        StatusRow("InstalledVersion", "当前版本", _ => Or(_bridge.Status(NativeSettingsKeys.Version), "未提供版本号"));
        var check = Target("CheckUpdate", "检查更新", new Button { Content = "检查更新" });
        var result = Target("UpdateResult", "版本检查结果", Text(""));
        result.Visibility = Visibility.Collapsed;
        Row("GitHub 发布记录", "手动检查时连接 ClassNEX 官方仓库，不会自动安装。", check);
        _body.Children.Add(result);
        CommandRow("OpenReleases", "发布包与更新说明", "打开发布页", ReleasePage);
        var page = _page;
        check.Click += async (_, _) =>
        {
            if (_releaseCheck != null || _closed) return;
            using var cts = new CancellationTokenSource();
            _releaseCheck = cts;
            check.IsEnabled = false; result.Text = "正在检查…"; result.Visibility = Visibility.Visible;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/ClassNex/ClassNEX/releases?per_page=1");
                request.Headers.UserAgent.ParseAdd("ClassNEX-Desktop/1.0");
                using var response = await Releases.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();
                using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cts.Token), cancellationToken: cts.Token);
                if (_closed || _page != page) return;
                var entries = json.RootElement;
                var tag = entries.ValueKind == JsonValueKind.Array && entries.GetArrayLength() > 0 && entries[0].TryGetProperty("tag_name", out var value) ? value.GetString() : null;
                var installed = _bridge.Status(NativeSettingsKeys.Version);
                var same = tag != null && (tag.Equals(installed, StringComparison.OrdinalIgnoreCase) || tag.Equals(installed + "_Alpha", StringComparison.OrdinalIgnoreCase));
                result.Text = tag == null ? "仓库还没有发布记录。" : same ? $"当前版本已与发布页一致（{tag}）。" : $"发布页版本：{tag}。可在发布页查看说明并下载。";
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or JsonException or InvalidOperationException)
            {
                if (!_closed && _page == page && !cts.IsCancellationRequested) result.Text = "未能获取发布记录。可以重试，或打开官方发布页。";
            }
            finally { if (ReferenceEquals(_releaseCheck, cts)) _releaseCheck = null; if (!_closed && _page == page) check.IsEnabled = true; }
        };
    }
}
