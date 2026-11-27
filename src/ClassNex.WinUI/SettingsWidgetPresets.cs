using ClassNex.Models;
using ClassNex.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClassNex.WinUI;

public sealed partial class NativeSettingsWindow
{
    private readonly WidgetLayoutPresetStore _widgetPresets = new(Path.Combine(SettingsService.DataDirectory, "WidgetLayoutPresets.json"));
    private enum PresetEdit { New, Copy, Rename }

    private void AddWidgetPresetPicker()
    {
        var selector = Target("WidgetPresetSelection", "当前布局方案", new ComboBox { MinWidth = 128, MaxWidth = 200, PlaceholderText = "暂不可用" });
        var menu = new MenuFlyout();
        var more = Target("WidgetPresetMenu", "管理布局方案", new Button { Content = new FontIcon { Glyph = "\uE712", FontSize = 15 }, Flyout = menu });
        ToolTipService.SetToolTip(more, "管理布局方案");
        var editors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        editors.Children.Add(selector); editors.Children.Add(more);
        Row("布局方案", "切换桌面上的组件及其排列。", editors);
        var status = Target("WidgetPresetStatus", "布局方案提示", new InfoBar { Visibility = Visibility.Collapsed, IsClosable = false });
        _body.Children.Add(status);
        WidgetLayoutPresetIndex? index = null;
        bool loading = false;
        var delete = new MenuFlyoutItem();

        bool Here() => !_closed && CurrentPageKey == "widget-layout" && _targets.GetValueOrDefault("WidgetPresetSelection") == selector;
        void Run(Func<Task> action) => RunUserAction(async () =>
        {
            if (!Here()) return;
            selector.IsEnabled = more.IsEnabled = false;
            try { CommitInputs(); await action(); }
            catch (Exception error) when (PresetFailure(error))
            {
                if (_closed || CurrentPageKey != "widget-layout") return;
                if (Here()) Load();
                if (_targets.GetValueOrDefault("WidgetPresetStatus") is not InfoBar visible) return;
                if (!visible.IsOpen) ShowPresetStatus(visible, "布局方案操作未完成", error.Message);
                else if (visible.Message != error.Message) visible.Message += "\n" + error.Message;
            }
            finally { if (Here()) selector.IsEnabled = more.IsEnabled = index != null; }
        });
        void Item(string id, string title, Func<Task> action, MenuFlyoutItem? item = null)
        {
            item ??= new(); item.Text = title;
            Identify(item, id, title); item.Click += (_, _) => Run(action); menu.Items.Add(item);
        }
        Item("WidgetPresetNew", "新建方案…", () => Name(PresetEdit.New));
        Item("WidgetPresetCopy", "保存为副本…", () => Name(PresetEdit.Copy));
        Item("WidgetPresetRename", "重命名当前方案…", () => Name(PresetEdit.Rename));
        menu.Items.Add(new MenuFlyoutSeparator());
        Item("WidgetPresetDelete", "删除其他方案…", Delete, delete);

        void Load()
        {
            loading = true;
            try
            {
                index = _widgetPresets.Read(_bridge.ReadSettings().Widgets);
                selector.ItemsSource = index.Names; selector.SelectedItem = index.Active;
                selector.IsEnabled = more.IsEnabled = true; delete.IsEnabled = index.Names.Count > 1;
                status.IsOpen = false; status.Visibility = Visibility.Collapsed; status.ActionButton = null;
            }
            catch (Exception error) when (PresetFailure(error))
            {
                index = null; selector.ItemsSource = null; selector.IsEnabled = more.IsEnabled = false;
                var corrupt = error is InvalidDataException;
                var button = new Button { Content = corrupt ? "备份并恢复…" : "重试" };
                Identify(button, corrupt ? "WidgetPresetRecover" : "WidgetPresetRetry", (string)button.Content);
                button.Click += (_, _) => Run(async () =>
                {
                    if (!corrupt) { Load(); return; }
                    if (!await Confirm("从当前桌面恢复布局方案？", "先保留原方案文件的备份，再以当前桌面建立默认方案。课表与其他设置不变。", "备份并恢复") || !Here()) return;
                    CommitInputs();
                    var backup = _widgetPresets.Recover(_bridge.ReadSettings().Widgets);
                    Load();
                    if (index is not null) ShowPresetStatus(status, "已恢复布局方案", $"原文件已保存在数据目录：{Path.GetFileName(backup)}", InfoBarSeverity.Success);
                });
                ShowPresetStatus(status, "暂时无法读取布局方案", error.Message, action: button);
            }
            finally { loading = false; }
        }

        async Task Name(PresetEdit mode)
        {
            if (index is not { } original) return;
            var name = await AskWidgetPresetName(mode, original);
            if (name is null || !Here()) return;
            CommitInputs();
            var live = _bridge.ReadSettings().Widgets;
            switch (mode)
            {
                case PresetEdit.New: SwitchWidgetPreset(original.Active, name, create: true); break;
                case PresetEdit.Copy: _widgetPresets.SaveCopy(original.Active, name, live); Load(); break;
                case PresetEdit.Rename: _widgetPresets.Rename(original.Active, name, live); Load(); break;
            }
        }

        async Task Delete()
        {
            if (index is not { } original || original.Names.Count < 2) return;
            var choices = original.Names.Where(name => name != original.Active).ToArray();
            var picker = new ComboBox { Header = "要删除的方案", ItemsSource = choices, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            Identify(picker, "WidgetPresetDeleteSelection", "要删除的方案");
            var prompt = new ContentDialog { XamlRoot = _shell.XamlRoot, Title = "删除布局方案？", Content = picker, PrimaryButtonText = "删除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
            Identify(prompt, "WidgetPresetDeleteDialog", "删除布局方案");
            if (await prompt.ShowAsync() != ContentDialogResult.Primary || !Here() || picker.SelectedItem is not string name) return;
            CommitInputs(); _widgetPresets.Delete(original.Active, name, _bridge.ReadSettings().Widgets); Load();
        }

        selector.SelectionChanged += (_, _) =>
        {
            if (loading || index is not { } current || selector.SelectedItem is not string name || name == current.Active || !Here()) return;
            if (_commandPending) { loading = true; try { selector.SelectedItem = current.Active; } finally { loading = false; } return; }
            Run(() => { SwitchWidgetPreset(current.Active, name); return Task.CompletedTask; });
        };
        Load();
    }

    private void SwitchWidgetPreset(string active, string name, bool create = false)
    {
        var settings = _bridge.ReadSettings();
        var previous = settings.Widgets;
        bool installed = false;
        void Install(List<WidgetConfig> widgets)
        {
            settings.Widgets = widgets;
            try { _bridge.SaveSettings(); }
            catch { settings.Widgets = previous; throw; }
            _widgetIndex = 0; _widgetLibrary = false; installed = true;
        }
        try
        {
            if (create) _widgetPresets.Create(active, name, previous, Install);
            else _widgetPresets.Switch(active, name, previous, Install);
        }
        finally { if (installed && !_closed && CurrentPageKey == "widget-layout") RenderPage(); }
    }

    private static bool PresetFailure(Exception error) => error is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException;

    private static void ShowPresetStatus(InfoBar bar, string title, string message, InfoBarSeverity severity = InfoBarSeverity.Warning, Button? action = null)
    {
        bar.Severity = severity; bar.Title = title; bar.Message = message; bar.ActionButton = action;
        bar.Visibility = Visibility.Visible; bar.IsOpen = true;
    }

    private async Task<string?> AskWidgetPresetName(PresetEdit mode, WidgetLayoutPresetIndex index)
    {
        var (title, verb) = mode switch { PresetEdit.New => ("新建布局方案", "创建并切换"), PresetEdit.Copy => ("保存布局副本", "保存"), _ => ("重命名当前方案", "重命名") };
        var field = new TextBox { Header = "方案名称", MaxLength = 64, PlaceholderText = "例如：考试周" };
        if (mode == PresetEdit.Rename) field.Text = index.Active;
        if (mode == PresetEdit.Copy)
        {
            var stem = index.Active.Length <= 54 ? index.Active : "布局";
            var name = stem + " 副本";
            for (int i = 2; index.Names.Contains(name, StringComparer.OrdinalIgnoreCase); i++) name = $"{stem} 副本 {i}";
            field.Text = name;
        }
        Identify(field, "WidgetPresetName", "方案名称");
        var hint = SettingCaption("");
        var body = new StackPanel { Spacing = 6 }; body.Children.Add(field); body.Children.Add(hint);
        var dialog = new ContentDialog { XamlRoot = _shell.XamlRoot, Title = title, Content = body, PrimaryButtonText = verb, CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
        Identify(dialog, "WidgetPresetNameDialog", title);
        void Validate()
        {
            var name = field.Text.Trim();
            var duplicate = index.Names.Any(n => (mode != PresetEdit.Rename || n != index.Active) && StringComparer.OrdinalIgnoreCase.Equals(n, name));
            hint.Text = duplicate ? "已有同名方案，请换一个名称。" : ""; hint.Visibility = duplicate ? Visibility.Visible : Visibility.Collapsed;
            dialog.IsPrimaryButtonEnabled = name.Length > 0 && !name.Any(char.IsControl) && !duplicate && (mode != PresetEdit.Rename || name != index.Active);
        }
        field.TextChanged += (_, _) => Validate(); field.Loaded += (_, _) => field.SelectAll(); Validate();
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? field.Text.Trim() : null;
    }
}
