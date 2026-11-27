using ClassNex.Models;
using ClassNex.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using static ClassNex.WinUI.NativeSettingsKeys;

namespace ClassNex.WinUI;

public sealed partial class NativeSettingsWindow
{
    private static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private void StartupPage()
    {
        Switch("RunAtStartup", "开机自启", s => s.RunAtStartup, (s, v) => s.RunAtStartup = v, "登录 Windows 后启动 ClassNEX。");
        Switch("StartupSplash", "显示启动画面", s => s.ShowStartupSplash, (s, v) => s.ShowStartupSplash = v, "桌面课表打开时显示加载画面。");
        Switch("UrlProtocol", "注册 URL 协议", s => s.RegisterUrlProtocol, (s, v) => s.RegisterUrlProtocol = v, "允许通过 classnex:// 链接打开本机应用。");
        Section("托盘菜单");
        const string trayHelp = "单击或长按通知区域里的 ClassNEX 图标即可打开菜单。要收起或恢复桌面课表，请选择「显示 / 隐藏主界面」。";
        var gesture = Target("TrayMenu", trayHelp, SettingCaption(trayHelp));
        gesture.IsTabStop = true;
        _body.Children.Add(gesture);
    }

    private void RuntimePage()
    {
        Switch("TeachingSafeMode", "教学安全模式", s => s.TeachingSafeMode, (s, v) => s.TeachingSafeMode = v, "发生异常时使用下方的处理方式。");
        Choice("CrashHandling", "异常处理", ["显示崩溃报告", "忽略并继续", "重新启动应用"], s => s.CrashHandlingMode, (s, v) => s.CrashHandlingMode = v);
    }

