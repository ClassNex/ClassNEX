using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media.Imaging;
using static ClassNex.WinUI.NativeSettingsKeys;

namespace ClassNex.WinUI;

public sealed partial class NativeSettingsWindow
{
    private void BuildPane()
    {
        var header = new StackPanel { Spacing = 8, Margin = new Thickness(6, 2, 6, 10) };
        var identity = new Grid { ColumnSpacing = 10 };
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
        void Layout()
        {
            var full = _navigation.IsPaneOpen;
            header.Width = (full ? _navigation.OpenPaneLength : _navigation.CompactPaneLength) - 12;
            _identityPhoto.Width = _identityPhoto.Height = full ? 42 : 26;
            _identityButton.Padding = full ? new Thickness(8, 6, 8, 6) : new Thickness(3);
            _identityLabels.Visibility = _search.Visibility = full ? Visibility.Visible : Visibility.Collapsed;
            _searchCompact.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
        }
        _navigation.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, (_, _) => Layout());
        _navigation.Loaded += (_, _) => Layout();
        Layout();
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
        _navigation.IsPaneOpen = true;
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

    private static Border Card(StackPanel content)
    {
        var border = (Border)XamlReader.Load("""
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" CornerRadius="8" Padding="16" BorderThickness="1" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" VerticalAlignment="Top"/>
            """);
        border.Child = content;
        return border;
    }

    private Border GroupCard(string title, string glyph, Action add)
    {
        var group = new StackPanel { Spacing = 8 };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        heading.Children.Add(new FontIcon { Glyph = glyph, FontSize = 18 });
        heading.Children.Add(Text(title));
        group.Children.Add(heading);
        var parent = _body;
        _body = group;
        try { add(); } finally { _body = parent; }
        return Card(group);
    }

    private static void SetCards(Grid grid, IReadOnlyList<FrameworkElement> cards, double columnMinimum = 300)
    {
        var left = new StackPanel { Spacing = 12 };
        var right = new StackPanel { Spacing = 12 };
        grid.ColumnSpacing = 12;
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        bool? layout = null;
        void Arrange(double width)
        {
            var wide = cards.Count > 1 && width >= 2 * columnMinimum + 12;
            if (layout == wide) return;
            layout = wide;
            left.Children.Clear(); right.Children.Clear();
            grid.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            grid.ColumnSpacing = wide ? 12 : 0;
            for (var i = 0; i < cards.Count; i++) (wide && i % 2 == 1 ? right : left).Children.Add(cards[i]);
        }
        grid.SizeChanged += (_, args) => Arrange(args.NewSize.Width);
        Arrange(0);
    }
}
