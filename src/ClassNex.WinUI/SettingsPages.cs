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

    private void BasicPage()
    {
        var layout = new Grid();
        Identify(layout, "GeneralCards", "通用设置分组");
        SetCards(layout, [
            GroupCard("启动", "\uE945", () =>
            {
                Switch("RunAtStartup", "开机自启", s => s.RunAtStartup, (s, v) => s.RunAtStartup = v, "登录 Windows 后启动。");
                Switch("StartupSplash", "显示启动画面", s => s.ShowStartupSplash, (s, v) => s.ShowStartupSplash = v);
                Switch("UrlProtocol", "注册 URL 协议", s => s.RegisterUrlProtocol, (s, v) => s.RegisterUrlProtocol = v);
            }),
            GroupCard("课表与档案", "\uE787", () =>
            {
                Date("SemesterStart", "单周起始日期", s => new DateTimeOffset(s.SingleWeekStartTime.Date), (s, v) => { if (v.HasValue) s.SingleWeekStartTime = v.Value.Date; });
                ActionRow("GoToSchedule", "课表与当前档案", "管理档案", () => NavigateTo("schedule"));
            }),
            GroupCard("托盘", "\uE7F8", () => Choice("TrayBehavior", "单击托盘图标", ["显示 / 隐藏主界面", "打开应用设置", "打开档案编辑器", "打开托盘菜单"], s => s.TrayClickBehavior, (s, v) => s.TrayClickBehavior = v)),
            GroupCard("运行与恢复", "\uE9D9", () =>
            {
                Switch("TeachingSafeMode", "教学安全模式", s => s.TeachingSafeMode, (s, v) => s.TeachingSafeMode = v);
                Choice("CrashHandling", "异常处理", ["显示崩溃报告", "忽略并继续", "重新启动应用"], s => s.CrashHandlingMode, (s, v) => s.CrashHandlingMode = v);
            })
        ]);
        _body.Children.Add(layout);
    }

    private void CentralPage()
    {
        Switch("CentralEnabled", "启用集控", s => s.CentralManagement.IsEnabled, (s, v) => s.CentralManagement.IsEnabled = v,
            "在集控后台登录同一个 ClassNEX ID，然后选择要管理的设备。");
        Field("CentralUrl", "集控服务地址", s => s.CentralManagement.ServerUrl, (s, v) => s.CentralManagement.ServerUrl = v.Trim());
        StatusRow("CentralIdentity", "设备身份", s => Or(s.CentralManagement.ClientId, "尚未登记"));
        StatusRow("CentralState", "同步状态", s => Or(s.CentralManagement.LastStatus, "未配置"));
        ActionRow("CentralAccount", "ClassNEX ID", "查看账户", () => NavigateTo("account"));
        AsyncRow("CentralConnect", "连接并检查同步", "连接集控", CentralConnect, before: () =>
        {
            if (!_bridge.ReadSettings().CentralManagement.IsEnabled) Edit(s => s.CentralManagement.IsEnabled = true);
        });
        CommandRow("CentralDisconnect", "结束当前连接", "断开集控", "central.disconnect");
    }

    private void AppearancePage()
    {
        string[] modes = ["system", "light", "dark"];
        Choice("Theme", "主题", ["跟随系统", "浅色", "深色"], s => Array.IndexOf(modes, s.ThemeMode), (s, i) => s.ThemeMode = modes[i]);
        Section("桌面课表");
        Scale("Opacity", "主界面背景不透明度", .1, 1, .05, s => s.BackgroundOpacity, (s, v) => s.BackgroundOpacity = v, v => $"{v:P0}");
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

    private void NotificationPage()
    {
        Section("提醒通知");
        Switch("Notifications", "允许课堂提醒", s => s.AllowNotification, (s, v) => s.AllowNotification = v);
        Detail("PrepareDetails", "课前准备", () =>
        {
            Switch("ClassPrepare", "发出课前准备提醒", s => s.Notification.IsClassOnPreparingNotificationEnabled, (s, v) => s.Notification.IsClassOnPreparingNotificationEnabled = v);
            Number("PrepareMinutes", "室内课程 · 课间剩余分钟", 1, 60, s => s.Notification.ClassOnBreakReminderMinutes, (s, v) =>
            {
                s.Notification.ClassOnBreakReminderMinutes = v;
                s.Notification.InDoorClassPreparingDeltaTime = 60 * v;
            });
            Number("OutdoorSeconds", "室外课程 · 提前秒数", 0, 3600, s => s.Notification.OutDoorClassPreparingDeltaTime, (s, v) => s.Notification.OutDoorClassPreparingDeltaTime = v);
            Field("PreparingText", "室内课程提醒文字", s => s.Notification.ClassOnPreparingText, (s, v) => s.Notification.ClassOnPreparingText = v);
            Field("PreparingMask", "室内课程遮罩文字", s => s.Notification.ClassOnPreparingMaskText, (s, v) => s.Notification.ClassOnPreparingMaskText = v);
            Field("OutdoorText", "室外课程提醒文字", s => s.Notification.OutdoorClassOnPreparingText, (s, v) => s.Notification.OutdoorClassOnPreparingText = v);
            Field("OutdoorMask", "室外课程遮罩文字", s => s.Notification.OutdoorClassOnPreparingMaskText, (s, v) => s.Notification.OutdoorClassOnPreparingMaskText = v);
        });
        Detail("ClassOnDetails", "上课提醒", () =>
        {
            Switch("ClassOn", "上课提醒", s => s.Notification.IsClassOnNotificationEnabled, (s, v) => s.Notification.IsClassOnNotificationEnabled = v);
            Field("ClassOnMask", "上课遮罩文字", s => s.Notification.ClassOnMaskText, (s, v) => s.Notification.ClassOnMaskText = v);
        });
        Detail("ClassOffDetails", "下课提醒", () =>
        {
            Switch("ClassOff", "下课提醒", s => s.Notification.IsClassOffNotificationEnabled, (s, v) => s.Notification.IsClassOffNotificationEnabled = v);
            Field("ClassOffMask", "下课遮罩文字", s => s.Notification.ClassOffMaskText, (s, v) => s.Notification.ClassOffMaskText = v);
            Field("ClassOffText", "下课提醒文字", s => s.Notification.ClassOffOverlayText, (s, v) => s.Notification.ClassOffOverlayText = v);
        });
        Detail("DisplayDetails", "提醒显示", () =>
        {
            Switch("NotificationEffects", "提醒效果", s => s.AllowNotificationEffect, (s, v) => s.AllowNotificationEffect = v);
            Switch("MinorNotifications", "允许次要提醒", s => s.AllowMinorNotification, (s, v) => s.AllowMinorNotification = v);
            Switch("Teacher", "显示科任老师名字", s => s.Notification.ShowTeacherName, (s, v) => s.Notification.ShowTeacherName = v);
        });
    }

    private void Detail(string id, string title, Action fill)
    {
        var child = new StackPanel { Spacing = 8 };
        var expander = Target(id, title, new Expander { Header = title, Content = child, IsExpanded = false, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        var parent = _body;
        _body = child;
        _currentExpander = expander;
        try { fill(); } finally { _body = parent; _currentExpander = null; }
        parent.Children.Add(expander);
    }

    private void NexPage()
    {
        Switch("NexEnabled", "启用 NEX Intelligence", s => s.IsNexIntelligenceEnabled, (s, v) => s.IsNexIntelligenceEnabled = v);
        CommandRow("NexChat", "课堂助手", "打开对话", Chat);
        CommandRow("NexGlow", "跑马灯效果", "预览", Glow);
        string[] permissions = ["readonly", "workspace", "full"];
        Choice("NexPermission", "默认权限", ["仅可查看", "工作区内修改", "完全权限"], s => Array.IndexOf(permissions, s.NexPermission), (s, i) => s.NexPermission = permissions[i], "新的对话使用此权限。完全权限允许修改其他文件和执行命令。");
        Field("SystemPrompt", "自定义系统提示词", s => s.NexSystemPrompt, (s, v) => s.NexSystemPrompt = v, true, "留空使用内置提示词。");
        Section("唤醒方式");
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
        var identity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (File.Exists(iconPath)) identity.Children.Add(new Image { Width = 48, Height = 48, Source = new BitmapImage(new Uri(iconPath)) });
        identity.Children.Add(Text("ClassNEX", "SubtitleTextBlockStyle"));
        _body.Children.Add(identity);
        var version = ActionButton("Version", "当前版本", OnVersionClicked);
        _bindings.Add(_ => { var value = Or(_bridge.Status(NativeSettingsKeys.Version), "未提供版本号"); if (!Equals(version.Content, value)) version.Content = value; });
        Row("当前版本", "ClassNEX 桌面客户端", version);
        CommandRow("AboutRepo", "项目主页", "打开 GitHub", Repo);
        CommandRow("AboutIssues", "反馈与建议", "反馈问题", Issues);
        CommandRow("AboutLicense", "开放源代码许可", "查看许可", License, "GNU General Public License v3.0");
        ActionRow("AboutPrivacy", "隐私与安全性", "查看", () => NavigateTo("privacy"));
        ActionRow("AboutUpdate", "版本更新", "打开更新", () => NavigateTo("update"));
        CommandRow("OobePreview", "首次启动向导", "预览 OOBE", Oobe, "不会重置你的账户、课表或偏好。");
        if (_bridge.ReadSettings().IsDebugOptionsEnabled) ActionRow("AboutDebug", "开发者选项", "打开", () => NavigateTo("debug"));
    }

    private void PrivacyPage()
    {
        Section("本机数据");
        ActionRow("PrivacyData", "课表与设置", "打开数据目录", () => RunUserAction(() => OpenFolder(SettingsService.DataDirectory)), "课表、设置和本机账户资料保存在应用的数据目录。");
        ActionRow("PrivacyLogs", "运行日志", "打开日志目录", () => RunUserAction(() => OpenFolder(SettingsService.LogsDirectory)), "发生异常时可查看本地日志。发送给他人前请检查内容。");
        Section("联网与权限");
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
        var key = Target("NexKey", "接口密钥", new PasswordBox { Header = "接口密钥", PasswordRevealMode = PasswordRevealMode.Hidden });
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
        _body.Children.Add(key);
        Field("NexModel", "模型", s => s.NexModel, (s, v) => s.NexModel = v.Trim());
        var models = Target("ModelChoices", "可用模型", new ComboBox { Header = "可用模型", Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Stretch });
        models.SelectionChanged += (_, _) => { if (models.SelectedItem is string selected) Edit(s => s.NexModel = selected); };
        static string[] ModelNames(string result) => result.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        AsyncRow("FetchModels", "模型列表", "拉取模型", NativeSettingsKeys.Models, complete: result =>
        {
            var names = ModelNames(result);
            models.ItemsSource = names;
            models.Visibility = names.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }, summary: result => $"找到 {ModelNames(result).Length} 个模型");
        _body.Children.Add(models);
        AsyncRow("TestNex", "连接诊断", "测试连接", TestNex);
        ActionRow("ResetNex", "恢复内置配置", "恢复内置接口", () => Edit(s => { s.NexApiBaseUrl = ""; s.NexApiKey = ""; s.NexModel = ""; }));
        ActionRow("HideDebug", "调试菜单", "隐藏调试菜单", () => Edit(s => s.IsDebugOptionsEnabled = false));
    }
}
