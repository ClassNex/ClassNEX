using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassNex.Services;

namespace ClassNex.Views;

/// <summary>应用设置窗口：左侧导航 + 通用 / 界面 / 课表 / 关于。</summary>
public partial class SettingsWindow : Window
{
    private bool _loading;

    public SettingsWindow()
    {
        InitializeComponent();
        WireEvents();
        LoadFromSettings();
    }

    /// <summary>导航到指定页面：general / interface / schedule / about。</summary>
    public void NavigateTo(string page)
    {
        NavList.SelectedIndex = page switch
        {
            "interface" => 1,
            "schedule" => 2,
            "about" => 3,
            _ => 0,
        };
    }

    private void WireEvents()
    {
        NavList.SelectionChanged += (_, _) => SwitchPage();

        SingleWeekStartPicker.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property == DatePicker.SelectedDateProperty)
                ApplyGeneral();
        };
        TrayBehaviorCombo.SelectionChanged += (_, _) => ApplyGeneral();
        HideOnClassCheck.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property == CheckBox.IsCheckedProperty)
                ApplyGeneral();
        };

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
        ShowDateCheck.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property == CheckBox.IsCheckedProperty)
                ApplyInterface();
        };
        TopmostCheck.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property == CheckBox.IsCheckedProperty)
                ApplyInterface();
        };

        OpenTimetableButton.Click += OnOpenTimetable;
        ReloadTimetableButton.Click += (_, _) =>
        {
            AppServices.ReloadDocument();
            RefreshTimetableText();
        };
    }

    private void ThemeChanged(Avalonia.AvaloniaProperty property, bool? isChecked)
    {
        if (_loading || !isChecked.HasValue || property != CheckBox.IsCheckedProperty)
            return;

        ApplyTheme();
    }

    private void LoadFromSettings()
    {
        _loading = true;
        var s = AppServices.Settings;

        SingleWeekStartPicker.SelectedDate = new DateTimeOffset(s.SingleWeekStartTime);
        TrayBehaviorCombo.SelectedIndex = Math.Clamp(s.TrayClickBehavior, 0, 2);
        HideOnClassCheck.IsChecked = s.IsHideOnClass;

        ThemeSystem.IsChecked = s.ThemeMode == "system";
        ThemeLight.IsChecked = s.ThemeMode == "light";
        ThemeDark.IsChecked = s.ThemeMode == "dark";

        OpacitySlider.Value = Math.Clamp(s.BackgroundOpacity, 0.1, 1);
        FontScaleSlider.Value = Math.Clamp(s.FontScale, 0.8, 1.6);
        ShowDateCheck.IsChecked = s.ShowDate;
        TopmostCheck.IsChecked = s.Topmost;

        OpacityValueText.Text = $"{s.BackgroundOpacity:P0}";
        FontScaleValueText.Text = $"{s.FontScale:0.00}x";

        RefreshTimetableText();
        _loading = false;

        SwitchPage();
    }

    private void SwitchPage()
    {
        var i = NavList.SelectedIndex;
        PageGeneral.IsVisible = i == 0;
        PageInterface.IsVisible = i == 1;
        PageSchedule.IsVisible = i == 2;
        PageAbout.IsVisible = i == 3;
    }

    private void RefreshTimetableText() => TimetableFileText.Text = AppServices.TimetablePath;

    private void ApplyGeneral()
    {
        if (_loading)
            return;

        var s = AppServices.Settings;
        if (SingleWeekStartPicker.SelectedDate is { } d)
            s.SingleWeekStartTime = d.Date;
        s.TrayClickBehavior = Math.Max(0, TrayBehaviorCombo.SelectedIndex);
        s.IsHideOnClass = HideOnClassCheck.IsChecked == true;
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
        s.ShowDate = ShowDateCheck.IsChecked == true;
        s.Topmost = TopmostCheck.IsChecked == true;

        OpacityValueText.Text = $"{s.BackgroundOpacity:P0}";
        FontScaleValueText.Text = $"{s.FontScale:0.00}x";

        AppServices.SaveSettings();
    }

    private async void OnOpenTimetable(object? sender, RoutedEventArgs e)
    {
        var path = await FilePickerHelper.PickTimetableAsync(this);
        if (path is null)
            return;

        AppServices.LoadTimetable(path);
        RefreshTimetableText();
    }
}
