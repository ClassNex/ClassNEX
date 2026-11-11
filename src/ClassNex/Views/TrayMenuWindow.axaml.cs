using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
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

    public TrayMenuWindow()
    {
        InitializeComponent();
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

    public void PrepareForOpen()
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

    public void PlayOpenAnimation()
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

    public void CloseAnimated()
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
            button.Classes.Add("TrayMenuItem");
            ClassNex.Styles.MetroTilt.Attach(button);
            button.Click += (_, _) =>
            {
                CloseAnimated();
                item.Action();
            };

            ItemsPanel.Children.Add(button);
        }
    }
}
