using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;
using ClassNex.ViewModels;

namespace ClassNex.Views;

/// <summary>应用设置：通用 / 界面 / 主界面组件 / 课表 / 关于。</summary>
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
        NavList.SelectedIndex = page switch
        {
            "interface" => 1,
            "widgets" => 2,
            "schedule" => 3,
            "about" => 4,
            _ => 0,
        };
    }

    private void WireEvents()
    {
        NavList.SelectionChanged += (_, _) => SwitchPage();

        // ---- 通用 ----
        SingleWeekStartPicker.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property == DatePicker.SelectedDateProperty)
                ApplyGeneral();
        };
        TrayBehaviorCombo.SelectionChanged += (_, _) => ApplyGeneral();

        // ---- 界面 ----
        ThemeSystem.PropertyChanged += (_, e) => ThemeChanged(e.Property, ThemeSystem.IsChecked);
        ThemeLight.PropertyChanged += (_, e) => ThemeChanged(e.Property, ThemeLight.IsChecked);
        ThemeDark.PropertyChanged += (_, e) => ThemeChanged(e.Property, ThemeDark.IsChecked);

        OpacitySlider.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property == Slider.ValueProperty)
                ApplyInterface();
        };
        FontScaleSlider.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property == Slider.ValueProperty)
                ApplyInterface();
        };
        OrientationCombo.SelectionChanged += (_, _) => ApplyInterface();
        TopmostCheck.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property == CheckBox.IsCheckedProperty)
                ApplyInterface();
        };

        // ---- 主界面组件 ----
        ResetWidgetButton.Click += (_, _) => ResetWidgets();
        WidgetList.SelectionChanged += (_, _) => SelectWidget();
        RemoveWidgetButton.Click += (_, _) => RemoveWidget();
        MoveWidgetUpButton.Click += (_, _) => MoveWidget(-1);
        MoveWidgetDownButton.Click += (_, _) => MoveWidget(1);

        WidgetEnabledCheck.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property == CheckBox.IsCheckedProperty)
                ApplyWidgetEdit();
        };
        WidgetFontSlider.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property == Slider.ValueProperty)
                ApplyWidgetEdit();
        };
        WidgetSecondsCheck.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property == CheckBox.IsCheckedProperty)
                ApplyWidgetEdit();
        };
        WidgetTextBox.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property == TextBox.TextProperty)
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
        TopmostCheck.IsChecked = s.Topmost;

        RefreshWidgetLibrary();
        RefreshWidgetList();
        if (_widgets.Count > 0)
            WidgetList.SelectedIndex = 0;
        RefreshTimetableText();

        _loading = false;
        SwitchPage();
    }

    private void SwitchPage()
    {
        var i = NavList.SelectedIndex;
        PageGeneral.IsVisible = i == 0;
        PageInterface.IsVisible = i == 1;
        PageWidgets.IsVisible = i == 2;
        PageSchedule.IsVisible = i == 3;
        PageAbout.IsVisible = i == 4;
    }

    private void RefreshTimetableText() => TimetableFileText.Text = AppServices.TimetablePath;

    // ==================== 通用 / 界面 ====================

    private void ThemeChanged(AvaloniaProperty property, bool? isChecked)
    {
        if (_loading || !isChecked.HasValue || property != CheckBox.IsCheckedProperty)
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
        s.Topmost = TopmostCheck.IsChecked == true;

        OpacityValueText.Text = $"{s.BackgroundOpacity:P0}";
        FontScaleValueText.Text = $"{s.FontScale:0.00}x";

        AppServices.SaveSettings();
    }

    // ==================== 主界面组件 ====================

    private void UpdateWidgetTypeHint()
    {
        // 组件库改为卡片网格后不再需要类型提示
    }

    /// <summary>组件库（CI 图2）：每个组件类型一张卡片，点击即添加。</summary>
    private void RefreshWidgetLibrary()
    {
        WidgetLibraryPanel.Children.Clear();

        foreach (var type in AppServices.Widgets.AvailableTypes)
        {
            var card = new Border
            {
                Width = 200,
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
                            FontWeight = FontWeight.SemiBold,
                        },
                        new TextBlock
                        {
                            Text = type.Description,
                            FontSize = 11,
                            TextWrapping = TextWrapping.Wrap,
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
