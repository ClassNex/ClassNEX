using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using AvaloniaFluentUI.Controls;
using ClassNex.Services;
using ClassNex.Styles;

namespace ClassNex.Views;

public partial class SettingsWindow
{
    private sealed record Destination(string Key, string Title, Control? Page, NavigationViewItem Item, IImage Icon)
    {
        public override string ToString() => Title;
    }

    private sealed record SearchEntry(string Title, Destination Destination, Control? Target = null, SettingsExpander? Group = null, int? WidgetTab = null, string? WidgetType = null, string? WidgetSetting = null)
    {
        public string PageName => Destination.Title;
        public IImage Icon => Destination.Icon;
    }

    private readonly Dictionary<string, Destination> _destinations = new(StringComparer.Ordinal);
    private readonly Stack<string> _history = new();
    private readonly List<SearchEntry> _searchIndex = [];
    private Destination? _current;
    private bool _goingBack;
    private int _scrollRequest;

    private static IImage SettingsNavigationIcon(string key) => SettingsIcons.Navigation(key);

    private void InitSettingsNavigation()
    {
        foreach (var item in NavView.MenuItems.Concat(NavView.FooterMenuItems).OfType<NavigationViewItem>())
        {
            var key = (string)item.Tag!;
            Control? section = key switch
            {
                "home" => PageHome, "basic" => PageGeneral, "appearance" => PageInterface,
                "window" => PageWindow, "widgets" => PageWidgets, "nex" => PageNex,
                "notification" => PageNotification, "account" => PageAccount, "about" => PageAbout,
                "debug" => PageDebug, "settings" => null,
                _ => throw new InvalidOperationException($"未注册的设置入口：{key}"),
            };
            var icon = SettingsNavigationIcon(key);
            item.IconSource = new ImageIconSource { Source = icon };
            var dest = new Destination(key, (string)item.Content!, section, item, icon);
            _destinations.Add(key, dest);
            _searchIndex.Add(new SearchEntry(dest.Title, dest));
            if (section is null || key == "home")
                continue;
            foreach (var group in section.GetLogicalDescendants().OfType<SettingsExpander>())
            {
                if (group.Header is string title)
                    _searchIndex.Add(new SearchEntry(title, dest, group, WidgetTab: group.GetLogicalAncestors().Contains(WidgetEditor) ? 1 : null));
                foreach (var row in group.Items.OfType<SettingsExpanderItem>())
                    if (row.Content is string text)
                        _searchIndex.Add(new SearchEntry(text, dest, row, group));
            }
        }
        var contentIndex = 0;
        foreach (var dest in _destinations.Values)
            if (dest.Page is { } page)
                PageHost.Children.Move(PageHost.Children.IndexOf(page), contentIndex++);
        _searchIndex.Add(new SearchEntry("组件库（添加组件）", _destinations["widgets"], WidgetLibraryPanel, WidgetTab: 0));
        _searchIndex.Add(new SearchEntry("组件设置（排序、启用、字号）", _destinations["widgets"], WidgetEditor, WidgetTab: 1));
        _searchIndex.Add(new SearchEntry("主题（浅色 / 深色 / 跟随系统）", _destinations["appearance"], ThemeCombo));
        _searchIndex.Add(new SearchEntry("组件列表（上移、下移、删除）", _destinations["widgets"], WidgetList));
        _searchIndex.Add(new SearchEntry("恢复默认布局", _destinations["widgets"], ResetWidgetButton, WidgetTab: 0));
        _searchIndex.Add(new SearchEntry("时钟组件显示秒数", _destinations["widgets"], WidgetType: "clock", WidgetSetting: "显示秒数"));
        _searchIndex.Add(new SearchEntry("自定义文本占位符", _destinations["widgets"], WidgetType: "text", WidgetSetting: "文本内容"));
        _searchIndex.Add(new SearchEntry("浏览器登录与注册", _destinations["account"], AccountLoginButton));
        _searchIndex.Add(new SearchEntry("课前提醒", _destinations["notification"], PageNotification));
        _searchIndex.Add(new SearchEntry("版本信息", _destinations["about"], AboutVersionText));
        SearchResults.ItemsSource = _searchEntries;
        NavView.SelectionChanged += (_, _) =>
        {
            if (NavView.SelectedItem is NavigationViewItem { Tag: string key } && _destinations.TryGetValue(key, out var dest))
                DisplayDestination(dest);
        };
        SectionPicker.SelectionChanged += (_, _) =>
        {
            if (SectionPicker.SelectedItem is not Destination section)
                return;
            SectionPicker.SelectedIndex = -1;
            if (_current?.Key == "settings" && section.Page is { } target)
                ScrollToSection(target);
            else
                NavigateTo(section.Key);
        };
        SearchBox.TextChanged += (_, _) => UpdateSearchResults();
        SearchBox.GotFocus += (_, _) => UpdateSearchResults();
        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        SearchResults.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        SearchResults.Tapped += (_, _) => ActivateSearchResult(SearchResults.SelectedItem as SearchEntry);
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.F && args.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                args.Handled = true;
            }
            else if (args.Key == Key.Left && args.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                NavigateBack();
                args.Handled = true;
            }
        };
    }

    private void RefreshDebugVisibility()
    {
        DebugNavItem.IsVisible = AppServices.Settings.IsDebugOptionsEnabled;
        SectionPicker.ItemsSource = _destinations.Values.Where(x => x.Page != null && x.Key != "home" && x.Item.IsVisible).ToArray();
    }

    public void NavigateTo(string page)
    {
        var key = page switch { "general" => "basic", "interface" => "appearance", _ => page };
        if (!_destinations.TryGetValue(key, out var dest) || !dest.Item.IsVisible)
            dest = _destinations["home"];
        NavView.SelectedItem = dest.Item;
        DisplayDestination(dest);
    }

    private void DisplayDestination(Destination dest)
    {
        if (_current == dest || !dest.Item.IsVisible)
            return;
        var previous = _current;
        if (previous != null && !_goingBack)
            _history.Push(previous.Key);
        _current = dest;
        _scrollRequest++;
        BackButton.IsVisible = _history.Count > 0;
        SearchPopup.IsOpen = false;
        var overview = dest.Key == "settings";
        var home = dest.Key == "home";
        PageHeaderHost.IsVisible = !home;
        SettingsIdentity.IsVisible = overview;
        PageTitleText.Text = dest.Title;
        PageHost.Margin = home ? default : new Thickness(32, 0, 32, 40);
        foreach (var section in _destinations.Values)
            if (section.Page is { } page)
                page.IsVisible = section == dest || (overview && section.Key != "home" && section.Item.IsVisible);
        PageScroll.Offset = default;
        if (home)
            RefreshSettingsDashboardState();
        if (overview || dest.Key is "nex" or "debug")
            RefreshNexPage();
        if (dest.Key == "debug" || (overview && DebugNavItem.IsVisible))
            RefreshNexApiFields();
        if (previous != null && IsVisible && ElementComposition.GetElementVisual(PageHost) is { } visual)
        {
            var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
            fade.Duration = TimeSpan.FromMilliseconds(167);
            fade.InsertKeyFrame(0, 0.4f);
            fade.InsertKeyFrame(1, 1, new CubicEaseOut());
            visual.StartAnimation(nameof(visual.Opacity), fade);
        }
    }

    private void ScrollToSection(Control target)
    {
        var ticket = ++_scrollRequest;
        Dispatcher.UIThread.Post(() =>
        {
            if (ticket != _scrollRequest || !target.IsEffectivelyVisible || PageScroll.Content is not Control content)
                return;
            content.UpdateLayout();
            if (target.TranslatePoint(default, content) is { } position)
                PageScroll.Offset = new Vector(0, Math.Max(0, position.Y - 8));
        }, DispatcherPriority.Loaded);
    }

    private void OnTogglePane(object? sender, RoutedEventArgs e) => NavView.IsPaneOpen = !NavView.IsPaneOpen;

    private void OnNavigateBack(object? sender, RoutedEventArgs e) => NavigateBack();

    private void NavigateBack()
    {
        while (_history.TryPop(out var key))
        {
            if (!_destinations[key].Item.IsVisible)
                continue;
            _goingBack = true;
            try { NavigateTo(key); }
            finally { _goingBack = false; }
            break;
        }
        BackButton.IsVisible = _history.Count > 0;
    }

    private void UpdateSearchResults()
    {
        var query = SearchBox.Text?.Trim();
        var matches = string.IsNullOrEmpty(query) ? Array.Empty<SearchEntry>() : _searchIndex.Where(x => x.Destination.Item.IsVisible &&
            (x.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || x.PageName.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (!_searchEntries.SequenceEqual(matches))
        {
            _searchEntries.Clear();
            foreach (var match in matches)
                _searchEntries.Add(match);
        }
        SearchPopup.IsOpen = _searchEntries.Count > 0 && IsVisible;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Focus();
            SearchPopup.IsOpen = false;
        }
        else if ((e.Key is Key.Up or Key.Down) && _searchEntries.Count > 0 && (sender == SearchBox || sender == SearchResults))
        {
            SearchPopup.IsOpen = true;
            var index = Math.Clamp(SearchResults.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, _searchEntries.Count - 1);
            SearchResults.SelectedIndex = index;
            SearchResults.ScrollIntoView(index);
            Dispatcher.UIThread.Post(() =>
            {
                if (!SearchPopup.IsOpen || SearchResults.SelectedIndex != index) return;
                SearchResults.UpdateLayout();
                SearchResults.ContainerFromIndex(index)?.Focus(NavigationMethod.Directional);
            }, DispatcherPriority.Loaded);
        }
        else if (e.Key == Key.Enter && SearchPopup.IsOpen)
            ActivateSearchResult(SearchResults.SelectedItem as SearchEntry ?? _searchEntries.FirstOrDefault());
        else
            return;
        e.Handled = true;
    }

    private void ActivateSearchResult(SearchEntry? hit)
    {
        if (hit == null)
            return;
        SearchPopup.IsOpen = false;
        Dispatcher.UIThread.Post(() =>
        {
            SearchBox.Text = string.Empty;
            NavigateTo(hit.Destination.Key);
            if (hit.WidgetType is { } type)
            {
                var widget = _widgets.FirstOrDefault(w => w.Type == type);
                WidgetTabs.SelectedIndex = widget == null ? 0 : 1;
                if (widget != null) WidgetList.SelectedItem = widget;
                var widgetTarget = widget == null
                    ? WidgetLibraryPanel.Children.FirstOrDefault(c => Equals(c.Tag, type))
                    : WidgetSettingsPanel.Children.OfType<SettingsExpander>().FirstOrDefault(c => Equals(c.Header, hit.WidgetSetting));
                ScrollToSection(widgetTarget ?? (widget == null ? WidgetLibraryPanel : WidgetEditor));
                return;
            }
            if (hit.WidgetTab is int tab)
                WidgetTabs.SelectedIndex = tab;
            if (hit.Group is { } group)
                group.IsExpanded = true;
            if (hit.Target is { } target)
                ScrollToSection(target);
        });
    }
}
