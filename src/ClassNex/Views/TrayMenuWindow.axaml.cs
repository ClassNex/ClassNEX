using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClassNex.Views;

/// <summary>
/// 托盘左键菜单：原生托盘菜单（NativeMenu）只能通过右键触发，
/// 所以左键点击改为弹出这个无边框 Fluent 菜单窗（失焦即关闭，行为与系统菜单一致）。
/// </summary>
public partial class TrayMenuWindow : Window
{
    public TrayMenuWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => Close();
    }

    /// <summary>菜单项：图标 + 文字 + 点击动作。IconFont 留空用 Segoe MDL2。</summary>
    public sealed record Item(string Glyph, string Header, Action Action, string? IconFont = null);

    /// <summary>分隔线标记。</summary>
    public static Item Separator { get; } = new("", "-", () => { });

    /// <summary>按顺序构建菜单项（对照 App.axaml 里原来的 NativeMenu）。</summary>
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
                        // 托盘菜单：回到上一版字号（20），图标位宽改回 16 与其他项一致，
                        // 这样文字左缘对齐（宽度不一致会把文字顶偏，看着没对齐）
                        Glyph = item.Glyph,
                        FontFamily = new FontFamily(item.IconFont ?? "Segoe MDL2 Assets"),
                        FontSize = item.IconFont is null ? 14 : 20,
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
