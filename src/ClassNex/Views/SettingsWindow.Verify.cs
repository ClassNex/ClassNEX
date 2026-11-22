#if DEBUG
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Threading;
using AvaloniaFluentUI.Controls;
using ClassNex.Services;

namespace ClassNex.Views;

public partial class SettingsWindow
{
    private void StartSettingsVerification()
    {
        if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY") != "1")
            return;
        if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_SETTINGS") == "1")
            DispatcherTimer.RunOnce(async () => await VerifySettingsAsync(), TimeSpan.FromMilliseconds(600));
        else if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_SEARCH") == "1")
            DispatcherTimer.RunOnce(() =>
            {
                SearchBox.Text = "组件";
                UpdateSearchResults();
                var result = _searchEntries.FirstOrDefault(x => x.Destination.Key == "widgets");
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "_verify.log"), $"SEARCH 命中 {_searchEntries.Count} 条，目标 {result?.PageName}\n");
                ActivateSearchResult(result);
            }, TimeSpan.FromMilliseconds(200));
    }

    private async Task VerifySettingsAsync()
    {
        var report = new List<string>();
        void Check(string name, bool value) => report.Add($"{(value ? "PASS" : "FAIL")} {name}");
        var original = (Width, Height, RequestedThemeVariant);
        var editFixture = Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_EDIT_FIXTURE") == "1";
        static void RequireCleanFixture()
        {
            if (!File.Exists(Path.Combine(SettingsService.DataDirectory, ".test-fixture")) ||
                File.Exists(Path.Combine(SettingsService.DataDirectory, "account.json")) || AppServices.Settings.CentralManagement.IsEnabled)
                throw new InvalidOperationException("Writable verification needs a fresh signed-out fixture with management disabled.");
        }
        try
        {
            if (editFixture) RequireCleanFixture();
            NavigateTo("home");
            RequestedThemeVariant = ThemeVariant.Light;
            await Task.Delay(300);
            Check("one visible home on open", PageHost.Children.Count(c => c.IsVisible) == 1 && PageHome.IsVisible);
            if (AccountService.Current is { IsLoggedIn: true } user)
                Check("signed-in greeting", HomeGreetingText.Text?.EndsWith("，" + (string.IsNullOrWhiteSpace(user.DisplayName) ? "同学" : user.DisplayName.Trim()), StringComparison.Ordinal) == true);
            else
                Check("signed-out greeting", HomeGreetingText.Text == "Hello，请先登录");
            Check("title bar 48, left-aligned search 320", TitleSearchArea.Bounds.Height == 48 && SearchBox.Bounds.Width == 320 && SearchBox.Bounds.X <= 1);
            Check("navigation pane and icons", NavView.OpenPaneLength == 200 && _destinations.Values.All(d => d.Icon.Size.Width > 0));
            var iconBox = _destinations["home"].Item.GetVisualDescendants().OfType<Viewbox>().FirstOrDefault(v => v.Name == "IconBox");
            Check($"navigation icon host measures 24 (actual {iconBox?.Bounds.Height})", iconBox?.Bounds.Height == 24);
            SaveShot("_settings_home_light.png");
            RequestedThemeVariant = ThemeVariant.Dark;
            await Task.Delay(300);
            var surface = NavView.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "ContentGridBorder");
            Check("navigation body shows shell colour", surface != null && (surface.Background is null or ISolidColorBrush { Color.A: 0 }));
            SaveShot("_settings_home_dark.png");
            NavView.SelectedItem = _destinations["settings"].Item;
            await Task.Delay(350);
            Check("settings overview", SettingsIdentity.IsVisible && PageGeneral.IsVisible && PageNotification.IsVisible && !PageHome.IsVisible && PageDebug.IsVisible == DebugNavItem.IsVisible);
            Check("category picker 172", SectionPicker.Bounds.Width == 172);
            SaveShot("_settings_overview_dark.png");
            var group = _destinations["window"];
            SectionPicker.SelectedItem = group;
            await Task.Delay(180);
            Check("picker scrolls to section", PageScroll.Offset.Y > 0 && _current?.Key == "settings" && group.Page!.IsEffectivelyVisible);
            NavigateTo("account");
            await Task.Delay(180);
            Check("account opens at top", PageScroll.Offset.Y == 0 && PageAccount.IsVisible && !PageGeneral.IsVisible);
            NavigateBack();
            Check("back from account", _current?.Key == "settings");
            SearchBox.Text = "主界面缩放";
            UpdateSearchResults();
            var hit = _searchEntries.FirstOrDefault(x => x.Destination.Key == "appearance");
            Check("search matches a real setting", hit?.Target is SettingsExpander);
            ActivateSearchResult(hit);
            await Task.Delay(350);
            Check("search opens appearance", _current?.Key == "appearance" && PageInterface.IsVisible && !SearchPopup.IsOpen);
            RequestedThemeVariant = ThemeVariant.Light;
            Width = 580;
            Height = 630;
            await Task.Delay(400);
            Check("compact mode below 600", !NavView.IsPaneOpen && ThemeCombo.IsEffectivelyVisible);
            Check("narrow content fits horizontally", PageScroll.Extent.Width <= PageScroll.Viewport.Width + 1);
            Check("usable slider step buttons", PageInterface.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Content is "−" or "+").Count(b => b.Bounds.Width >= 32 && b.Bounds.Height >= 32) == 6);
            SaveShot("_settings_appearance_narrow.png");
            foreach (var width in new[] { 520, 600, 610 })
            {
                Width = width;
                await Task.Delay(170);
                Check($"appearance fits at {width}", PageScroll.Extent.Width <= PageScroll.Viewport.Width + 1 && SearchBox.Bounds.Width >= 80);
            }
            Width = 520;
            NavigateTo("home");
            await Task.Delay(350);
            Check("single-column compact home", Convert.ToInt32(DashboardEntries.Tag) == 1 && PageHost.Children.Count(c => c.IsVisible) == 1);
            SaveShot("_settings_home_narrow.png");
            DashboardNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(150);
            Check("featured cards advance", DashboardFeatureScroll.Offset.X >= 220);
            DashboardPrevious.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DashboardQuery.Text = "课堂提醒";
            await Task.Delay(100);
            Check("dashboard filter", DashboardEntries.ItemCount == 1);
            DashboardQuery.Text = string.Empty;
            await Dispatcher.UIThread.InvokeAsync(() => PageScroll.UpdateLayout(), DispatcherPriority.Background);
            PageScroll.Offset = new Vector(0, 400);
            Check($"home scrolls vertically (items={DashboardEntries.ItemCount}, offset={PageScroll.Offset.Y:0})", DashboardEntries.ItemCount > 1 && PageScroll.Offset.Y >= 200);
            NavView.SelectedItem = _destinations["widgets"].Item;
            await Task.Delay(120);
            Check("side navigation resets scroll", _current?.Key == "widgets" && PageScroll.Offset.Y == 0);
            SearchBox.Text = "深色";
            UpdateSearchResults();
            Check("theme search alias", _searchEntries.Any(x => x.Target == ThemeCombo));
            WidgetTabs.SelectedIndex = 1;
            SearchBox.Text = "恢复默认布局";
            UpdateSearchResults();
            SearchBox.Focus();
            SearchBox.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Down });
            await Task.Delay(160);
            var firstResult = SearchResults.ContainerFromIndex(0);
            Check($"down arrow focuses a real search result (selected={SearchResults.SelectedIndex}, focus={TopLevel.GetTopLevel(SearchResults)?.FocusManager?.GetFocusedElement()?.GetType().Name}, open={SearchPopup.IsOpen})", firstResult?.IsFocused == true);
            firstResult?.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Enter });
            await Task.Delay(240);
            Check("enter switches to the visible reset action", WidgetTabs.SelectedIndex == 0 && ResetWidgetButton.IsEffectivelyVisible && !SearchPopup.IsOpen);
            Check("widget move and delete search aliases", new[] { "上移", "下移", "删除" }.All(term => _searchIndex.Any(x => x.Target == WidgetList && x.Title.Contains(term))));
            if (editFixture)
            {
                RequireCleanFixture();
                NavigateTo("home");
                await Task.Delay(120);
                var star = DashboardEntries.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is DashboardEntry && b.Classes.Contains("home-star"));
                var favorite = (DashboardEntry)star.DataContext!;
                if (favorite.Favorite) star.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                star.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                DashboardFavoritesTab.IsChecked = true;
                var saved = System.Text.Json.JsonSerializer.Deserialize<DashboardPreferences>(File.ReadAllText(DashboardPreferencesPath));
                Check("favorites filter and persistence", DashboardEntries.Items.Cast<DashboardEntry>().Contains(favorite) && saved?.Favorites?.Contains(favorite.Key) == true);
                DashboardAllTab.IsChecked = true;
                star = DashboardEntries.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => ReferenceEquals(b.DataContext, favorite) && b.Classes.Contains("home-star")) ?? star;
                star.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                NavigateTo("appearance");
                var oldOpacity = OpacitySlider.Value;
                var testOpacity = oldOpacity >= 0.8 ? 0.4 : 0.9;
                OpacitySlider.Value = testOpacity;
                Check("slider writes real setting", Math.Abs(AppServices.Settings.BackgroundOpacity - testOpacity) < 0.001);
                OpacitySlider.Value = oldOpacity;
                NavigateTo("window");
                var oldToggle = ClickThroughSwitch.IsChecked;
                ClickThroughSwitch.IsChecked = oldToggle != true;
                Check("toggle writes real setting", AppServices.Settings.IsClickThrough == (oldToggle != true));
                ClickThroughSwitch.IsChecked = oldToggle;
            }
        }
        catch (Exception ex) { report.Add($"FAIL {ex}"); }
        finally
        {
            Width = original.Width;
            Height = original.Height;
            RequestedThemeVariant = original.RequestedThemeVariant;
            NavigateTo("home");
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "_settings_verify.log"), report);
        }
    }
}
#endif
