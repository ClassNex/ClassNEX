using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;

namespace ClassNex.Views;










public partial class MainWindow
{
    private bool _editMode;

    
    public bool IsEditMode => _editMode;

    public void EnterEditMode()
    {
        if (_editMode)
            return;

        _editMode = true;
        RebuildWidgets();
        RefreshWidgets();
        ApplySettings();
    }

    public void ExitEditMode()
    {
        if (!_editMode)
            return;

        _editMode = false;
        RebuildWidgets();
        RefreshWidgets();
        ApplySettings();
    }

    private void OnMenuEnterEditMode(object? sender, RoutedEventArgs e) => EnterEditMode();

    
    
    
    private Control BuildComponentToolbar(WidgetConfig config)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };

        row.Children.Add(new TextBlock
        {
            Text = WidgetRegistry.DisplayNameOf(config.Type),
            FontSize = 13,
            Foreground = CiPalette.PrimaryTextBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });

        
        row.Children.Add(ToolButton("\uE154", "该组件的设置", () => App.OpenSettings("widgets")));

        
        row.Children.Add(ToolButton("\uE18E", "删除该组件", () => AppServices.Widgets.Remove(config)));

        
        var moreButton = ToolButton("\uE0B6", "更多（上移 / 下移 / 删除）", null);
        moreButton.Flyout = BuildMoreFlyout(config);
        row.Children.Add(moreButton);

        return new Border
        {
            Background = CiPalette.IslandSurfaceBrush(),
            BorderBrush = CiPalette.IslandBorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 4),
            Child = row,
        };
    }

    private static Button ToolButton(string glyph, string tip, Action? onClick)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("avares://ClassNex/Assets/Fonts/#lucide"),
                FontSize = 12,
                Foreground = CiPalette.PrimaryTextBrush(),
            },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetTip(button, tip);
        if (onClick is not null)
            button.Click += (_, _) => onClick();

        return button;
    }

    private static MenuFlyout BuildMoreFlyout(WidgetConfig config)
    {
        var flyout = new MenuFlyout();

        var up = new MenuItem { Header = "上移" };
        up.Click += (_, _) => AppServices.Widgets.Move(config, -1);
        var down = new MenuItem { Header = "下移" };
        down.Click += (_, _) => AppServices.Widgets.Move(config, 1);
        var remove = new MenuItem { Header = "删除" };
        remove.Click += (_, _) => AppServices.Widgets.Remove(config);

        flyout.Items.Add(up);
        flyout.Items.Add(down);
        flyout.Items.Add(new Separator());
        flyout.Items.Add(remove);
        return flyout;
    }

    
    private static Control BuildAddComponentButton(bool compact)
    {
        var flyout = new MenuFlyout();
        foreach (var info in WidgetRegistry.Types)
        {
            var item = new MenuItem { Header = $"{info.DisplayName} —— {info.Description}" };
            var type = info.Type;
            item.Click += (_, _) => AppServices.Widgets.Add(type);
            flyout.Items.Add(item);
        }

        return new Button
        {
            Content = new TextBlock
            {
                Text = compact ? "＋" : "＋ 添加组件",
                FontSize = compact ? 16 : 14,
                Foreground = CiPalette.SakuraAccentBrush(),
            },
            Background = CiPalette.SakuraAccentTint(0.14),
            BorderBrush = CiPalette.SakuraAccentTint(0.3),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = compact ? new Thickness(10, 12) : new Thickness(10, 6),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = compact ? new Thickness(8, 0, 0, 0) : new Thickness(0),
            Flyout = flyout,
        };
    }

    
    private void BuildEditBar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        row.Children.Add(BuildAddComponentButton(compact: false));

        var settings = new Button
        {
            Content = new TextBlock { Text = "组件设置…", FontSize = 14, Foreground = CiPalette.PrimaryTextBrush() },
            Background = CiPalette.ChipSurfaceBrush(),
            BorderBrush = CiPalette.ChipBorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6),
        };
        settings.Click += (_, _) => App.OpenSettings("widgets");
        row.Children.Add(settings);

        var done = new Button
        {
            Content = new TextBlock { Text = "✓ 完成", FontSize = 14, Foreground = CiPalette.OnAccentBrush() },
            Background = CiPalette.SakuraAccentGradient(),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6),
        };
        done.Click += (_, _) => ExitEditMode();
        row.Children.Add(done);

        EditBarHost.Children.Clear();
        EditBarHost.Children.Add(new Border
        {
            Background = CiPalette.IslandSurfaceBrush(),
            BorderBrush = CiPalette.IslandBorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(6),
            BoxShadow = BoxShadows.Parse("0 8 24 2 #55000000"),
            Child = row,
        });
    }
}
