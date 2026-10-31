using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClassNex.Views;





public partial class TrayMenuWindow : Window
{
    public TrayMenuWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => Close();
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
            button.Click += (_, _) =>
            {
                Close();
                item.Action();
            };

            ItemsPanel.Children.Add(button);
        }
    }
}
