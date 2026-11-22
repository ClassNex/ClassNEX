using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;

namespace ClassNex.Views;

public partial class SettingsWindow
{
    private const double DashboardFeatureGap = 12;
    private const double DashboardMinimumCardWidth = 360;
    private readonly Dictionary<string, DashboardEntry> _dashboardEntries = new(StringComparer.Ordinal);
    private DashboardGroup[] _dashboardGroups = [];
    private readonly List<string> _dashboardFeatures = ["account", "profile", "appearance", "notification", "widgets"];
    private readonly HashSet<string> _dashboardFavorites = [];
    private Bitmap? _dashboardAvatar;
    private (string? Path, DateTime Modified) _dashboardAvatarStamp;
    private bool _dashboardInitialized;
    private bool _dashboardFavoritesOnly;
    private int _dashboardRefreshQueued;
    private int _dashboardFirstCard;
    private int _dashboardVisibleCards = 1;
    private bool _dashboardAlignmentQueued;

    private sealed record DashboardPreferences(string[]? Featured, string[]? Favorites);
    private sealed record DashboardGroup(string Title, string Description, IReadOnlyList<DashboardEntry> Entries);

    private sealed class DashboardEntry(string key, string title, string description, string action) : INotifyPropertyChanged
    {
        private string _featureKicker = title;
        private string _featureTitle = title;
        private string _featureDescription = description;
        private string _featureAction = action;
        private bool _favorite;
        private bool _showAvatar;
        private string _initial = "";
        private Bitmap? _avatar;

        public string Key { get; } = key;
        public string Title { get; } = title;
        public string Description { get; } = description;
        public IImage Icon { get; } = SettingsNavigationIcon(key);
        public string FeatureKicker { get => _featureKicker; set => Set(ref _featureKicker, value); }
        public string FeatureTitle { get => _featureTitle; set => Set(ref _featureTitle, value); }
        public string FeatureDescription { get => _featureDescription; set => Set(ref _featureDescription, value); }
        public string FeatureAction { get => _featureAction; set => Set(ref _featureAction, value); }
        public bool ShowAvatar { get => _showAvatar; set => Set(ref _showAvatar, value); }
        public string Initial { get => _initial; set => Set(ref _initial, value); }
        public Bitmap? Avatar { get => _avatar; set => Set(ref _avatar, value); }
        public string FavoriteGlyph => _favorite ? "\uE735" : "\uE734";
        public string FavoriteAction => (_favorite ? "取消收藏：" : "收藏：") + Title;
        public bool Favorite
        {
            get => _favorite;
            set
            {
                if (!Set(ref _favorite, value)) return;
                PropertyChanged?.Invoke(this, new(nameof(FavoriteGlyph)));
                PropertyChanged?.Invoke(this, new(nameof(FavoriteAction)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new(name));
            return true;
        }
    }

    private void InitializeSettingsDashboard()
    {
        if (_dashboardInitialized) return;
        _dashboardInitialized = true;
        _dashboardGroups =
        [
            new("课表与桌面", "课程、提醒和桌面课表的显示方式。",
            [
                new("profile", "今日课表", "查看当前与下一节课，在档案编辑器里调整课程和作息。", "编辑课表"),
                new("notification", "课堂提醒", "调整课前、上课、下课提醒的内容与时长。", "设置提醒"),
                new("appearance", "外观", "调整主题、字号、背景透明度和主界面缩放。", "调整外观"),
                new("window", "窗口", "设置桌面课表的位置、排列、淡化与鼠标穿透。", "设置窗口"),
                new("widgets", "主界面组件", "添加、启用或排列桌面课表中的各类组件。", "管理组件")
            ]),
            new("账户与应用", "账户资料、启动选项、助手和版本信息。",
            [
                new("account", "ClassNEX 账户", "查看头像、用户名和邮箱，管理浏览器授权与登录。", "查看账户"),
                new("basic", "基本设置", "开机时启动、学期开始日期和托盘点击行为。", "打开基本设置"),
                new("nex", "NEX Intelligence", "打开助手设置，管理对话、唤醒和默认权限。", "打开助手设置"),
                new("about", "关于 ClassNEX", "查看当前版本、项目信息、许可与欢迎向导。", "查看版本信息")
            ])
        ];
        foreach (var entry in _dashboardGroups.SelectMany(group => group.Entries))
            _dashboardEntries.Add(entry.Key, entry);
        LoadDashboardPreferences();
        foreach (var entry in _dashboardEntries.Values) entry.Favorite = _dashboardFavorites.Contains(entry.Key);
        DashboardFeatures.Tag = DashboardMinimumCardWidth;
        DisplayDashboardFeatures();
        FilterDashboard();
        ResizeDashboard(PageHome.Bounds.Width);
        AppServices.Schedule.ProfileChanged += OnDashboardSourceChanged;
        AppServices.SettingsChanged += OnDashboardSourceChanged;
        Closed += (_, _) =>
        {
            AppServices.Schedule.ProfileChanged -= OnDashboardSourceChanged;
            AppServices.SettingsChanged -= OnDashboardSourceChanged;
            _dashboardAvatar?.Dispose();
            _dashboardAvatar = null;
        };
    }

    private void OnDashboardSourceChanged()
    {
        if (Interlocked.Exchange(ref _dashboardRefreshQueued, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _dashboardRefreshQueued, 0);
            if (PageHome.IsVisible && IsVisible) RefreshSettingsDashboardState();
        }, DispatcherPriority.Background);
    }

