using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Models;
using ClassNex.Services;

namespace ClassNex.Views;

/// <summary>
/// 主界面「编辑模式」—— 照搬 CI 的「编辑主界面」逻辑
/// （ClassIsland/Views/EditMode/EditableComponentsListBox + EditModeView）：
///   - **每个组件都是独立的一项**：点它上方自己的工具条就能单独操作它（设置 / 删除 / 上移下移）
///   - 组件末尾是「＋ 添加组件」（CI 里同位置是「＋ 新主界面行」，本项目是单行布局）
///   - 底部工具条：添加组件 / 组件设置… / 完成（对应 CI 底部的 添加组件 / 外观 / 完成）
///   - 编辑模式下**不淡化、鼠标不穿透**（CI 的 `[IsLineFaded=True][MainWindowInEditMode=False]` 同样如此，
///     否则鼠标点不到工具条上的按钮）
/// </summary>
public partial class MainWindow
{
    private bool _editMode;

    /// <summary>是否处于编辑模式。</summary>
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

    /// <summary>
    /// 单个组件的工具条（CI 编辑模式下每个组件上方那一条：名称 + ⚙ 设置 + 🗑 删除 + ⋯ 更多）。
    /// </summary>
    private Control BuildComponentToolbar(WidgetConfig config)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };

        row.Children.Add(new TextBlock
        {
            Text = WidgetRegistry.DisplayNameOf(config.Type),
            FontSize = 14,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });

        // ⚙ 该组件的设置（CI 打开的是该组件自己的设置控件；本项目落在「主界面组件」页）
        row.Children.Add(ToolButton("\uE713", "该组件的设置", () => App.OpenSettings("widgets")));

        // 🗑 删除该组件
        row.Children.Add(ToolButton("\uE74D", "删除该组件", () => AppServices.Widgets.Remove(config)));

        // ⋯ 更多（上移 / 下移 / 删除）
        var moreButton = ToolButton("\uE712", "更多（上移 / 下移 / 删除）", null);
        moreButton.Flyout = BuildMoreFlyout(config);
        row.Children.Add(moreButton);

        return new Border
        {
            Background = new SolidColorBrush(Colors.Black, 0.55),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 2),
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
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                Foreground = Brushes.White,
            },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
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

    /// <summary>「＋ 添加组件」按钮（列表 = 组件注册表里的全部类型）。</summary>
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
                Foreground = Brushes.White,
            },
            Background = new SolidColorBrush(Colors.White, 0.10),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Padding = compact ? new Thickness(10, 14) : new Thickness(10, 6),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = compact ? new Thickness(8, 0, 0, 0) : new Thickness(0),
            Flyout = flyout,
        };
    }

    /// <summary>底部工具条（CI 编辑主界面底部栏的对等物）。</summary>
    private void BuildEditBar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        row.Children.Add(BuildAddComponentButton(compact: false));

        var settings = new Button
        {
            Content = new TextBlock { Text = "组件设置…", FontSize = 14, Foreground = Brushes.White },
            Background = new SolidColorBrush(Colors.White, 0.10),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6),
        };
        settings.Click += (_, _) => App.OpenSettings("widgets");
        row.Children.Add(settings);

        var done = new Button
        {
            Content = new TextBlock { Text = "✓ 完成", FontSize = 14, Foreground = Brushes.Black },
            Background = Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 6),
        };
        done.Click += (_, _) => ExitEditMode();
        row.Children.Add(done);

        EditBarHost.Children.Clear();
        EditBarHost.Children.Add(new Border
        {
            Background = new SolidColorBrush(Colors.Black, 0.6),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6),
            Child = row,
        });
    }
}
