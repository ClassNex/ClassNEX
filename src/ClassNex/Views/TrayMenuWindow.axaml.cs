using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace ClassNex.Views;


public partial class TrayMenuWindow : Window
{
    private bool _closing;
    private bool _prewarmed;
    private bool _prewarming;
    private int _closeVersion;
    internal event EventHandler? KeyboardDismissed;

    public TrayMenuWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, NavigateMenu, RoutingStrategies.Tunnel);
        Deactivated += (_, _) =>
        {
            if (!_prewarming)
                CloseAnimated();
        };
    }

    public void Prewarm()
    {
        if (_prewarmed)
            return;

        _prewarmed = true;
        _prewarming = true;

        try
        {
            Position = new PixelPoint(-10000, -10000);
            Opacity = 0;
            Show();
            UpdateLayout();
            Hide();
        }
        finally
        {
            Opacity = 1;
            _prewarming = false;
        }
    }

    internal void OpenAt(PixelRect? anchor, bool keyboard)
    {
        if (IsVisible && !_closing)
        {
            if (keyboard) ItemsPanel.Children.OfType<Button>().FirstOrDefault()?.Focus(NavigationMethod.Directional);
            return;
        }
        PrepareForOpen();
        var opening = _closeVersion;
        void Place()
        {
            var screen = (anchor is { } region ? Screens.ScreenFromPoint(region.Center) : null) ?? Screens.Primary;
            if (screen is null) return;
            var work = screen.WorkingArea;
            var origin = anchor ?? new PixelRect(work.Right - 1, work.Bottom - 1, 1, 1);
            var bounds = Bounds.Size;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            var scale = screen.Scaling;
            Position = PositionAt(origin, work, PixelSize.FromSize(bounds, scale),
                (int)Math.Round(MenuRoot.Margin.Left * scale), (int)Math.Ceiling(4 * scale));
        }
        Place();
        if (!IsVisible) Show();
        Activate();
        if (keyboard) ItemsPanel.Children.OfType<Button>().FirstOrDefault()?.Focus(NavigationMethod.Directional);
        Dispatcher.UIThread.Post(() =>
        {
            if (opening != _closeVersion || !IsVisible) return;
            Place(); PlayOpenAnimation();
        }, DispatcherPriority.Render);
    }

    internal static PixelPoint PositionAt(PixelRect icon, PixelRect work, PixelSize window, int margin, int gap)
    {
        var w = Math.Max(1, window.Width - 2 * margin); var h = Math.Max(1, window.Height - 2 * margin);
        var left = Math.Clamp(icon.Center.X - w / 2, work.X, Math.Max(work.X, work.Right - w));
        var roomAbove = icon.Y - work.Y; var roomBelow = work.Bottom - icon.Bottom;
        var top = roomAbove >= h + gap || roomAbove >= roomBelow ? icon.Y - gap - h : icon.Bottom + gap;
        return new(left - margin, Math.Clamp(top, work.Y, Math.Max(work.Y, work.Bottom - h)) - margin);
    }

    private void NavigateMenu(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; CloseAnimated(true); return; }
        if (e.Key is not (Key.Up or Key.Down or Key.Home or Key.End)) return;
        var items = ItemsPanel.Children.OfType<Button>().ToArray();
        if (items.Length == 0) return;
        var current = Array.FindIndex(items, b => b.IsKeyboardFocusWithin);
        var next = e.Key switch { Key.Home => 0, Key.End => items.Length - 1,
            Key.Up => current < 0 ? items.Length - 1 : (current + items.Length - 1) % items.Length, _ => (current + 1) % items.Length };
        e.Handled = items[next].Focus(NavigationMethod.Directional);
    }

    private void PrepareForOpen()
    {
        _closeVersion++;
        _closing = false;
        MenuRoot.Opacity = 0;

        var rootVisual = ElementComposition.GetElementVisual(MenuRoot);
        if (rootVisual is not null)
        {
            rootVisual.StopAnimation("Opacity");
            rootVisual.Opacity = 0;
        }

        foreach (var child in ItemsPanel.Children)
        {
            if (child is not Control control)
                continue;

            var visual = ElementComposition.GetElementVisual(control);
            if (visual is null)
                continue;

            visual.StopAnimation("Opacity");
            visual.StopAnimation("Scale");
            visual.StopAnimation("Offset");
            visual.Opacity = 0;
        }

        if (Bounds.Width <= 0)
            UpdateLayout();
    }

    private void PlayOpenAnimation()
    {
        if (!IsVisible || _closing)
            return;

        MenuRoot.Opacity = 1;

        var rootVisual = ElementComposition.GetElementVisual(MenuRoot);
        if (rootVisual is not null)
        {
            rootVisual.StopAnimation("Opacity");
            rootVisual.Opacity = 0;

            var fade = rootVisual.Compositor.CreateScalarKeyFrameAnimation();
            fade.Duration = TimeSpan.FromMilliseconds(140);
            fade.InsertKeyFrame(0f, 0f);
            fade.InsertKeyFrame(1f, 1f, Easing.Parse("0.16,1,0.3,1"));
            rootVisual.StartAnimation("Opacity", fade);
        }

        ClassNex.Styles.MetroTransitions.PlayTrayMenuTransition(ItemsPanel);
    }

    public void CloseAnimated(bool returnFocus = false)
    {
        if (_closing || !IsVisible)
            return;

        _closing = true;
        var version = ++_closeVersion;

        var visual = ElementComposition.GetElementVisual(MenuRoot);
        if (visual is null)
        {
            Hide();
            _closing = false;
            if (returnFocus) KeyboardDismissed?.Invoke(this, EventArgs.Empty);
            return;
        }

        visual.StopAnimation("Opacity");
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = TimeSpan.FromMilliseconds(120);
        fade.InsertKeyFrame(0f, 1f);
        fade.InsertKeyFrame(1f, 0f, Easing.Parse("0.4,0,1,1"));
        visual.StartAnimation("Opacity", fade);

        DispatcherTimer.RunOnce(() =>
        {
            if (version != _closeVersion)
                return;

            if (IsVisible)
                Hide();
            _closing = false;
            if (returnFocus) KeyboardDismissed?.Invoke(this, EventArgs.Empty);
        }, TimeSpan.FromMilliseconds(140));
    }

    
    public sealed record Item(string Glyph, string Header, Action Action, string? IconFont = null);

    
    public static Item Separator { get; } = new("", "-", () => { });

    
    public void Build(IReadOnlyList<Item> items)
    {
        ItemsPanel.Children.Clear();

        foreach (var item in items)
        {
            if (ReferenceEquals(item, Separator))
            {
                ItemsPanel.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Avalonia.Thickness(12, 4),
                    Background = (IBrush?)this.FindResource("DividerStrokeColorDefaultBrush")
                                 ?? new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)),
                });
                continue;
            }

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    new AvaloniaFluentUI.Controls.FontIcon
                    {
                        
                        
                        Glyph = item.Glyph,
                        FontFamily = new FontFamily(item.IconFont ?? "avares://ClassNex/Assets/Fonts/#lucide"),
                        FontSize = item.IconFont is null ? 14 : 14,
                        Width = 16,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock { Text = item.Header, FontSize = 13, VerticalAlignment = VerticalAlignment.Center },
                },
            };

            var button = new Button
            {
                Content = row,
                Padding = new Avalonia.Thickness(12, 7),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
                BorderThickness = new Avalonia.Thickness(0),
                CornerRadius = new Avalonia.CornerRadius(4),
            };
            AutomationProperties.SetName(button, item.Header);
            button.Classes.Add("TrayMenuItem");
            ClassNex.Styles.MetroTilt.Attach(button);
            button.Click += (_, _) =>
            {
                if (_closing) return;
                CloseAnimated();
                item.Action();
            };

            ItemsPanel.Children.Add(button);
        }
    }
}
