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
using ClassNex.Services;
using ClassNex.Styles;

namespace ClassNex.Views;

public partial class SettingsWindow
{
    private readonly List<DashboardEntry> _dashboardEntries = [];
    private readonly List<string> _dashboardFeatures = ["account", "profile", "appearance", "notification", "widgets"];
    private readonly HashSet<string> _dashboardFavorites = [];
    private Bitmap? _dashboardAvatar;
    private (string? Path, DateTime Modified) _dashboardAvatarStamp;
    private bool _dashboardInitialized;

    private sealed record DashboardPreferences(string[]? Featured, string[]? Favorites);

    private sealed class DashboardEntry(string key, string title, string description, IImage icon) : INotifyPropertyChanged
    {
        private string _featureTitle = title;
        private string _featureDescription = description;
        private bool _favorite;
        private bool _showAvatar;
        private string _initial = "";
        private Bitmap? _avatar;

        public string Key { get; } = key;
        public string Title { get; } = title;
        public string Description { get; } = description;
        public IImage Icon { get; } = icon;
        public string FeatureTitle { get => _featureTitle; set => Set(ref _featureTitle, value); }
        public string FeatureDescription { get => _featureDescription; set => Set(ref _featureDescription, value); }
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
        (string Key, string Name, string Detail)[] definitions =
        [
            ("profile", "今日课表", "查看当前与下一节课，在档案编辑器里调整课程和作息。"),
            ("account", "ClassNEX 账户", "查看头像、用户名和邮箱，管理浏览器授权与登录。"),
            ("basic", "基本设置", "开机时启动、学期开始日期和托盘点击行为。"),
            ("appearance", "外观", "调整主题、字号、背景透明度和主界面缩放。"),
            ("window", "窗口", "设置桌面课表的位置、排列、淡化与鼠标穿透。"),
            ("widgets", "主界面组件", "添加、启用或排列桌面课表中的各类组件。"),
            ("notification", "课堂提醒", "调整课前、上课、下课提醒的内容与时长。"),
            ("nex", "NEX Intelligence", "打开助手设置，管理对话、唤醒和默认权限。"),
            ("about", "关于 ClassNEX", "查看当前版本、项目信息、许可与欢迎向导。")
        ];
        foreach (var (key, title, detail) in definitions)
            _dashboardEntries.Add(new(key, title, detail, SettingsNavigationIcon(key)));
        LoadDashboardPreferences();
        foreach (var entry in _dashboardEntries) entry.Favorite = _dashboardFavorites.Contains(entry.Key);
        DisplayDashboardFeatures();
        FilterDashboard();
        ResizeDashboard(PageHome.Bounds.Width);
        AppServices.Schedule.ProfileChanged += OnDashboardScheduleChanged;
        Closed += (_, _) =>
        {
            AppServices.Schedule.ProfileChanged -= OnDashboardScheduleChanged;
            _dashboardAvatar?.Dispose();
            _dashboardAvatar = null;
        };
    }