    private void RefreshSettingsDashboardState()
    {
        InitializeSettingsDashboard();
        var now = DateTime.Now;
        var user = AccountService.Current;
        var signedIn = user is { IsLoggedIn: true };
        var name = signedIn && !string.IsNullOrWhiteSpace(user!.DisplayName) ? user.DisplayName.Trim() : "同学";
        var greeting = now.Hour switch { >= 5 and < 11 => "早上好", >= 11 and < 17 => "中午好", _ => "晚上好" };
        HomeGreetingText.Text = signedIn ? $"{greeting}，{name}" : "Hello，请先登录";
        var account = _dashboardEntries["account"];
        account.FeatureTitle = signedIn ? name : "尚未登录";
        account.FeatureDescription = signedIn ? (string.IsNullOrWhiteSpace(user!.Email) ? "当前账户未提供邮箱。" : user.Email.Trim()) : "使用浏览器登录后，这里会显示你的头像、用户名和邮箱。";
        account.FeatureAction = signedIn ? "查看账户" : "登录账户";
        account.Initial = AccountAvatar.Initial(name);
        account.ShowAvatar = signedIn;
        var path = signedIn ? AccountService.AvatarImagePath : null;
        var stamp = (path, path is null ? DateTime.MinValue : File.GetLastWriteTimeUtc(path));
        if (stamp != _dashboardAvatarStamp)
        {
            _dashboardAvatarStamp = stamp;
            var previous = _dashboardAvatar;
            _dashboardAvatar = null;
            if (path is not null)
            {
                try
                {
                    using var file = File.OpenRead(path);
                    _dashboardAvatar = Bitmap.DecodeToWidth(file, 144);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
                {
                    Trace.WriteLine($"无法载入主页头像：{error.GetType().Name}");
                }
            }
            account.Avatar = _dashboardAvatar;
            previous?.Dispose();
        }
        var today = AppServices.Time.GetTodaySummary(AppServices.Schedule.Profile, now);
        var course = today.Current ?? today.Next ?? today.Slots.LastOrDefault();
        var timetable = _dashboardEntries["profile"];
        timetable.FeatureKicker = course is null ? "今日课表" : $"今日课表 · {(today.Current is not null ? "正在上课" : today.Next is not null ? "下一节" : "最后一节")}";
        timetable.FeatureTitle = course is null ? "今天没有课程" : !string.IsNullOrWhiteSpace(course.Subject) ? course.Subject : course.DisplayName;
        timetable.FeatureDescription = course is null ? $"{now:M月d日 dddd} · 可在档案编辑器中添加课程与作息。"
            : $"{course.TimeRange}{(string.IsNullOrWhiteSpace(course.Detail) ? "" : " · " + course.Detail)}\n今日 {today.Slots.Count} 节课，已完成 {today.Slots.Count(item => item.End <= now.TimeOfDay)} 节。";
        RefreshDashboardSettingsCards();
    }

    private void RefreshDashboardSettingsCards()
    {
        var settings = AppServices.Settings;
        var appearance = _dashboardEntries["appearance"];
        appearance.FeatureTitle = settings.ThemeMode switch { "dark" => "深色主题", "light" => "浅色主题", _ => "跟随系统主题" };
        appearance.FeatureDescription = $"主界面缩放 {settings.MainWindowScale:0.##} 倍 · 背景不透明度 {settings.BackgroundOpacity:P0}";

        var reminder = _dashboardEntries["notification"];
        var options = settings.Notification;
        reminder.FeatureTitle = settings.AllowNotification ? "课堂提醒已开启" : "课堂提醒已关闭";
        var preparing = settings.AllowMinorNotification && options.IsClassOnPreparingNotificationEnabled ? $"室内课提前 {options.ClassOnBreakReminderMinutes} 分钟提醒" : "课前提醒已关闭";
        reminder.FeatureDescription = settings.AllowNotification
            ? $"{preparing}\n上课提醒：{OnOff(options.IsClassOnNotificationEnabled)} · 下课提醒：{OnOff(options.IsClassOffNotificationEnabled)}"
            : "当前不显示课前、上课和下课提醒。";

        var widgets = _dashboardEntries["widgets"];
        widgets.FeatureTitle = $"已启用 {settings.Widgets.Count(widget => widget.IsEnabled)} 个组件";
        widgets.FeatureDescription = $"布局中共有 {settings.Widgets.Count} 个组件，可调整字号和排列顺序。";

        var window = _dashboardEntries["window"];
        window.FeatureTitle = settings.Orientation == LayoutOrientation.Vertical ? "纵向排列" : "横向排列";
        window.FeatureDescription = settings.IsClickThrough ? "鼠标穿透已开启，点击会落到后方的窗口。" : "鼠标穿透已关闭，桌面课表可以接收点击。";

        var basic = _dashboardEntries["basic"];
        basic.FeatureTitle = settings.RunAtStartup ? "开机时自动启动" : "手动启动应用";
        var trayAction = settings.TrayClickBehavior switch { 1 => "打开应用设置", 2 => "打开档案编辑器", _ => "显示或隐藏主界面" };
        basic.FeatureDescription = $"单周起始日 {settings.SingleWeekStartTime:yyyy/M/d}\n点击托盘图标：{trayAction}";

        var nex = _dashboardEntries["nex"];
        nex.FeatureTitle = settings.IsNexIntelligenceEnabled ? "NEX 助手已开启" : "NEX 助手已关闭";
        nex.FeatureDescription = settings.IsNexIntelligenceEnabled
            ? $"侧边唤醒条：{OnOff(settings.IsNexEdgeBarEnabled)} · 语音唤醒：{OnOff(settings.IsNexVoiceWakeEnabled)}"
            : "可在助手设置中开启对话与唤醒。";
        _dashboardEntries["about"].FeatureTitle = VersionText.Text ?? "ClassNEX";

        static string OnOff(bool enabled) => enabled ? "开" : "关";
    }

    private static string DashboardPreferencesPath => Path.Combine(SettingsService.DataDirectory, "SettingsHome.json");

    private void LoadDashboardPreferences()
    {
        try
        {
            if (!File.Exists(DashboardPreferencesPath)) return;
            var saved = JsonSerializer.Deserialize<DashboardPreferences>(File.ReadAllText(DashboardPreferencesPath));
            if (saved is null) return;
            var featured = saved.Featured?.Where(_dashboardEntries.ContainsKey).Distinct().ToArray();
            if (featured is { Length: > 0 })
            {
                _dashboardFeatures.Clear();
                _dashboardFeatures.AddRange(featured);
            }
            if (saved.Favorites is not null) _dashboardFavorites.UnionWith(saved.Favorites.Where(_dashboardEntries.ContainsKey));
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"无法载入主页布局：{error.GetType().Name}");
        }
    }