    private void CentralPage()
    {
        StatusRow("CentralCode", "本机配对码", s => Or(s.CentralManagement.PairingCode, "—"));
        AsyncRow("CentralConnect", "连接此设备", "获取配对码", CentralConnect, "在集控网页登录后输入配对码。仅将配对码交给获准管理课堂的人。");
        StatusRow("CentralState", "同步状态", s => Or(s.CentralManagement.LastStatus, "未连接"));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var restoring = false;
        var drafts = ActionButton("CentralDrafts", "查看本机草稿", () => RunUserAction(() => OpenFolder(Path.Combine(SettingsService.DataDirectory, "CentralDrafts"))));
        var restore = ActionButton("CentralRestoreDraft", "恢复到本机…", () => RunUserAction(RestoreDraft));
        actions.Children.Add(drafts);
        actions.Children.Add(restore);
        var draftRow = MakeSettingRow("同步冲突", "本机未同步的更改已保存为草稿。", actions);
        Identify(draftRow, "Row_CentralDrafts", "同步冲突");
        draftRow.Visibility = Visibility.Collapsed;
        _body.Children.Add(draftRow);
        _bindings.Add(s =>
        {
            draftRow.Visibility = string.IsNullOrWhiteSpace(s.CentralManagement.LastConflictDraft) ? Visibility.Collapsed : Visibility.Visible;
            restore.Visibility = CanRestore(s.CentralManagement) ? Visibility.Visible : Visibility.Collapsed;
            restore.IsEnabled = !restoring;
        });
        AsyncRow("CentralDisconnect", "停止同步并解除设备绑定", "解除绑定", "central.unpair");

        static bool CanRestore(CentralManagementSettings s) => s.IsEnabled && !string.IsNullOrWhiteSpace(s.ClientId) &&
            !string.IsNullOrWhiteSpace(s.SelectedClassId) && !string.IsNullOrWhiteSpace(s.LastConflictDraft);

        static (string, string, string, long, string?) TargetDraft(CentralManagementSettings s) =>
            (s.ServerUrl, s.ClientId, s.SelectedClassId, s.AppliedRevision, s.LastConflictDraft);

        async Task RestoreDraft()
        {
            var page = _page;
            var before = _bridge.ReadSettings().CentralManagement;
            if (!CanRestore(before)) return;
            var target = TargetDraft(before);
            restoring = true;
            RefreshSettings();
            try
            {
                if (!await Confirm("将草稿恢复到本机？", "这会将草稿中的课表或设置应用到本机，并向当前班级重新同步。原草稿文件会保留。", "恢复到本机") || _page != page) return;
                var current = _bridge.ReadSettings().CentralManagement;
                if (!CanRestore(current) || target != TargetDraft(current)) throw new InvalidOperationException("班级、同步版本或草稿已变化。请核对同步状态后重试。");
                await _bridge.ExecuteAsync("central.restoreDraft");
            }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException or TimeoutException or OperationCanceledException)
            { throw new InvalidOperationException("无法完成班级同步。请检查网络连接，并查看同步状态后再试。", e); }
            catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException or YamlDotNet.Core.YamlException or FormatException)
            { throw new InvalidOperationException("草稿格式无效，不能恢复。请查看本机草稿并核对文件。", e); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { throw new InvalidOperationException("无法读取或保存本机草稿。请检查数据目录中的文件、磁盘空间和访问权限。", e); }
            finally { restoring = false; if (!_closed && _page == page) RefreshSettings(); }
        }
    }

    private void AppearancePage()
    {
        ThemeChoice("Theme", "主题");
        Section("桌面课表");
        Scale("Opacity", "主界面背景不透明度", 0, 1, .01, s => s.BackgroundOpacity, (s, v) => s.BackgroundOpacity = v, v => $"{v:P0}");
        Scale("FontScale", "全局字号缩放", .8, 2.4, .05, s => s.FontScale, (s, v) => s.FontScale = v, v => $"{v:0.00}×");
        Scale("MainScale", "主界面缩放", .5, 4, .05, s => s.MainWindowScale, (s, v) => s.MainWindowScale = v, v => $"{v:0.00}×");
        ActionRow("StandardScale", "标准缩放", "恢复标准", () => { FlushSlider(); Edit(s => { s.FontScale = 1; s.MainWindowScale = 1.9; }); });
    }

    private void WindowPage()
    {
        Choice("Orientation", "组件排列方向", ["横向", "纵向"], s => s.Orientation == LayoutOrientation.Vertical ? 1 : 0, (s, i) => s.Orientation = i == 1 ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal);
        Switch("WindowVisible", "显示桌面课表", s => s.IsMainWindowVisible, (s, v) => s.IsMainWindowVisible = v);
        Switch("Topmost", "窗口置顶", s => s.Topmost, (s, v) => s.Topmost = v);
        Switch("ClickThrough", "鼠标穿透", s => s.IsClickThrough, (s, v) => s.IsClickThrough = v, "点击直接落到桌面课表后面的窗口。");
        Scale("HoverOpacity", "鼠标移入时不透明度", .0, 1, .05, s => s.HoverOpacity, (s, v) => s.HoverOpacity = v, v => $"{v:P0}");
    }

    private void AssistantPage()
    {
        Switch("NexEnabled", "启用 NEX Intelligence", s => s.IsNexIntelligenceEnabled, (s, v) => s.IsNexIntelligenceEnabled = v);
        CommandRow("NexChat", "课堂助手", "打开对话", Chat);
        CommandRow("NexGlow", "跑马灯效果", "预览", Glow);
        string[] permissions = ["readonly", "workspace", "full"];
        Choice("NexPermission", "默认权限", ["仅可查看", "工作区内修改", "完全权限"], s => Array.IndexOf(permissions, s.NexPermission), (s, i) => s.NexPermission = permissions[i], "新的对话使用此权限。完全权限允许修改其他文件和执行命令。");
        Field("SystemPrompt", "自定义系统提示词", s => s.NexSystemPrompt, (s, v) => s.NexSystemPrompt = v, true, "留空使用内置提示词。");
    }

    private void WakePage()
    {
        Switch("VoiceWake", "语音唤醒", s => s.IsNexVoiceWakeEnabled, (s, v) => s.IsNexVoiceWakeEnabled = v);
        Switch("EdgeBar", "侧边唤醒条", s => s.IsNexEdgeBarEnabled, (s, v) => s.IsNexEdgeBarEnabled = v);
        Choice("EdgeBarSide", "唤醒条位置", ["右侧", "左侧"], s => s.NexEdgeBarSide == "left" ? 1 : 0, (s, i) => s.NexEdgeBarSide = i == 1 ? "left" : "right");
        StatusRow("VoiceState", "唤醒引擎", _ => Or(_bridge.Status(Voice), "尚未启动"));
        AsyncRow("MicTest", "麦克风诊断", "检查麦克风", Mic);
        CommandRow("SystemSpeech", "Windows 语音", "打开语音设置", Speech);
        CommandRow("SystemSound", "输入设备", "打开声音设置", Sound);
    }

    private void AccountPage()
    {
        var identity = new Grid { ColumnSpacing = 14 };
        identity.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        identity.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var photo = new PersonPicture { Width = 52, Height = 52 };
        var updatePhoto = BindPhoto(photo);
        identity.Children.Add(photo);
        var labels = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var user = Target("AccountName", "账户名称", Text("", "SubtitleTextBlockStyle"));
        var mail = Target("AccountEmail", "邮箱", Text(""));
        labels.Children.Add(user); labels.Children.Add(mail);
        Grid.SetColumn(labels, 1); identity.Children.Add(labels);
        _body.Children.Add(identity);
        var action = ActionButton("AccountAction", "浏览器登录", AccountAction);
        var switchAccount = ActionButton("SwitchAccount", "切换账户", () => _bridge.Command(SwitchIdentity));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(switchAccount);
        actions.Children.Add(action);
        _bindings.Add(_ =>
        {
            var name = _bridge.Status(AccountName);
            var signedIn = !string.IsNullOrWhiteSpace(name);
            user.Text = Or(name, "未登录");
            mail.Text = signedIn ? _bridge.Status(AccountEmail) : "";
            mail.Visibility = mail.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            updatePhoto();
            var label = signedIn ? "退出登录" : "浏览器登录";
            switchAccount.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
            if (!Equals(action.Content, label)) { action.Content = label; Identify(action, "AccountAction", label); }
        });
        Row("ClassNEX ID", "登录和注册在默认浏览器中完成。", actions);
    }

    private async void AccountAction()
    {
        var name = _bridge.Status(AccountName);
        if (string.IsNullOrWhiteSpace(name)) { _bridge.Command(Login); return; }
        var email = _bridge.Status(AccountEmail);
        var dialog = new ContentDialog { XamlRoot = _shell.XamlRoot, Title = "退出 ClassNEX ID？", Content = $"将在本机退出 {name}。本地课表和设置会保留。",
            PrimaryButtonText = "退出登录", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        Identify(dialog, "ConfirmAccountLogout", "确认退出登录");
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !_closed && _bridge.Status(AccountName) == name && _bridge.Status(AccountEmail) == email)
            _bridge.Command(Logout);
    }

    private void AboutPage()
    {
        var identity = new StackPanel { Spacing = 2 };
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app-mark.png");
        FrameworkElement icon = File.Exists(iconPath) ? new Image { Width = 48, Height = 48, Source = new BitmapImage(new Uri("ms-appx:///Assets/app-mark.png")) }
            : new FontIcon { FontSize = 36, Glyph = "\uE787" };
        Identify(icon, "AboutProductMark", "ClassNEX 图标");
        var name = Text("ClassNEX", "SubtitleTextBlockStyle");
        name.VerticalAlignment = VerticalAlignment.Center;
        var version = ActionButton("Version", "当前版本", OnVersionClicked);
        version.Style = ButtonStyle("SubtleButtonStyle");
        version.Padding = new Thickness(8, 2, 8, 2);
        ToolTipService.SetToolTip(version, "本机桌面版本");
        _bindings.Add(_ => { var value = Or(_bridge.Status(NativeSettingsKeys.Version), "未提供版本号"); if (!Equals(version.Content, value)) version.Content = value; });
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(name); title.Children.Add(version);
        identity.Children.Add(title);
        identity.Children.Add(SettingCaption("面向班级的桌面课表与上下课提醒"));
        var update = ActionButton("AboutUpdate", "查看更新", () => NavigateTo("update"));
        _body.Children.Add(SummaryCard(icon, identity, update));
        Section("支持与许可");
        _body.Children.Add(NavigationCard("AboutRepo", "项目主页", "\uE774", SettingCaption("在 GitHub 查看 ClassNEX 项目"), () => _bridge.Command(Repo), true));
        _body.Children.Add(NavigationCard("AboutIssues", "反馈问题", "\uE939", SettingCaption("报告问题或提出功能建议"), () => _bridge.Command(Issues), true));
        _body.Children.Add(NavigationCard("AboutLicense", "开源许可", "\uE8A5", SettingCaption("ClassNEX 与随附资源的许可说明，可离线查看"), ShowLicenses));
        _body.Children.Add(NavigationCard("AboutPrivacy", "隐私与安全性", "\uE72E", SettingCaption("查看本机数据和联网权限"), () => NavigateTo("privacy")));
        Section("使用帮助");
        CommandRow("OobePreview", "首次启动向导", "查看向导", Oobe, "不会重置账户、课表或偏好。");
        if (_bridge.ReadSettings().IsDebugOptionsEnabled) ActionRow("AboutDebug", "开发者选项", "打开", () => NavigateTo("debug"));
    }

    private ContentDialog? _licenseDialog;

    private async void ShowLicenses()
    {
        if (_licenseDialog is not null || _closed) return;
        (string Label, string File)[] notices =
        [
            ("ClassNEX · GNU GPL v3", "ThirdParty/ClassIsland/GPL-3.0.txt"),
            ("课堂提醒 · 移植说明", "ThirdParty/ClassIsland/NOTICE.md"),
            ("FluentAvalonia", "ThirdParty/FluentAvalonia/LICENSE.txt"),
            ("Fluent System Icons", "Licenses/Fluent-LICENSE.txt"),
            ("Lucide", "ThirdParty/ClassIsland/Lucide-LICENSE.txt"),
            ("HarmonyOS Sans", "Assets/Fonts/HarmonyOS-Sans-LICENSE.txt")
        ];
        var selection = new ComboBox { ItemsSource = notices.Select(n => n.Label).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
        var text = new TextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
        var scroll = new ScrollViewer { Content = text, Height = Math.Clamp(_shell.ActualHeight * .4, 100, 360) };
        var content = new StackPanel { Spacing = 12 }; content.Children.Add(selection); content.Children.Add(scroll);
        Identify(selection, "LicenseSelection", "选择许可"); Identify(text, "LicenseText", "许可正文");
        var dialog = new ContentDialog { XamlRoot = _shell.XamlRoot, Title = "开源许可", Content = content, CloseButtonText = "关闭" };
        _licenseDialog = dialog;
        selection.SelectionChanged += async (_, _) =>
        {
            var index = selection.SelectedIndex;
            if (index < 0) return;
            string notice;
            try { notice = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, notices[index].File)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { notice = "未能读取本机随附的许可文件。可从「项目主页」查看源码与许可。"; }
            if (_licenseDialog != dialog || selection.SelectedIndex != index) return;
            text.Text = notice; scroll.ChangeView(null, 0, null, true);
        };
        selection.SelectedIndex = 0;
        try { await dialog.ShowAsync(); }
        finally { _licenseDialog = null; }
    }

    private void StoragePage()
    {
        ActionRow("PrivacyData", "课表与设置", "打开数据目录", () => RunUserAction(() => OpenFolder(SettingsService.DataDirectory)), "课表、设置和本机账户资料保存在应用的数据目录。");
        ActionRow("PrivacyLogs", "运行日志", "打开日志目录", () => RunUserAction(() => OpenFolder(SettingsService.LogsDirectory)), "发生异常时可查看本地日志。发送给他人前请检查内容。");
    }

    private void PermissionsPage()
    {
        ActionRow("PrivacyAccount", "ClassNEX ID", "管理账户", () => NavigateTo("account"), "通过浏览器登录后，从账户服务读取昵称、邮箱和头像。");
        ActionRow("PrivacyCentral", "设备集控", "查看集控", () => NavigateTo("central"), "连接集控后，会与配置的服务器交换设备状态、课表和可同步的设置。");
        ActionRow("PrivacyNex", "课堂助手与语音", "管理助手", () => NavigateTo("nex"), "使用助手时，对话会发送到配置的模型服务。启用语音唤醒会使用麦克风。");
        ActionRow("PrivacyPlugins", "本机插件", "查看插件", () => NavigateTo("plugins"), "启用的插件运行在 ClassNEX 进程中。只安装你信任的扩展。");
    }

    private async void OnVersionClicked()
    {
        if (_bridge.ReadSettings().IsDebugOptionsEnabled || ++_versionClicks < 10) return;
        _versionClicks = 0;
        const string phrase = "我知晓我所做的行为开发者不对此负责";
        var input = new TextBox { Header = "输入以下文字以启用调试菜单", PlaceholderText = phrase };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Text("调试菜单可测试提醒、配置服务接口，仅供开发使用。"));
        body.Children.Add(Text(phrase));
        body.Children.Add(input);
        var dialog = new ContentDialog { XamlRoot = _shell.XamlRoot, Title = "启用调试菜单", Content = body, PrimaryButtonText = "继续", CloseButtonText = "取消", IsPrimaryButtonEnabled = false };
        input.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = input.Text.Trim() == phrase;
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !_closed && input.Text.Trim() == phrase)
        {
            Edit(s => s.IsDebugOptionsEnabled = true);
            NavigateTo("debug");
        }
    }

    private void DebugPage()
    {
        Section("通知测试");
        CommandRow("NotifyTest", "拉起测试提醒", "测试", TestReminder);
        CommandRow("NotifyPrepare", "课前准备提醒", "触发课前提醒", Prepare);
        CommandRow("NotifyOn", "上课提醒", "触发上课提醒", ClassOn);
        CommandRow("NotifyOff", "下课提醒", "触发下课提醒", ClassOff);
        Section("NEX Intelligence 接口");
        Field("NexUrl", "服务地址", s => s.NexApiBaseUrl, (s, v) => s.NexApiBaseUrl = v.Trim(), detail: "留空使用内置服务。");
        var key = Target("NexKey", "接口密钥", new PasswordBox { PasswordRevealMode = PasswordRevealMode.Hidden });
        _bindings.Add(s => key.PlaceholderText = string.IsNullOrEmpty(s.NexApiKey) ? "输入接口密钥" : "已保存自定义密钥；输入以替换");
        void CommitKey()
        {
            if (_closed || string.IsNullOrWhiteSpace(key.Password) || !_targets.TryGetValue("NexKey", out var current) || current != key) return;
            var secret = key.Password.Trim();
            key.Password = "";
            Edit(s => s.NexApiKey = secret);
        }
        key.LostFocus += (_, _) => CommitKey();
        _fieldCommits.Add(CommitKey);
        Row("接口密钥", "", key);
        Field("NexModel", "模型", s => s.NexModel, (s, v) => s.NexModel = v.Trim());
        var models = Target("ModelChoices", "可用模型", new ComboBox { MinWidth = 154, MaxWidth = 240 });
        var choicesRow = MakeSettingRow("可用模型", "", models);
        choicesRow.Visibility = Visibility.Collapsed;
        models.SelectionChanged += (_, _) => { if (models.SelectedItem is string selected) Edit(s => s.NexModel = selected); };
        static string[] ModelNames(string result) => result.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        AsyncRow("FetchModels", "模型列表", "拉取模型", NativeSettingsKeys.Models, complete: result =>
        {
            var names = ModelNames(result);
            models.ItemsSource = names;
            choicesRow.Visibility = names.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }, summary: result => $"找到 {ModelNames(result).Length} 个模型");
        _body.Children.Add(choicesRow);
        AsyncRow("TestNex", "连接诊断", "测试连接", TestNex);
        ActionRow("ResetNex", "恢复内置配置", "恢复内置接口", () => Edit(s => { s.NexApiBaseUrl = ""; s.NexApiKey = ""; s.NexModel = ""; }));
        ActionRow("HideDebug", "调试菜单", "隐藏调试菜单", () => Edit(s => s.IsDebugOptionsEnabled = false));
    }
}