    private void OnDashboardScheduleChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (PageHome.IsVisible && IsVisible) RefreshSettingsDashboardState();
    });

    private void RefreshSettingsDashboardState()
    {
        InitializeSettingsDashboard();
        var now = DateTime.Now;
        var user = AccountService.Current;
        var signedIn = user is { IsLoggedIn: true };
        var name = signedIn && !string.IsNullOrWhiteSpace(user!.DisplayName) ? user.DisplayName.Trim() : "同学";
        var greeting = now.Hour switch { >= 5 and < 11 => "早上好", >= 11 and < 17 => "中午好", _ => "晚上好" };
        HomeGreetingText.Text = signedIn ? $"{greeting}，{name}" : "Hello，请先登录";
        var account = _dashboardEntries.First(item => item.Key == "account");
        account.FeatureTitle = signedIn ? name : "登录 ClassNEX 账户";
        account.FeatureDescription = signedIn ? $"已登录 · {(string.IsNullOrWhiteSpace(user!.Email) ? "未提供邮箱" : user.Email.Trim())}" : "通过浏览器安全登录，自动同步头像、用户名和邮箱。";
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
        var timetable = _dashboardEntries.First(item => item.Key == "profile");
        var courseName = course is not null && !string.IsNullOrWhiteSpace(course.Subject) ? course.Subject : course?.DisplayName;
        timetable.FeatureTitle = course is null ? "今天没有课程" : $"{(today.Current is not null ? "正在上课" : today.Next is not null ? "下一节" : "今日最后一节")} · {courseName}";
        timetable.FeatureDescription = course is null ? $"{now:M月d日 dddd} · 点击编辑档案，添加课程与作息。"
            : $"{course.TimeRange}{(string.IsNullOrWhiteSpace(course.Detail) ? "" : " · " + course.Detail)}\n今日 {today.Slots.Count} 节课，已完成 {today.Slots.Count(item => item.End <= now.TimeOfDay)} 节。";
    }

    private static string DashboardPreferencesPath => Path.Combine(SettingsService.DataDirectory, "SettingsHome.json");

    private void LoadDashboardPreferences()
    {
        try
        {
            if (!File.Exists(DashboardPreferencesPath)) return;
            var saved = JsonSerializer.Deserialize<DashboardPreferences>(File.ReadAllText(DashboardPreferencesPath));
            if (saved is null) return;
            var keys = _dashboardEntries.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
            var featured = saved.Featured?.Where(keys.Contains).Distinct().ToArray();
            if (featured is { Length: > 0 })
            {
                _dashboardFeatures.Clear();
                _dashboardFeatures.AddRange(featured);
            }
            if (saved.Favorites is not null) _dashboardFavorites.UnionWith(saved.Favorites.Where(keys.Contains));
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

    private void DisplayDashboardFeatures() => DashboardFeatures.ItemsSource = _dashboardFeatures.Select(key => _dashboardEntries.First(entry => entry.Key == key)).ToArray();

    private void FilterDashboard()
    {
        if (!_dashboardInitialized || DashboardEntries is null) return;
        var favoritesOnly = DashboardFavoritesTab.IsChecked == true;
        var query = DashboardQuery.Text?.Trim() ?? "";
        var items = _dashboardEntries.Where(entry => (!favoritesOnly || entry.Favorite) && (query.Length == 0 || entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || entry.Description.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        DashboardEntries.ItemsSource = items;
        DashboardCount.Text = $"{(favoritesOnly ? "收藏" : "全部设置")} · {items.Length} 个入口";
        DashboardEmpty.IsVisible = items.Length == 0;
        DashboardEmpty.Text = query.Length != 0 ? "没有匹配的设置入口。" : "还没有收藏。点击设置卡片右上角的星标，即可在这里快速打开。";
    }

    private void OnDashboardFilterChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true }) FilterDashboard();
    }
    private void OnDashboardQueryChanged(object? sender, TextChangedEventArgs e) => FilterDashboard();
    private void OnDashboardSizeChanged(object? sender, SizeChangedEventArgs e) => ResizeDashboard(e.NewSize.Width);

    private void ResizeDashboard(double width)
    {
        if (width <= 0 || DashboardQuery is null) return;
        DashboardArtwork.Width = Math.Clamp(width - 200, 0, 750);
        DashboardArtwork.Opacity = Math.Clamp((width - 400) / 400, .42, 1);
        DashboardQuery.Width = Math.Min(240, Math.Max(110, (width - 72) * 0.44));
        var columns = Math.Max(1, (int)((width - 60) / 248));
        DashboardEntries.Tag = columns;
    }

    private void OnDashboardPrevious(object? sender, RoutedEventArgs e) => DashboardFeatureScroll.Offset = new Vector(Math.Max(0, DashboardFeatureScroll.Offset.X - 244), 0);
    private void OnDashboardNext(object? sender, RoutedEventArgs e) => DashboardFeatureScroll.Offset = new Vector(Math.Min(DashboardFeatureScroll.Extent.Width, DashboardFeatureScroll.Offset.X + 244), 0);

    private void OnDashboardScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (DashboardPrevious is null || DashboardNext is null) return;
        var end = DashboardFeatureScroll.Extent.Width - DashboardFeatureScroll.Viewport.Width;
        DashboardPrevious.IsVisible = DashboardNext.IsVisible = end > 1;
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
        if (DashboardFavoritesTab.IsChecked == true) FilterDashboard();
    }

    private void OnDashboardEditFeatures(object? sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 6, MinWidth = 224 };
        panel.Children.Add(new TextBlock { Text = "选择显示在主页顶部的大卡片", Margin = new Thickness(0, 0, 0, 6) });
        var toggles = new List<(DashboardEntry Entry, ToggleSwitch Toggle)>();
        foreach (var entry in _dashboardEntries)
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
        new Flyout { Content = new ScrollViewer { Content = panel, MaxHeight = Math.Min(400, Math.Max(200, Bounds.Height - 160)) } }.ShowAt(DashboardEditFeatures);
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
