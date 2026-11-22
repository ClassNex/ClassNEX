using ClassNex.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
        Text = title, FontSize = 20, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 2)
    });

    private T Target<T>(string id, string title, T element) where T : FrameworkElement
    {
        Identify(element, id, title);
        _targets[id] = element;
        if (_currentExpander != null) _detailOwners[id] = _currentExpander;
        return element;
    }

    private void Row(string title, string detail, FrameworkElement editor)
    {
        var grid = new Grid { ColumnSpacing = 14, RowSpacing = 4, Padding = new Thickness(4, 5, 4, 5) };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        label.Children.Add(Text(title));
        if (detail.Length != 0) label.Children.Add(Text(detail, "CaptionTextBlockStyle"));
        grid.Children.Add(label);
        Grid.SetColumn(editor, 1);
        editor.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(editor);
        bool? stacked = null;
        grid.SizeChanged += (_, args) =>
        {
            var threshold = editor is ToggleSwitch ? 225 : editor is ComboBox or CalendarDatePicker or NumberBox ? 315 : 350;
            var small = args.NewSize.Width < threshold;
            if (small == stacked) return;
            stacked = small;
            Grid.SetColumnSpan(label, small ? 2 : 1);
            Grid.SetRow(editor, small ? 1 : 0);
            Grid.SetColumn(editor, small ? 0 : 1);
            Grid.SetColumnSpan(editor, small ? 2 : 1);
            editor.HorizontalAlignment = small ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        };
        _body.Children.Add(grid);
    }

    private ToggleSwitch Switch(string id, string title, Func<AppSettings, bool> get, Action<AppSettings, bool> set, string detail = "")
    {
        var toggle = Target(id, title, new ToggleSwitch { OnContent = "开", OffContent = "关" });
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
        var field = Target(id, title, new TextBox { Header = title, Description = detail.Length == 0 ? null : detail, AcceptsReturn = multiline,
            MinHeight = multiline ? 72 : 32, MaxHeight = multiline ? 152 : 64, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap });
        var dirty = false;
        _bindings.Add(s => { var v = get(s) ?? ""; if (!dirty && field.Text != v) field.Text = v; });
        field.TextChanged += (_, _) => { if (!_refreshing && field.FocusState != FocusState.Unfocused) dirty = true; };
        void Commit()
        {
            if (!dirty || !_targets.TryGetValue(id, out var current) || current != field) return;
            dirty = false;
            if ((get(_bridge.ReadSettings()) ?? "") != field.Text) Edit(s => set(s, field.Text));
        }
        field.LostFocus += (_, _) => Commit();
        _fieldCommits.Add(Commit);
        field.KeyDown += (_, args) => { if (!multiline && args.Key == VirtualKey.Enter) { Commit(); args.Handled = true; } };
        _body.Children.Add(field);
        return field;
    }

    private void StatusRow(string id, string title, Func<AppSettings, string> text)
    {
        var label = Target(id, title, Text(""));
        label.IsTextSelectionEnabled = true;
        label.MaxWidth = 300;
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
