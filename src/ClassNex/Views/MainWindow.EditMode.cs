using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;

namespace ClassNex.Views;

public partial class MainWindow
{
    private bool _editMode;

    public bool IsEditMode => _editMode;

    private volatile bool _canPresentReminder;
    internal bool CanPresentReminder => _canPresentReminder;
    internal double ReminderScale => Math.Max(0.1, AppServices.Settings.EffectiveScale);
    private void UpdateReminderAvailability() => _canPresentReminder = !_closed && IsVisible && !_editMode && !_nexPanelActive;

    public void EnterEditMode()
    {
        if (_editMode)
            return;

        if (_nexPanelActive) HideNexPanel();
        _editMode = true;
        UpdateReminderAvailability();
        if (_showingNotification) HideNotificationMask();
        RebuildWidgets();
        RefreshWidgets();
        ApplySettings();
    }

    public void ExitEditMode()
    {
        if (!_editMode)
            return;

        FinishIslandDrag(false);
        _editMode = false;
        EditBarHost.Children.Clear();
        UpdateReminderAvailability();
        RebuildWidgets();
        RefreshWidgets();
        ApplySettings();
    }

    private void OnMenuEnterEditMode(object? sender, RoutedEventArgs e) => EnterEditMode();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_editMode && e.Key == Key.Escape && !e.Handled)
        {
            ExitEditMode(); e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private Control BuildComponentToolbar(WidgetConfig config)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };

        var handle = new TextBlock
        {
            Text = "⠿  " + WidgetRegistry.DisplayNameOf(config.Type),
            FontSize = 13,
            Foreground = CiPalette.PrimaryTextBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = new Cursor(StandardCursorType.SizeAll),
        };
        ToolTip.SetTip(handle, "拖到目标组件的位置，或在更多菜单中移动");
        Avalonia.Automation.AutomationProperties.SetName(handle, "拖动" + WidgetRegistry.DisplayNameOf(config.Type));
        handle.Tag = config;
        Point start = default;
        var exceededThreshold = false;
        handle.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            start = e.GetPosition(this); exceededThreshold = false;
            e.Pointer.Capture(handle);
            e.Handled = true;
        };
        handle.PointerMoved += (_, e) =>
        {
            if (e.Pointer.Captured != handle) return;
            var distance = e.GetPosition(this) - start;
            var threshold = PlatformSettings?.GetTapSize(e.Pointer.Type) ?? new Size(4, 4);
            if (Math.Abs(distance.X) >= threshold.Width || Math.Abs(distance.Y) >= threshold.Height) exceededThreshold = true;
            e.Handled = true;
        };
        handle.PointerReleased += (_, e) =>
        {
            if (e.Pointer.Captured != handle) return;
            e.Pointer.Capture(null);
            e.Handled = true;
            if (!exceededThreshold) return;
            for (var i = 0; i < _widgets.Count; i++)
            {
                if (_widgets[i].DisplayView.Parent is not Control item || !new Rect(item.Bounds.Size).Contains(e.GetPosition(item))) continue;
                var list = WidgetConfig.InDisplayOrder(AppServices.Settings.Widgets).ToList();
                var delta = list.IndexOf(_widgets[i].Config) - list.IndexOf(config);
                if (delta != 0) AppServices.Widgets.Move(config, delta);
                break;
            }
        };
        row.Children.Add(handle);

        var settings = ToolButton("\uE154", "该组件的设置", null);
        settings.Tag = config;
        settings.Click += (_, _) =>
        {
            var flyout = BuildQuickProperties(config);
            FlyoutBase.SetAttachedFlyout(settings, flyout);
            flyout.ShowAt(settings);
        };
        row.Children.Add(settings);

        row.Children.Add(ToolButton("\uE18E", "删除该组件", () => AppServices.Widgets.Remove(config)));

        var moreButton = ToolButton("\uE0B6", "更多组件操作", null);
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

        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        ToolTip.SetTip(button, tip);
        if (onClick is not null)
            button.Click += (_, _) => onClick();

        return button;
    }

    private static MenuFlyout BuildMoreFlyout(WidgetConfig config)
    {
        var flyout = new MenuFlyout();

        var sorted = WidgetConfig.InDisplayOrder(AppServices.Settings.Widgets).ToList();
        var index = sorted.IndexOf(config);
        var up = new MenuItem { Header = "向前移动", IsEnabled = index > 0 };
        up.Click += (_, _) => AppServices.Widgets.Move(config, -1);
        var down = new MenuItem { Header = "向后移动", IsEnabled = index + 1 < sorted.Count };
        down.Click += (_, _) => AppServices.Widgets.Move(config, 1);
        var rows = sorted.Select(w => w.Row).Distinct().ToArray();
        var currentRow = Array.IndexOf(rows, config.Row);
        MenuItem RowAction(string name, int? row, bool enabled)
        {
            var item = new MenuItem { Header = name, IsEnabled = enabled };
            item.Click += (_, _) => { if (WidgetConfig.MoveToRowEnd(AppServices.Settings.Widgets, config, row) >= 0) AppServices.Widgets.Save(); };
            return item;
        }
        var copy = new MenuItem { Header = "创建副本" };
        copy.Click += (_, _) => AppServices.Widgets.Duplicate(config);
        var remove = new MenuItem { Header = "删除" };
        remove.Click += (_, _) => AppServices.Widgets.Remove(config);

        flyout.Items.Add(up);
        flyout.Items.Add(down);
        flyout.Items.Add(RowAction("移到上一行", currentRow > 0 ? rows[currentRow - 1] : null, currentRow > 0));
        flyout.Items.Add(RowAction("移到下一行", currentRow >= 0 && currentRow + 1 < rows.Length ? rows[currentRow + 1] : null, currentRow >= 0 && currentRow + 1 < rows.Length));
        flyout.Items.Add(RowAction("移到新的一行", null, currentRow < rows.Length - 1 || sorted.Count(w => w.Row == config.Row) > 1));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(copy);
        flyout.Items.Add(new Separator());
        flyout.Items.Add(remove);
        return flyout;
    }

    private static Flyout BuildQuickProperties(WidgetConfig config)
    {
        var panel = new StackPanel { Spacing = 8, MinWidth = 224, MaxWidth = 310 };
        var flyout = new Flyout { Content = panel };
        panel.Children.Add(new TextBlock { Text = WidgetRegistry.DisplayNameOf(config.Type), FontSize = 16 });
        void Change(Action update)
        {
            if (!AppServices.Settings.Widgets.Contains(config)) { flyout.Hide(); return; }
            update(); AppServices.SaveSettings();
        }
        void Toggle(string name, bool enabled, Action<bool> assign)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            var control = new ToggleSwitch { IsChecked = enabled, OnContent = "开", OffContent = "关" };
            Grid.SetColumn(control, 1); row.Children.Add(control);
            Avalonia.Automation.AutomationProperties.SetName(control, name);
            control.IsCheckedChanged += (_, _) => Change(() => assign(control.IsChecked == true));
            panel.Children.Add(row);
        }
        Toggle("启用该组件", config.IsEnabled, v => config.IsEnabled = v);
        switch (config.Type)
        {
            case "clock": Toggle("显示秒数", config.ShowSeconds, v => config.ShowSeconds = v); break;
            case "schedule": Toggle("淡化已上的课", config.FadeCompletedClasses, v => config.FadeCompletedClasses = v); break;
            case "nextclass": Toggle("显示下一节课", config.ShowNextClass, v => config.ShowNextClass = v); break;
            case "countdown": Toggle("使用强调色", config.UseCountdownAccent, v => config.UseCountdownAccent = v); break;
            case "text":
                var text = new TextBox { Text = config.Text, Watermark = "文本内容", MaxWidth = 280 };
                Avalonia.Automation.AutomationProperties.SetName(text, "文本内容");
                void SaveText() { if (config.Text != text.Text) Change(() => config.Text = text.Text ?? ""); }
                text.LostFocus += (_, _) => SaveText();
                text.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SaveText(); e.Handled = true; } };
                panel.Children.Add(text); break;
        }
        QuickSlider(panel, flyout, "组件字号", .6, 2, .05, () => config.FontScale, v => Change(() => config.FontScale = v));
        QuickSlider(panel, flyout, "组件不透明度", 0, 1, .01, () => config.Presentation?.Opacity ?? 1, v => Change(() =>
        {
            var style = (config.Presentation ?? new()) with { Opacity = v };
            config.Presentation = style == new WidgetPresentation() ? null : style;
        }));
        var all = new Button { Content = "全部组件设置", HorizontalAlignment = HorizontalAlignment.Stretch };
        all.Click += (_, _) => { flyout.Hide(); App.EditWidget(config); };
        panel.Children.Add(all); return flyout;
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

        var move = new Border
        {
            Name = "MoveIslandHandle", Width = 32, MinHeight = 32, CornerRadius = new CornerRadius(6),
            Background = CiPalette.ChipSurfaceBrush(), Cursor = new Cursor(StandardCursorType.SizeAll),
            Child = new TextBlock
            {
                Text = "\uE121", FontFamily = ApplicationLucide, FontSize = 16, Foreground = CiPalette.PrimaryTextBrush(),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        ToolTip.SetTip(move, "拖动主界面");
        Avalonia.Automation.AutomationProperties.SetName(move, "拖动主界面");
        move.PointerPressed += IslandPressed;
        move.PointerMoved += IslandMoved;
        move.PointerReleased += IslandReleased;
        move.PointerCaptureLost += (_, e) => { if (e.Pointer == _placementPointer) FinishIslandDrag(false); };
        row.Children.Add(move);

        row.Children.Add(BuildAddComponentButton(compact: false));

        var appearance = ToolButton("\uE154", "桌面外观", null);
        appearance.MinWidth = 32;
        appearance.MinHeight = 32;
        appearance.Click += (_, _) =>
        {
            var settingsPanel = BuildQuickAppearance();
            FlyoutBase.SetAttachedFlyout(appearance, settingsPanel);
            settingsPanel.ShowAt(appearance);
        };
        row.Children.Add(appearance);

        var settings = new Button
        {
            Content = new TextBlock { Text = "组件设置…", FontSize = 14, Foreground = CiPalette.PrimaryTextBrush() },
            Background = CiPalette.ChipSurfaceBrush(),
            BorderBrush = CiPalette.ChipBorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6),
        };
        settings.Click += (_, _) => App.OpenSettings("widget-layout");
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
