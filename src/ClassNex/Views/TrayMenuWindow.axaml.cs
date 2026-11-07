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

    public TrayMenuWindow()
    {
        InitializeComponent();
        Opened += (_, _) => PlayOpenAnimation();
        Deactivated += (_, _) => CloseAnimated();
    }

    private void PlayOpenAnimation()
    {
        MenuRoot.Opacity = 1;
        ClassNex.Styles.MetroTransitions.PlayList(ItemsPanel);

        var visual = ElementComposition.GetElementVisual(MenuRoot);
        if (visual is null)
            return;

        var bounds = MenuRoot.Bounds;
        visual.CenterPoint = new Vector3D(bounds.Width, bounds.Height, 0);
        visual.Opacity = 0f;
        visual.Scale = new Vector3D(0.96f, 0.96f, 1f);
        visual.Offset = new Vector3D(0f, 8f, 0f);

        var compositor = visual.Compositor;

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, Easing.Parse("0.16, 1, 0.3, 1"));
        fade.Duration = TimeSpan.FromMilliseconds(180);
        visual.StartAnimation(nameof(visual.Opacity), fade);

        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, new System.Numerics.Vector3(0.96f, 0.96f, 1f));
        scale.InsertKeyFrame(1f, new System.Numerics.Vector3(1f, 1f, 1f), Easing.Parse("0.16, 1, 0.3, 1"));
        scale.Duration = TimeSpan.FromMilliseconds(280);
        visual.StartAnimation(nameof(visual.Scale), scale);

        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0f, new System.Numerics.Vector3(0f, 8f, 0f));
        slide.InsertKeyFrame(1f, new System.Numerics.Vector3(0f, 0f, 0f), Easing.Parse("0.16, 1, 0.3, 1"));
        slide.Duration = TimeSpan.FromMilliseconds(280);
        visual.StartAnimation(nameof(visual.Offset), slide);
    }

    public void CloseAnimated()
    {
        if (_closing)
            return;

        _closing = true;

        var visual = ElementComposition.GetElementVisual(MenuRoot);
        if (visual is null)
        {
            Close();
            return;
        }

        var compositor = visual.Compositor;

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 1f);
        fade.InsertKeyFrame(1f, 0f, Easing.Parse("0.4, 0, 1, 1"));
        fade.Duration = TimeSpan.FromMilliseconds(130);
        visual.StartAnimation(nameof(visual.Opacity), fade);

        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, new System.Numerics.Vector3(1f, 1f, 1f));
        scale.InsertKeyFrame(1f, new System.Numerics.Vector3(0.97f, 0.97f, 1f), Easing.Parse("0.4, 0, 1, 1"));
        scale.Duration = TimeSpan.FromMilliseconds(150);
        visual.StartAnimation(nameof(visual.Scale), scale);

        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0f, new System.Numerics.Vector3(0f, 0f, 0f));
        slide.InsertKeyFrame(1f, new System.Numerics.Vector3(0f, 6f, 0f), Easing.Parse("0.4, 0, 1, 1"));
        slide.Duration = TimeSpan.FromMilliseconds(150);
        visual.StartAnimation(nameof(visual.Offset), slide);

        DispatcherTimer.RunOnce(() =>
        {
            if (IsVisible)
                Close();
        }, TimeSpan.FromMilliseconds(150));
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