    private void SaveDashboardPreferences()
    {
        try
        {
            SettingsService.EnsureDataDirectory();
            var path = DashboardPreferencesPath;
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new DashboardPreferences([.. _dashboardFeatures], [.. _dashboardFavorites])));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"无法保存主页布局：{error.GetType().Name}");
        }
    }

    private void DisplayDashboardFeatures()
    {
        DashboardFeatures.ItemsSource = _dashboardFeatures.Select(key => _dashboardEntries[key]).ToArray();
        ResizeDashboardFeatures(DashboardFeatureScroll.Viewport.Width);
        QueueDashboardAlignment();
    }

    private void FilterDashboard()
    {
        if (!_dashboardInitialized || DashboardEntries is null) return;
        var favoritesOnly = _dashboardFavoritesOnly;
        var query = DashboardQuery.Text?.Trim() ?? "";
        bool Matches(DashboardEntry entry) => (!favoritesOnly || entry.Favorite) && (query.Length == 0 ||
            entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || entry.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
        var groups = _dashboardGroups.Select(group => group with { Entries = group.Entries.Where(Matches).ToArray() })
            .Where(group => group.Entries.Count != 0).ToArray();
        DashboardEntries.ItemsSource = groups;
        ResizeDashboardGroups(PageHome.Bounds.Width);
        var count = groups.Sum(group => group.Entries.Count);
        DashboardCount.Text = $"{(favoritesOnly ? "收藏" : "全部设置")} · {count} 个入口";
        DashboardEmpty.IsVisible = count == 0;
        DashboardEmpty.Text = query.Length != 0 ? "没有匹配的设置入口。" : "还没有收藏。点击设置入口右侧的星标，即可在这里快速打开。";
    }

    private void OnDashboardFilterChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { IsChecked: true } selected) return;
        _dashboardFavoritesOnly = selected == DashboardFavoritesTab;
        FilterDashboard();
    }
    private void OnDashboardQueryChanged(object? sender, TextChangedEventArgs e) => FilterDashboard();
    private void OnDashboardSizeChanged(object? sender, SizeChangedEventArgs e) => ResizeDashboard(e.NewSize.Width);

    private void ResizeDashboard(double width)
    {
        if (width <= 0 || DashboardQuery is null) return;
        DashboardArtwork.Width = Math.Clamp(width - 200, 0, 750);
        DashboardArtwork.Opacity = Math.Clamp((width - 400) / 400, .42, 1);
        DashboardQuery.Width = Math.Min(240, Math.Max(110, (width - 72) * 0.44));
        ResizeDashboardGroups(width);
        var viewport = DashboardFeatureScroll.Viewport.Width;
        ResizeDashboardFeatures(viewport > 0 ? viewport : width - DashboardFeatureScroll.Margin.Left - DashboardFeatureScroll.Margin.Right);
    }

    private void ResizeDashboardGroups(double width)
    {
        var room = width - DashboardLibrary.Margin.Left - DashboardLibrary.Margin.Right;
        DashboardEntries.Tag = room >= 2 * DashboardMinimumCardWidth + DashboardFeatureGap && DashboardEntries.ItemCount > 1 ? 2 : 1;
    }

    private void ResizeDashboardFeatures(double viewport)
    {
        if (!double.IsFinite(viewport) || viewport <= 0 || _dashboardFeatures.Count == 0) return;
        var visible = Math.Clamp((int)((viewport + DashboardFeatureGap) / (DashboardMinimumCardWidth + DashboardFeatureGap)), 1, Math.Min(3, _dashboardFeatures.Count));
        var width = (viewport - (visible - 1) * DashboardFeatureGap) / visible;
        if (_dashboardVisibleCards == visible && DashboardFeatures.Tag is double previous && Math.Abs(previous - width) < .01) return;
        _dashboardVisibleCards = visible;
        DashboardFeatures.Tag = width;
        QueueDashboardAlignment();
    }

    private int DashboardLastStart => Math.Max(0, _dashboardFeatures.Count - _dashboardVisibleCards);

    private double DashboardCardOffset(int index)
    {
        if (index <= 0) return 0;
        if (index >= DashboardLastStart) return Math.Max(0, DashboardFeatureScroll.Extent.Width - DashboardFeatureScroll.Viewport.Width);
        if (DashboardFeatures.ContainerFromIndex(index) is { } container && container.Bounds.X > 0) return container.Bounds.X;
        return DashboardFeatures.Tag is double width ? index * (width + DashboardFeatureGap) : 0;
    }

    private void ScrollDashboardToCard(int index)
    {
        _dashboardFirstCard = Math.Clamp(index, 0, DashboardLastStart);
        DashboardFeatureScroll.Offset = new Vector(DashboardCardOffset(_dashboardFirstCard), 0);
        UpdateDashboardArrows();
    }

    private void QueueDashboardAlignment()
    {
        if (_dashboardAlignmentQueued) return;
        _dashboardAlignmentQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _dashboardAlignmentQueued = false;
            if (DashboardFeatureScroll.Viewport.Width > 0) ScrollDashboardToCard(_dashboardFirstCard);
        }, DispatcherPriority.Loaded);
    }

    private void OnDashboardPrevious(object? sender, RoutedEventArgs e) => StepDashboardFeature(-1);
    private void OnDashboardNext(object? sender, RoutedEventArgs e) => StepDashboardFeature(1);

    private void StepDashboardFeature(int direction)
    {
        var offset = DashboardFeatureScroll.Offset.X;
        for (var i = direction > 0 ? 1 : DashboardLastStart - 1; i >= 0 && i <= DashboardLastStart; i += direction)
            if ((DashboardCardOffset(i) - offset) * direction > 1)
            {
                ScrollDashboardToCard(i);
                return;
            }
    }

    private void OnDashboardScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (DashboardPrevious is null || DashboardNext is null || !_dashboardInitialized) return;
        if (e.ViewportDelta.X != 0 || e.ExtentDelta.X != 0)
        {
            ResizeDashboardFeatures(DashboardFeatureScroll.Viewport.Width);
            QueueDashboardAlignment();
        }
        else if (!_dashboardAlignmentQueued && e.OffsetDelta.X != 0 && DashboardFeatures.Tag is double width)
            _dashboardFirstCard = Math.Clamp((int)Math.Round(DashboardFeatureScroll.Offset.X / (width + DashboardFeatureGap)), 0, DashboardLastStart);
        UpdateDashboardArrows();
    }

    private void UpdateDashboardArrows()
    {
        var end = Math.Max(0, DashboardFeatureScroll.Extent.Width - DashboardFeatureScroll.Viewport.Width);
        DashboardPrevious.IsVisible = DashboardNext.IsVisible = DashboardLastStart > 0 && end > 1;
        DashboardPrevious.IsEnabled = DashboardFeatureScroll.Offset.X > 1;
        DashboardNext.IsEnabled = DashboardFeatureScroll.Offset.X < end - 1;
    }

    private void OnDashboardEntryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DashboardEntry entry }) return;
        if (entry.Key == "profile") App.OpenProfileEditor(0);
        else NavigateTo(entry.Key);
    }

    private void OnDashboardFeatureClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DashboardEntry { Key: "account" } } && AccountService.Current is not { IsLoggedIn: true }) App.OpenLoginWindow();
        else OnDashboardEntryClick(sender, e);
    }

    private void OnDashboardFavoriteClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { DataContext: DashboardEntry entry }) return;
        entry.Favorite = !entry.Favorite;
        if (entry.Favorite) _dashboardFavorites.Add(entry.Key); else _dashboardFavorites.Remove(entry.Key);
        SaveDashboardPreferences();
        if (_dashboardFavoritesOnly) FilterDashboard();
    }

    private void OnDashboardEditFeatures(object? sender, RoutedEventArgs e) => CreateDashboardFeaturePicker().ShowAt(DashboardEditFeatures);

    private Flyout CreateDashboardFeaturePicker()
    {
        var panel = new StackPanel { Spacing = 6, MinWidth = 224 };
        panel.Children.Add(new TextBlock { Text = "选择显示在主页顶部的大卡片", Margin = new Thickness(0, 0, 0, 6) });
        var toggles = new List<(DashboardEntry Entry, ToggleSwitch Toggle)>();
        foreach (var entry in _dashboardGroups.SelectMany(group => group.Entries))
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            row.Children.Add(new TextBlock { Text = entry.Title, VerticalAlignment = VerticalAlignment.Center });
            var toggle = new ToggleSwitch { IsChecked = _dashboardFeatures.Contains(entry.Key), OnContent = "开", OffContent = "关" };
            AutomationProperties.SetName(toggle, entry.Title);
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);
            panel.Children.Add(row);
            toggles.Add((entry, toggle));
        }
        foreach (var (entry, toggle) in toggles)
        {
            toggle.IsEnabled = _dashboardFeatures.Count > 1 || toggle.IsChecked != true;
            toggle.IsCheckedChanged += (_, _) =>
            {
                if (toggle.IsChecked == true) _dashboardFeatures.Add(entry.Key);
                else _dashboardFeatures.Remove(entry.Key);
                foreach (var (_, item) in toggles) item.IsEnabled = _dashboardFeatures.Count > 1 || item.IsChecked != true;
                DisplayDashboardFeatures();
                SaveDashboardPreferences();
            };
        }
        return new Flyout { Content = new ScrollViewer { Content = panel, MaxHeight = Math.Min(400, Math.Max(200, Bounds.Height - 160)) } };
    }
}

