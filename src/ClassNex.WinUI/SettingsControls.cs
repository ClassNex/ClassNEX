using ClassNex.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Windows.System;

namespace ClassNex.WinUI;

public sealed partial class NativeSettingsWindow
{
    private static TextBlock Text(string value, string style = "BodyTextBlockStyle")
    {
        var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        if (Application.Current.Resources.TryGetValue(style, out var resource) && resource is Style systemStyle) text.Style = systemStyle;
        else text.FontSize = style switch { "TitleTextBlockStyle" => 28, "SubtitleTextBlockStyle" => 20, "CaptionTextBlockStyle" => 12, _ => 14 };
        return text;
    }

    private void Section(string title) => _body.Children.Add(new TextBlock
    {
        Text = title, FontSize = 16, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 4)
    });

    private T Target<T>(string id, string title, T element) where T : FrameworkElement
    {
        Identify(element, id, title);
        _targets[id] = element;
        if (_currentExpander != null) _detailOwners[id] = _currentExpander;
        return element;
    }

    private static Style? _settingSurface, _settingCaption;

    private static Style SettingSurface => _settingSurface ??= (Style)XamlReader.Load("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Grid">
            <Setter Property="Background" Value="{ThemeResource CardBackgroundFillColorDefaultBrush}"/>
            <Setter Property="BorderBrush" Value="{ThemeResource CardStrokeColorDefaultBrush}"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="CornerRadius" Value="{ThemeResource ControlCornerRadius}"/>
        </Style>
        """);

    private static TextBlock SettingCaption(string value)
    {
        _settingCaption ??= (Style)XamlReader.Load("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="TextBlock" BasedOn="{StaticResource CaptionTextBlockStyle}">
                <Setter Property="Foreground" Value="{ThemeResource TextFillColorSecondaryBrush}"/>
            </Style>
            """);
        return new TextBlock { Text = value, Style = _settingCaption, TextWrapping = TextWrapping.Wrap };
    }

    private static Grid MakeSettingRow(string title, string detail, FrameworkElement editor, bool surface = true)
    {
        var grid = new Grid { ColumnSpacing = 16, HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = surface ? SettingsCardHeight : 40, Padding = surface ? new Thickness(16, 12, 16, 12) : new Thickness(0, 3, 0, 3) };
        if (surface) grid.Style = SettingSurface;
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        label.Children.Add(Text(title));
        if (detail.Length != 0) label.Children.Add(SettingCaption(detail));
        grid.Children.Add(label);
        var id = AutomationProperties.GetAutomationId(editor);
        if (!string.IsNullOrEmpty(id)) AutomationProperties.SetAutomationId(grid, "Row_" + id);
        FrameworkElement action = editor is ToggleSwitch toggle ? TrailingSwitch(toggle, id) : editor;
        action.VerticalAlignment = VerticalAlignment.Center;
        action.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(action, 1);
        grid.Children.Add(action);
        var field = editor is TextBox or PasswordBox;
        var multiline = editor is TextBox { AcceptsReturn: true };
        bool? stacked = null;
        void Arrange(double width)
        {
            var threshold = field ? 480 : editor is ToggleSwitch ? 240 : editor is TextBlock ? 410 : 360;
            var small = multiline || width < threshold;
            if (small == stacked) return;
            stacked = small;
            grid.RowSpacing = small ? 8 : 0;
            Grid.SetColumnSpan(label, small ? 2 : 1);
            Grid.SetRow(action, small ? 1 : 0);
            Grid.SetColumn(action, small ? 0 : 1);
            Grid.SetColumnSpan(action, small ? 2 : 1);
            if (field)
            {
                action.Width = small ? double.NaN : 248;
                action.HorizontalAlignment = small ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
            }
        }
        Arrange(double.PositiveInfinity);
        grid.SizeChanged += (_, args) => Arrange(args.NewSize.Width);
        return grid;
    }

    private static StackPanel TrailingSwitch(ToggleSwitch toggle, string id)
    {
        toggle.MinWidth = 0;
        toggle.Width = 40;
        toggle.OnContent = toggle.OffContent = string.Empty;
        var state = Text(toggle.IsOn ? "开" : "关");
        AutomationProperties.SetAccessibilityView(state, AccessibilityView.Raw);
        if (!string.IsNullOrEmpty(id)) AutomationProperties.SetAutomationId(state, id + "_State");
        toggle.Toggled += (_, _) => state.Text = toggle.IsOn ? "开" : "关";
        state.Tapped += (_, e) =>
        {
            if (!toggle.IsEnabled) return;
            toggle.Focus(FocusState.Pointer);
            toggle.IsOn = !toggle.IsOn;
            e.Handled = true;
        };
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        group.Children.Add(state);
        group.Children.Add(toggle);
        return group;
    }

    private void Row(string title, string detail, FrameworkElement editor) =>
        _body.Children.Add(MakeSettingRow(title, detail, editor, _currentExpander is null));

    private ToggleSwitch Switch(string id, string title, Func<AppSettings, bool> get, Action<AppSettings, bool> set, string detail = "")
    {
        var toggle = Target(id, title, new ToggleSwitch());
        _bindings.Add(s => { var value = get(s); if (toggle.IsOn != value) toggle.IsOn = value; });
        toggle.Toggled += (_, _) => { if (!_refreshing && get(_bridge.ReadSettings()) != toggle.IsOn) Edit(s => set(s, toggle.IsOn)); };
        Row(title, detail, toggle);
        return toggle;
    }

    private void Choice(string id, string title, string[] options, Func<AppSettings, int> get, Action<AppSettings, int> set, string detail = "")
    {
        var combo = Target(id, title, new ComboBox { ItemsSource = options, MinWidth = 154 });
        _bindings.Add(s => { var i = Math.Clamp(get(s), 0, options.Length - 1); if (combo.SelectedIndex != i) combo.SelectedIndex = i; });
        combo.SelectionChanged += (_, _) => { if (!_refreshing && combo.SelectedIndex is var i && i >= 0 && get(_bridge.ReadSettings()) != i) Edit(s => set(s, i)); };
        Row(title, detail, combo);
    }

    private void ThemeChoice(string id, string title)
    {
        string[] modes = ["system", "light", "dark"];
        Choice(id, title, ["跟随系统", "浅色", "深色"], s => Array.IndexOf(modes, s.ThemeMode), (s, i) => s.ThemeMode = modes[i]);
    }

    private void Scale(string id, string title, double min, double max, double step, Func<AppSettings, double> get,
        Action<AppSettings, double> set, Func<double, string> format, string detail = "")
    {
        var slider = Target(id, title, new Slider { Minimum = min, Maximum = max, StepFrequency = step, Width = 152, VerticalAlignment = VerticalAlignment.Center });
        var value = Text("");
        value.MinWidth = 54;
        value.TextAlignment = TextAlignment.Right;
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        group.Children.Add(slider);
        group.Children.Add(value);
        _bindings.Add(s =>
        {
            var v = Math.Clamp(get(s), min, max);
            if (_pendingSlider?.Owner == slider) return;
            if (Math.Abs(slider.Value - v) > .00001) slider.Value = v;
            value.Text = format(v);
        });
        slider.ValueChanged += (_, args) =>
        {
            var v = Math.Round(args.NewValue, 4);
            value.Text = format(v);
            if (_refreshing) return;
            var previous = _pendingSlider;
            _pendingSlider = (slider, s => set(s, v));
            if (previous is { } pending && pending.Owner != slider) Edit(pending.Update);
            _sliderTimer ??= CreateSliderTimer();
            _sliderTimer.Stop();
            _sliderTimer.Start();
        };
        Row(title, detail, group);
    }

    private (Slider Owner, Action<AppSettings> Update)? _pendingSlider;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _sliderTimer;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateSliderTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(100);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => FlushSlider();
        return timer;
    }

    private void FlushSlider()
    {
        _sliderTimer?.Stop();
        if (_pendingSlider is not { } pending) return;
        _pendingSlider = null;
        Edit(pending.Update);
    }

    private void CommitInputs()
    {
        FlushSlider();
        foreach (var commit in _fieldCommits.ToArray()) commit();
    }

    private void Number(string id, string title, int min, int max, Func<AppSettings, int> get, Action<AppSettings, int> set, string detail = "")
    {
        var box = Target(id, title, new NumberBox { Minimum = min, Maximum = max, SmallChange = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, Width = 148 });
        _bindings.Add(s => { var v = Math.Clamp(get(s), min, max); if (box.Value != v) box.Value = v; });
        box.ValueChanged += (_, args) =>
        {
            if (_refreshing || !double.IsFinite(args.NewValue)) return;
            var v = (int)Math.Clamp(Math.Round(args.NewValue), min, max);
            if (get(_bridge.ReadSettings()) != v) Edit(s => set(s, v));
        };
        Row(title, detail, box);
    }

    private void Date(string id, string title, Func<AppSettings, DateTimeOffset?> get, Action<AppSettings, DateTimeOffset?> set)
    {
        var calendar = Target(id, title, new CalendarDatePicker { PlaceholderText = "选择日期", MinWidth = 154 });
        _bindings.Add(s => { var day = get(s); if (calendar.Date?.Date != day?.Date) calendar.Date = day; });
        calendar.DateChanged += (_, args) => { if (!_refreshing && get(_bridge.ReadSettings())?.Date != args.NewDate?.Date) Edit(s => set(s, args.NewDate)); };
        Row(title, "", calendar);
    }

    private TextBox Field(string id, string title, Func<AppSettings, string?> get, Action<AppSettings, string> set, bool multiline = false, string detail = "")
    {
        var field = Target(id, title, new TextBox { AcceptsReturn = multiline,
            MinHeight = multiline ? 72 : 32, MaxHeight = multiline ? 152 : 64, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap });
        var dirty = false;
        _bindings.Add(s => { var v = get(s) ?? ""; if (!dirty && field.Text != v) field.Text = v; });
        field.TextChanging += (_, _) => { if (!_refreshing) dirty = field.Text != (get(_bridge.ReadSettings()) ?? ""); };
        void Commit()
        {
            if (!dirty || !_targets.TryGetValue(id, out var current) || current != field) return;
            dirty = false;
            if ((get(_bridge.ReadSettings()) ?? "") != field.Text) Edit(s => set(s, field.Text));
        }
        field.LostFocus += (_, _) => Commit();
        _fieldCommits.Add(Commit);
        field.KeyDown += (_, args) => { if (!multiline && args.Key == VirtualKey.Enter) { Commit(); args.Handled = true; } };
        Row(title, detail, field);
        return field;
    }

    private void StatusRow(string id, string title, Func<AppSettings, string> text)
    {
        var label = Target(id, title, Text(""));
        label.IsTextSelectionEnabled = true;
        label.MaxWidth = 300;
        label.TextAlignment = TextAlignment.Right;
        _bindings.Add(s => { var value = text(s); if (label.Text != value) label.Text = value; });
        Row(title, "", label);
    }

    private Button ActionButton(string id, string label, Action action)
    {
        var button = Target(id, label, new Button { Content = label });
        button.Click += (_, _) => action();
        return button;
    }

    private void ActionRow(string id, string title, string label, Action action, string detail = "") => Row(title, detail, ActionButton(id, label, action));

    private void CommandRow(string id, string title, string label, string command, string detail = "") =>
        ActionRow(id, title, label, () => _bridge.Command(command), detail);

    private void AsyncRow(string id, string title, string label, string command, string detail = "", Action<string>? complete = null,
        Action? before = null, Func<string, string>? summary = null)
    {
        var button = Target(id, title, new Button { Content = label });
        var feedback = Text("", "CaptionTextBlockStyle");
        feedback.IsTextSelectionEnabled = true;
        feedback.Visibility = Visibility.Collapsed;
        var page = _page;
        button.Click += async (_, _) =>
        {
            if (!button.IsEnabled) return;
            button.IsEnabled = false;
            feedback.Text = "正在处理…";
            feedback.Visibility = Visibility.Visible;
            try
            {
                before?.Invoke();
                var result = await _bridge.ExecuteAsync(command);
                if (_closed || _page != page) return;
                feedback.Text = summary?.Invoke(result) ?? (string.IsNullOrWhiteSpace(result) ? "操作已完成" : result);
                complete?.Invoke(result);
                RefreshSettings();
            }
            catch (Exception error)
            {
                if (!_closed && _page == page) feedback.Text = $"操作未完成（{error.GetType().Name}）";
            }
            finally { if (!_closed && _page == page) button.IsEnabled = true; }
        };
        Row(title, detail, button);
        _body.Children.Add(feedback);
    }
}
