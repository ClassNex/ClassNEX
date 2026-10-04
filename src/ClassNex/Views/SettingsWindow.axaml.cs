using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;
using ClassNex.ViewModels;
using FluentAvalonia.UI.Controls;

namespace ClassNex.Views;

/// <summary>
/// 应用设置。全程使用 FluentAvalonia（FluentUI）控件：
/// NavigationView（左侧导航）/ SettingsExpander（设置分组）/ ToggleSwitch（开关）/ FontIcon（图标）。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<WidgetItem> _widgets = new();

    private bool _loading;
    private WidgetItem? _currentWidget;

    public SettingsWindow()
    {
        InitializeComponent();
        WidgetList.ItemsSource = _widgets;

        WireEvents();
        LoadFromSettings();
    }

    /// <summary>导航到指定页面：general / interface / widgets / schedule / about。</summary>
    public void NavigateTo(string page)
    {
        var index = page switch
        {
            "interface" => 1,
            "widgets" => 2,
            "schedule" => 3,
            "about" => 4,
            _ => 0,
        };

        if (index < NavView.MenuItems.Count)
            NavView.SelectedItem = NavView.MenuItems[index];
    }

    private void WireEvents()
    {
        NavView.SelectionChanged += (_, _) => SwitchPage();

        // ---- 通用 ----
        SingleWeekStartPicker.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "SelectedDate")
                ApplyGeneral();
        };
        TrayBehaviorCombo.SelectionChanged += (_, _) => ApplyGeneral();

        // ---- 界面（ToggleSwitch 用属性名判断，避免依赖具体控件的静态属性）----
        ThemeSystem.PropertyChanged += (_, e) => ThemeChanged(e.Property.Name, ThemeSystem.IsChecked);
        ThemeLight.PropertyChanged += (_, e) => ThemeChanged(e.Property.Name, ThemeLight.IsChecked);
        ThemeDark.PropertyChanged += (_, e) => ThemeChanged(e.Property.Name, ThemeDark.IsChecked);

        OpacitySlider.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "Value")
                ApplyInterface();
        };
        FontScaleSlider.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "Value")
                ApplyInterface();
        };
        OrientationCombo.SelectionChanged += (_, _) => ApplyInterface();

        // ---- 主界面组件 ----
        ResetWidgetButton.Click += (_, _) => ResetWidgets();
        WidgetList.SelectionChanged += (_, _) => SelectWidget();
        RemoveWidgetButton.Click += (_, _) => RemoveWidget();
        MoveWidgetUpButton.Click += (_, _) => MoveWidget(-1);
        MoveWidgetDownButton.Click += (_, _) => MoveWidget(1);

        WidgetEnabledCheck.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "IsChecked")
                ApplyWidgetEdit();
        };
        WidgetFontSlider.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "Value")
                ApplyWidgetEdit();
        };
        WidgetSecondsCheck.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "IsChecked")
                ApplyWidgetEdit();
        };
        WidgetTextBox.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "Text")
                ApplyWidgetEdit();
        };

        // ---- 课表 ----
        OpenTimetableButton.Click += OnOpenTimetable;
        ReloadTimetableButton.Click += (_, _) =>
        {
            AppServices.ReloadTimetable();
            RefreshTimetableText();
        };
    }

    // ==================== 加载 ====================

    private void LoadFromSettings()
    {
        _loading = true;
        var s = AppServices.Settings;

        SingleWeekStartPicker.SelectedDate = new DateTimeOffset(s.SingleWeekStartTime);
        TrayBehaviorCombo.SelectedIndex = Math.Clamp(s.TrayClickBehavior, 0, 2);

        ThemeSystem.IsChecked = s.ThemeMode == "system";
        ThemeLight.IsChecked = s.ThemeMode == "light";
        ThemeDark.IsChecked = s.ThemeMode == "dark";

        OpacitySlider.Value = Math.Clamp(s.BackgroundOpacity, 0.1, 1);
        FontScaleSlider.Value = Math.Clamp(s.FontScale, 0.8, 1.6);
        OpacityValueText.Text = $"{s.BackgroundOpacity:P0}";
        FontScaleValueText.Text = $"{s.FontScale:0.00}x";
        OrientationCombo.SelectedIndex = s.Orientation == LayoutOrientation.Vertical ? 1 : 0;

        RefreshWidgetLibrary();
        RefreshWidgetList();

        if (_widgets.Count > 0)
            WidgetList.SelectedIndex = 0;

        RefreshTimetableText();

        _loading = false;

        NavView.SelectedItem = NavView.MenuItems[0];
        SwitchPage();
    }

    private void SwitchPage()
    {
        var index = NavView.SelectedItem is { } item && NavView.MenuItems.Contains(item)
            ? NavView.MenuItems.IndexOf(item)
            : 0;

        PageGeneral.IsVisible = index == 0;
        PageInterface.IsVisible = index == 1;
        PageWidgets.IsVisible = index == 2;
        PageSchedule.IsVisible = index == 3;
        PageAbout.IsVisible = index == 4;
    }

    private void RefreshTimetableText() => TimetableFileText.Text = AppServices.TimetablePath;

    // ==================== 通用 / 界面 ====================

    private void ThemeChanged(string propertyName, bool? isChecked)
    {
        if (_loading || !isChecked.HasValue || propertyName != "IsChecked")
            return;

        ApplyTheme();
    }

    private void ApplyGeneral()
    {
        if (_loading)
            return;

        var s = AppServices.Settings;
        if (SingleWeekStartPicker.SelectedDate is { } d)
            s.SingleWeekStartTime = d.Date;
        s.TrayClickBehavior = Math.Max(0, TrayBehaviorCombo.SelectedIndex);
        AppServices.SaveSettings();
    }

    private void ApplyTheme()
    {
        if (_loading)
            return;

        var s = AppServices.Settings;
        s.ThemeMode = ThemeLight.IsChecked == true ? "light"
            : ThemeDark.IsChecked == true ? "dark"
            : "system";

        App.ApplyTheme(s.ThemeMode);
        AppServices.SaveSettings();
    }

    private void ApplyInterface()
    {
        if (_loading)
            return;

        var s = AppServices.Settings;
        s.BackgroundOpacity = OpacitySlider.Value;
        s.FontScale = FontScaleSlider.Value;
        s.Orientation = OrientationCombo.SelectedIndex == 1
            ? LayoutOrientation.Vertical
            : LayoutOrientation.Horizontal;

        OpacityValueText.Text = $"{s.BackgroundOpacity:P0}";
        FontScaleValueText.Text = $"{s.FontScale:0.00}x";

        AppServices.SaveSettings();
    }

    // ==================== 主界面组件 ====================

    /// <summary>组件库：每个组件类型一张卡片，点击即添加。</summary>
    private void RefreshWidgetLibrary()
    {
        WidgetLibraryPanel.Children.Clear();

        foreach (var type in AppServices.Widgets.AvailableTypes)
        {
            var card = new Border
            {
                Width = 190,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(14, 10),
                CornerRadius = new CornerRadius(6),
                Background = CiPalette.SurfaceBrush("CardBackgroundFillColorSecondaryBrush", 0.25),
                BorderBrush = CiPalette.SurfaceBrush("CardStrokeColorDefaultBrush", 0.4),
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = type.DisplayName,
                            FontSize = 14,
                            FontWeight = Avalonia.Media.FontWeight.SemiBold,
                        },
                        new TextBlock
                        {
                            Text = type.Description,
                            FontSize = 11,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            Opacity = 0.7,
                        },
                    },
                },
            };

            card.PointerPressed += (_, _) => AddWidget(type.Type);
            WidgetLibraryPanel.Children.Add(card);
        }
    }

    private void RefreshWidgetList()
    {
        _widgets.Clear();
        foreach (var config in AppServices.Widgets.Widgets.OrderBy(w => w.Order))
            _widgets.Add(new WidgetItem(config));
    }

    private void SelectWidget()
    {
        _currentWidget = WidgetList.SelectedItem as WidgetItem;
        _loading = true;

        WidgetEditor.IsEnabled = _currentWidget is not null;

        if (_currentWidget is not null)
        {
            var config = _currentWidget.Config;
            WidgetTypeText.Text = $"{WidgetRegistry.DisplayNameOf(config.Type)} — {WidgetRegistry.DescriptionOf(config.Type)}";
            WidgetEnabledCheck.IsChecked = config.IsEnabled;
            WidgetFontSlider.Value = Math.Clamp(config.FontScale, 0.6, 2.0);
            WidgetFontValueText.Text = $"{config.FontScale:0.00}x";
            WidgetSecondsCheck.IsChecked = config.ShowSeconds;
            WidgetTextBox.Text = config.Text ?? "";
            WidgetSecondsCheck.IsEnabled = config.Type == "clock";
            WidgetTextBox.IsEnabled = config.Type == "text";
        }

        _loading = false;
    }

    private void ApplyWidgetEdit()
    {
        if (_loading || _currentWidget is null)
            return;

        var config = _currentWidget.Config;
        config.IsEnabled = WidgetEnabledCheck.IsChecked == true;
        config.FontScale = WidgetFontSlider.Value;
        config.ShowSeconds = WidgetSecondsCheck.IsChecked == true;
        config.Text = WidgetTextBox.Text;

        WidgetFontValueText.Text = $"{config.FontScale:0.00}x";
        _currentWidget.Refresh();

        AppServices.Widgets.Save();
    }

    private void AddWidget(string type)
    {
        var config = AppServices.Widgets.Add(type);
        RefreshWidgetList();
        WidgetList.SelectedItem = _widgets.FirstOrDefault(w => ReferenceEquals(w.Config, config));
    }

    private void RemoveWidget()
    {
        if (_currentWidget is null)
            return;

        AppServices.Widgets.Remove(_currentWidget.Config);
        _currentWidget = null;
        RefreshWidgetList();
        SelectWidget();
    }

    private void MoveWidget(int delta)
    {
        if (_currentWidget is null)
            return;

        AppServices.Widgets.Move(_currentWidget.Config, delta);
        var config = _currentWidget.Config;
        RefreshWidgetList();
        WidgetList.SelectedItem = _widgets.FirstOrDefault(w => ReferenceEquals(w.Config, config));
    }

    private void ResetWidgets()
    {
        AppServices.Widgets.ResetToDefault();
        _currentWidget = null;
        RefreshWidgetList();
        SelectWidget();
    }

    // ==================== 课表 ====================

    private async void OnOpenTimetable(object? sender, RoutedEventArgs e)
    {
        var path = await FilePickerHelper.PickTimetableAsync(this);
        if (path is null)
            return;

        AppServices.LoadTimetable(path);
        RefreshTimetableText();
    }
}