public sealed class SettingsDashboardArt : Control
{
    private static readonly Typeface LabelFont = new(new FontFamily("avares://ClassNex/Assets/Fonts/#HarmonyOS Sans SC"));
    private static readonly Geometry Bell = Geometry.Parse("M 11,26 C 14,24 15,22 15,16 C 15,7 29,7 29,16 C 29,22 30,24 33,26 Z M 19,29 C 20,33 24,33 25,29");
    private static readonly Geometry Wave = Geometry.Parse("M 0,23 L 15,22 25,16 37,21 49,11 62,14 75,4 88,8 100,1");
    private static readonly ArtTheme Light = new(false);
    private static readonly ArtTheme Dark = new(true);

    private sealed class ArtTheme
    {
        public readonly IBrush Paper, Edge, Bar, Ink, Soft, Grid, Shadow, Highlight, Sky, Teal, Violet;
        public readonly Pen Outline, Hair, Blueprint, Graph, BellOutline;
        public readonly FormattedText AppName, Today, Reminders;
        public ArtTheme(bool dark)
        {
            var ink = dark ? CiPalette.DashboardInkDark : CiPalette.DashboardInkLight;
            var body = dark ? CiPalette.DashboardHeroDark : CiPalette.DashboardInkDark;
            Paper = new SolidColorBrush(body);
            Edge = new SolidColorBrush(dark ? CiPalette.DashboardTileDark : CiPalette.DashboardInkLight);
            Bar = new SolidColorBrush(dark ? CiPalette.SettingsIconLight : CiPalette.SettingsIconDark);
            Ink = new SolidColorBrush(ink);
            Soft = new SolidColorBrush(ink, .45);
            Grid = new SolidColorBrush(ink, .17);
            Shadow = new SolidColorBrush(CiPalette.DashboardInkLight, dark ? .55 : .24);
            Highlight = new SolidColorBrush(CiPalette.DashboardInkDark, dark ? .30 : .75);
            Sky = new SolidColorBrush(CiPalette.DashboardSky);
            Teal = new SolidColorBrush(CiPalette.DashboardTeal);
            Violet = new SolidColorBrush(CiPalette.DashboardViolet);
            Outline = new Pen(Soft, 1);
            Hair = new Pen(Grid, 1);
            Blueprint = new Pen(new SolidColorBrush(ink, .105), .65);
            Graph = new Pen(Teal, 2.4);
            BellOutline = new Pen(Ink, 1.6);
            AppName = Label("ClassNEX", 11);
            Today = Label("今日课表", 12);
            Reminders = Label("课堂提醒", 10);
        }
        private FormattedText Label(string text, double size) => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelFont, size, Ink);
    }

    public SettingsDashboardArt()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width < 180 || Bounds.Height <= 0) return;
        var palette = ActualThemeVariant == ThemeVariant.Dark ? Dark : Light;
        var scale = Math.Min(Bounds.Width / 610, Bounds.Height / 245);
        using var canvas = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(Bounds.Width - 610 * scale, 0));
        for (var i = -16; i < 31; i++)
        {
            var x = i * 27;
            context.DrawLine(palette.Blueprint, new Point(x, 0), new Point(x + 235, 245));
            context.DrawLine(palette.Blueprint, new Point(x + 135, 0), new Point(x - 185, 245));
        }
        var artOffset = 64 / scale * Math.Clamp((600 - Bounds.Width) / 190, 0, 1);
        using var art = context.PushTransform(Matrix.CreateTranslation(0, artOffset));
        using (context.PushTransform(new Matrix(.97, -.19, .29, .94, 108, 77)))
        {
            Window(context, palette, 268, 140);
            context.DrawText(palette.AppName, new Point(12, 3));
            context.DrawText(palette.Today, new Point(15, 26));
            for (var column = 0; column < 5; column++)
            {
                var x = 16 + column * 48;
                context.DrawRectangle(palette.Grid, null, new Rect(x, 50, 20, 3), 1, 1);
                context.DrawLine(palette.Hair, new Point(x - 3, 60), new Point(x - 3, 129));
                for (var row = 0; row < 3; row++)
                {
                    var y = 63 + row * 23;
                    var brush = ((column + 2 * row) % 4) switch { 0 => palette.Teal, 1 => palette.Sky, 2 => palette.Violet, _ => palette.Grid };
                    context.DrawRectangle(brush, null, new Rect(x, y, 40, 17), 2, 2);
                    context.DrawRectangle(palette.Highlight, null, new Rect(x + 4, y + 5, 12 + (column + row) * 2, 2), 1, 1);
                }
            }
        }
        var reminderTop = Math.Max(37, 25 + 67 / scale - artOffset);
        using (context.PushTransform(new Matrix(.97, -.16, .23, .95, 385, reminderTop)))
        {
            Window(context, palette, 152, 85);
            context.DrawText(palette.Reminders, new Point(9, 4));
            context.DrawGeometry(null, palette.BellOutline, Bell);
            context.DrawRectangle(palette.Grid, null, new Rect(43, 28, 88, 4), 2, 2);
            context.DrawRectangle(palette.Grid, null, new Rect(43, 39, 52, 3), 1, 1);
            using var graph = context.PushTransform(Matrix.CreateTranslation(24, 52));
            context.DrawGeometry(null, palette.Graph, Wave);
        }
        using (context.PushTransform(new Matrix(.98, .12, -.12, .96, 400, 148)))
        {
            Window(context, palette, 155, 69);
            for (var i = 0; i < 3; i++)
            {
                context.DrawRectangle(i switch { 0 => palette.Teal, 1 => palette.Sky, _ => palette.Violet }, null, new Rect(12, 27 + i * 12, 5, 6), 1, 1);
                context.DrawRectangle(palette.Grid, null, new Rect(23, 29 + i * 12, 45 + i * 24, 3), 1, 1);
            }
        }
    }

    private static void Window(DrawingContext context, ArtTheme palette, int width, int height)
    {
        context.DrawRectangle(palette.Shadow, null, new Rect(5, 10, width, height), 5, 5);
        context.DrawRectangle(palette.Edge, null, new Rect(1.5, 3, width, height), 4, 4);
        context.DrawRectangle(palette.Paper, palette.Outline, new Rect(0, 0, width, height), 4, 4);
        context.DrawRectangle(palette.Bar, null, new Rect(2, 2, width - 4, 16), 2, 2);
        for (var i = 0; i < 3; i++) context.DrawRectangle(palette.Highlight, null, new Rect(width - 24 + i * 6, 8, 3, 3), .5, .5);
    }
}
