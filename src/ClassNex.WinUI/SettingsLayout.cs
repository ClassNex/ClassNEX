using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using static ClassNex.WinUI.NativeSettingsKeys;

namespace ClassNex.WinUI;

public sealed partial class NativeSettingsWindow
{
    private void BuildPane()
    {
        var header = new StackPanel { MinWidth = _navigation.CompactPaneLength - 12, Spacing = 8, Margin = new Thickness(6, 2, 6, 10) };
        var identity = new Grid();
        identity.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        identity.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var label in new[] { _identityName, _identityEmail })
        {
            label.TextWrapping = TextWrapping.NoWrap;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            _identityLabels.Children.Add(label);
        }
        identity.Children.Add(_identityPhoto);
        Grid.SetColumn(_identityLabels, 1);
        identity.Children.Add(_identityLabels);
        _identityButton.Content = identity;
        _identityButton.Style = ButtonStyle("SubtleButtonStyle");
        _identityButton.HorizontalAlignment = _identityButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _identityButton.MinHeight = 46;
        _identityButton.Padding = new Thickness(2, 4, 2, 4);
        _identityButton.Click += (_, _) => NavigateTo("account");
        Identify(_identityName, "SidebarAccountName", "ClassNEX 账户名称");
        Identify(_identityEmail, "SidebarAccountEmail", "账户邮箱");
        Identify(_identityPhoto, "SidebarAccountPhoto", "账户头像");
        header.Children.Add(_identityButton);
        _search.QueryIcon = new SymbolIcon(Symbol.Find);
        _searchCompact.Content = new FontIcon { Glyph = "\uE721", FontSize = 16 };
        _searchCompact.Style = ButtonStyle("SubtleButtonStyle");
        _searchCompact.Padding = new Thickness(0);
        _searchCompact.HorizontalContentAlignment = HorizontalAlignment.Center;
        _searchCompact.VerticalContentAlignment = VerticalAlignment.Center;
        _searchCompact.HorizontalAlignment = HorizontalAlignment.Stretch;
        Identify(_searchCompact, "CompactSettingsSearch", "查找设置");
        ToolTipService.SetToolTip(_searchCompact, "查找设置");
        _searchCompact.Click += (_, _) => OpenSearch();
        header.Children.Add(_search);
        header.Children.Add(_searchCompact);
        _navigation.PaneCustomContent = header;
        void UpdatePaneHeader()
        {
            var full = _navigation.IsPaneOpen;
            _identityLabels.Visibility = _search.Visibility = full ? Visibility.Visible : Visibility.Collapsed;
            _searchCompact.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
        }
        _navigation.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, (_, _) => UpdatePaneHeader());
        UpdatePaneHeader();
        var updatePhoto = BindPhoto(_identityPhoto);
        _refreshIdentity = () =>
        {
            var account = _bridge.Status(AccountName);
            var name = Or(account, "未登录");
            var email = account.Length == 0 ? "登录 ClassNEX ID" : _bridge.Status(AccountEmail);
            if (_identityName.Text != name || _identityEmail.Text != email)
            {
                _identityName.Text = name;
                _identityEmail.Text = email;
                _identityEmail.Visibility = email.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                var label = account.Length == 0 ? "未登录，前往账户登录" : name + "，查看账户";
                Identify(_identityButton, "SidebarAccount", label);
                ToolTipService.SetToolTip(_identityButton, label);
            }
            updatePhoto();
        };
    }

    private void OpenSearch()
    {
        SetPaneFromUser(true);
        DispatcherQueue.TryEnqueue(() => { if (!_closed) _search.Focus(FocusState.Keyboard); });
    }

    private static Style? ButtonStyle(string key) => Application.Current.Resources.TryGetValue(key, out var style) ? style as Style : null;

    private Action BindPhoto(PersonPicture photo)
    {
        (string Name, string Email, string Path, long Stamp)? previous = null;
        var generation = 0;
        return () =>
        {
            var name = _bridge.Status(AccountName);
            var email = name.Length == 0 ? "" : _bridge.Status(AccountEmail);
            var path = name.Length == 0 ? "" : _bridge.Status(AccountAvatar);
            long stamp = 0;
            if (path.Length != 0)
                try { if (File.Exists(path)) stamp = File.GetLastWriteTimeUtc(path).Ticks; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            var state = (name, email, path, stamp);
            if (previous == state) return;
            previous = state;
            var ticket = ++generation;
            photo.ProfilePicture = null;
            photo.DisplayName = name;
            if (stamp != 0) _ = Load(path, ticket);
        };
        async Task Load(string path, int ticket)
        {
            try
            {
                await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
                using var stream = file.AsRandomAccessStream();
                var image = new BitmapImage { DecodePixelWidth = 128 };
                await image.SetSourceAsync(stream);
                if (!_closed && generation == ticket) photo.ProfilePicture = image;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException or NotSupportedException) { }
        }
    }

    private static Grid SummaryCard(FrameworkElement icon, StackPanel text, Button action)
    {
        var grid = new Grid { Style = SettingSurface, Padding = new Thickness(18), ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        text.VerticalAlignment = icon.VerticalAlignment = action.VerticalAlignment = VerticalAlignment.Center;
        action.HorizontalAlignment = HorizontalAlignment.Right;
        grid.Children.Add(icon);
        Grid.SetColumn(text, 1); grid.Children.Add(text);
        Grid.SetColumn(action, 2); grid.Children.Add(action);
        bool? previous = null;
        void Arrange(double width)
        {
            var stacked = width < 440;
            if (stacked == previous) return;
            previous = stacked;
            Grid.SetColumnSpan(text, stacked ? 2 : 1);
            Grid.SetRow(action, stacked ? 1 : 0);
            Grid.SetColumn(action, stacked ? 1 : 2);
            Grid.SetColumnSpan(action, stacked ? 2 : 1);
            action.HorizontalAlignment = stacked ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            grid.RowSpacing = stacked ? 12 : 0;
        }
        grid.SizeChanged += (_, args) => Arrange(args.NewSize.Width);
        return grid;
    }

    private static void SetCards(Grid grid, IReadOnlyList<FrameworkElement> cards, double columnMinimum = 300)
    {
        foreach (var card in cards) grid.Children.Add(card);
        bool? layout = null;
        void Arrange(double width)
        {
            var wide = cards.Count > 1 && width >= 2 * columnMinimum + 8;
            if (layout == wide) return;
            layout = wide;
            var columns = wide ? 2 : 1;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            grid.ColumnSpacing = wide ? 8 : 0;
            grid.RowSpacing = wide ? 8 : 4;
            for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < cards.Count; i++)
            {
                if (i % columns == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                Grid.SetRow(cards[i], i / columns); Grid.SetColumn(cards[i], i % columns);
            }
        }
        grid.SizeChanged += (_, args) => Arrange(args.NewSize.Width);
        Arrange(0);
    }
}
