using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ClassNex.Services;

internal sealed partial class NativeDesktop
{
    private sealed partial class NativeVerification
    {
        private async Task VerifyWidgetPresetControls()
        {
            const string original = "默认方案", copy = "自习", renamed = "考试周", fresh = "日常";
            var saved = await Ui(() => AppServices.Settings.Widgets.Select(w => w.Copy()).ToList());
            ComboBox Selection() => Need<ComboBox>(Page, "WidgetPresetSelection");
            MenuFlyout Menu() => (MenuFlyout)Need<Button>(Page, "WidgetPresetMenu").Flyout;
            MenuFlyoutItem Item(string key) => Menu().Items.OfType<MenuFlyoutItem>().Single(i => AutomationProperties.GetAutomationId(i) == "WidgetPreset" + key);
            ContentDialog? Dialog() => Settings.Content is FrameworkElement { XamlRoot: { } root }
                ? VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Where(p => p.Child != null).SelectMany(p => Tree(p.Child)).OfType<ContentDialog>().FirstOrDefault() : null;
            TextBox Name() => Need<TextBox>(Dialog()!, "WidgetPresetName");
            bool Selected(string name) => Find<ComboBox>(Page, "WidgetPresetSelection") is { IsLoaded: true } picker && Equals(picker.SelectedItem, name);
            bool Listed(string name) => Selection().Items.Contains(name);
            async Task Open(string item, string step)
            {
                await Check(step + "-menu-ready", () => Dialog() == null && !Menu().IsOpen && Need<Button>(Page, "WidgetPresetMenu") is { IsEnabled: true } button && Shown(button), 4000);
                await Act(step + "-menu", () => new ButtonAutomationPeer(Need<Button>(Page, "WidgetPresetMenu")).Invoke());
                await Check(step + "-menu-opens", () => Menu().IsOpen && Item(item).IsEnabled && Shown(Item(item)), 4000);
                await Act(step + "-action", () => new MenuFlyoutItemAutomationPeer(Item(item)).Invoke());
                await Check(step + "-dialog-opens", () => Dialog() is { } dialog && Shown(dialog), 4000,
                    () => $"Menu open: {Menu().IsOpen}; source enabled: {Need<Button>(Page, "WidgetPresetMenu").IsEnabled}; dialog: {Dialog()?.Title ?? "none"}; notice: {Find<InfoBar>(Page, "WidgetPresetStatus")?.Message}");
            }
            async Task Answer(bool accept, string step)
            {
                var buttonName = accept ? "PrimaryButton" : "CloseButton";
                await Check(step + "-button-ready", () => Dialog() is { } dialog && (!accept || dialog.IsPrimaryButtonEnabled) &&
                    Tree(dialog).OfType<Button>().Any(b => b.Name == buttonName && b.IsEnabled && Shown(b)), 4000);
                await Act(step, () =>
                {
                    var dialog = Dialog()!;
                    var button = Tree(dialog).OfType<Button>().Single(b => b.Name == buttonName);
                    new ButtonAutomationPeer(button).Invoke();
                });
                await Check(step + "-dialog-closes", () => Dialog() == null, 4000);
            }

            try
            {
                await Act("arrange-preset-desktop", () =>
                {
                    AppServices.Settings.Widgets = [new() { Type = "text", Text = "初始通知" }]; AppServices.SaveSettings();
                    Settings.SelectWidget(0); Settings.NavigateTo("widget-layout");
                });
                await Check("preset-picker-does-not-materialize-a-legacy-install", () => Selected(original) && Selection().Items.Count == 1 && !Item("Delete").IsEnabled &&
                    !File.Exists(Path.Combine(SettingsService.DataDirectory, "WidgetLayoutPresets.json")), 4000);
                await Open("Copy", "copy-preset");
                await Act("type-existing-preset-name", () => Name().Text = original);
                await Check("preset-dialog-rejects-existing-name", () => Dialog() is { IsPrimaryButtonEnabled: false }, 4000);
                await Act("type-independent-preset-name", () => Name().Text = copy);
                await Snapshot("native-widget-preset-name.png", () => Dialog()!, true);
                await Answer(true, "save-preset-copy");
                await Check("saving-preset-copy-leaves-current-desktop", () => Selected(original) && Listed(copy) && Item("Delete").IsEnabled && SettingsService.Load().Widgets is [{ Text: "初始通知" }], 4000);
                await Act("switch-preset-with-field-and-slider-pending", () =>
                {
                    var field = Need<TextBox>(Page, "WidgetText"); field.Focus(FocusState.Keyboard); field.Text = "尚未失焦的通知";
                    Need<Slider>(Page, "WidgetFontScale").Value = 1.55;
                    Selection().SelectedItem = copy;
                });
                await Check("preset-copy-loads-original-values", () => Selected(copy) && SettingsService.Load().Widgets is [{ Text: "初始通知", FontScale: 1 }] && Need<TextBox>(Page, "WidgetText").Text == "初始通知", 4000);
                await Act("return-to-edited-preset", () => Selection().SelectedItem = original);
                await Check("switching-preset-flushes-both-field-and-slider", () => Selected(original) && SettingsService.Load().Widgets is [{ Text: "尚未失焦的通知" } w] && Math.Abs(w.FontScale - 1.55) < .001 && Need<TextBox>(Page, "WidgetText").Text == w.Text, 4000);
                await Open("New", "new-preset");
                await Act("type-new-preset-name", () => Name().Text = fresh);
                await Answer(true, "create-new-preset");
                await Check("new-preset-starts-with-date-clock-and-schedule", () => Selected(fresh) && SettingsService.Load().Widgets.Select(w => w.Type).SequenceEqual(["date", "clock", "schedule"]), 4000);
                await Open("Rename", "rename-preset");
                await Act("type-new-preset-label", () => Name().Text = renamed);
                await Answer(true, "confirm-preset-rename");
                await Check("renaming-active-preset-keeps-layout", () => Selected(renamed) && !Listed(fresh) && SettingsService.Load().Widgets.Count == 3, 4000);
                await Open("Delete", "cancel-preset-delete");
                await Check("delete-dialog-excludes-active-preset", () => Need<ComboBox>(Dialog()!, "WidgetPresetDeleteSelection") is { } options && !options.Items.Contains(renamed) && options.Items.Count == 2, 4000);
                await Answer(false, "keep-inactive-preset");
                await Check("cancel-preset-delete-keeps-all-layouts", () => Selection().Items.Count == 3 && Selected(renamed), 4000);
                await Open("Delete", "delete-preset");
                await Act("choose-inactive-preset", () => Need<ComboBox>(Dialog()!, "WidgetPresetDeleteSelection").SelectedItem = copy);
                await Answer(true, "confirm-preset-delete");
                await Check("deleting-inactive-preset-preserves-edited-original", () => Selected(renamed) && !Listed(copy) && Listed(original), 4000);
                await Act("reopen-widget-presets", () => { Settings.NavigateTo("widgets"); Settings.NavigateTo("widget-layout"); });
                await Check("reentering-page-restores-preset-selection", () => Selected(renamed) && Selection().Items.Count == 2, 4000);
                var path = Path.Combine(SettingsService.DataDirectory, "WidgetLayoutPresets.json");
                const string damaged = "{broken fixture";
                await Act("arrange-damaged-anonymous-preset-file", () =>
                {
                    Settings.NavigateTo("widgets"); File.WriteAllText(path, damaged); Settings.NavigateTo("widget-layout");
                });
                InfoBar Notice() => Need<InfoBar>(Page, "WidgetPresetStatus");
                await Check("damaged-presets-leave-the-widget-editor-available", () => Find<ComboBox>(Page, "WidgetPresetSelection") is { IsLoaded: true, IsEnabled: false } && Notice() is { IsOpen: true, ActionButton: Button } &&
                    Find<Slider>(Page, "WidgetFontScale") is { IsEnabled: true } && File.ReadAllText(path) == damaged, 4000);
                await Snapshot("native-widget-preset-recovery.png", () => Settings.Content, true);
                await Act("request-preset-recovery", () => new ButtonAutomationPeer((Button)Notice().ActionButton).Invoke());
                await Check("preset-recovery-requires-confirmation", () => Dialog() is { DefaultButton: ContentDialogButton.Close } && File.ReadAllText(path) == damaged, 4000);
                await Answer(true, "confirm-preset-recovery");
                await Check("recovered-presets-retain-damaged-bytes-and-live-desktop", () => Selection().IsEnabled && Selected(original) && Selection().Items.Count == 1 &&
                    Notice().Severity == InfoBarSeverity.Success && Directory.GetFiles(SettingsService.DataDirectory, "WidgetLayoutPresets.json.*.bak").Any(p => File.ReadAllText(p) == damaged) &&
                    SettingsService.Load().Widgets.Select(w => w.Type).SequenceEqual(["date", "clock", "schedule"]), 4000);
            }
            finally
            {
                await Ui(() => { Dialog()?.Hide(); Settings.NavigateTo("widgets"); AppServices.Settings.Widgets = saved; AppServices.SaveSettings(); return true; });
            }
        }
    }
}
