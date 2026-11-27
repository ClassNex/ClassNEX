using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace ClassNex.Services;

internal sealed partial class NativeDesktop
{
    private sealed partial class NativeVerification
    {
        private async Task VerifyWidgetList()
        {
            var saved = await Ui(() => AppServices.Settings.Widgets.Select(w => w.Copy()).ToList());
            ListView? List() => Find<ListView>(Page, "WidgetSelection");
            void Click(string id) => new ButtonAutomationPeer(Need<Button>(Page, id)).Invoke();
            const string note = "本周值日班长";
            try
            {
                await Act("open-widgets-with-known-order", () =>
                {
                    AppServices.Settings.Widgets = [new() { Type = "clock", Order = 0 }, new() { Type = "text", Text = "班级公告", Order = 1 }];
                    AppServices.SaveSettings(); Settings.SelectWidget(0); Settings.NavigateTo("widget-layout");
                });
                var depth = await Ui(() => Frame.BackStackDepth);
                static Windows.Foundation.Point Origin(FrameworkElement target, UIElement relative) => target.TransformToVisual(relative).TransformPoint(default);
                await Check("widgets-start-in-horizontal-desktop-order", () =>
                    Find<ComboBox>(Page, "WidgetOrderMode") is { SelectedIndex: 0 } && Find<Grid>(Page, "WidgetStrip_0") is { AllowDrop: true } strip &&
                    Find<ToggleButton>(Page, "WidgetChip_0") is { CanDrag: true, IsChecked: true } left && Find<ToggleButton>(Page, "WidgetChip_1") is { CanDrag: true } right &&
                    Shown(left) && Shown(right) && Math.Abs(Origin(left, strip).Y - Origin(right, strip).Y) <= 1 && Origin(left,strip).X + left.ActualWidth <= Origin(right,strip).X &&
                    Find<ComboBox>(Page, "WidgetPresetSelection") is { IsEnabled: true } presets && Shown(presets) && List() == null, 5000);
                await Act("select-text-in-horizontal-strip", () => new ToggleButtonAutomationPeer(Need<ToggleButton>(Page, "WidgetChip_1")).Toggle());
                await Check("horizontal-selection-opens-matching-instance", () => Find<ToggleButton>(Page, "WidgetChip_1")?.IsChecked == true &&
                    Find<TextBox>(Page, "WidgetText") is { Text: "班级公告" } field && Shown(field), 4000);
                await Act("split-horizontal-row", () => Need<ComboBox>(Page, "WidgetRow").SelectedIndex = 1);
                await Check("separate-rows-have-separate-visible-strips", () => Find<Grid>(Page, "WidgetStrip_0") is { } above && Find<Grid>(Page, "WidgetStrip_1") is { AllowDrop: true } below &&
                    Shown(above) && Shown(below) && Origin(above, Page).Y + above.ActualHeight <= Origin(below, Page).Y &&
                    SettingsService.Load().Widgets is [{ Type: "clock", Row: 0 }, { Type: "text", Row: 1 }], 4000);
                await Snapshot("native-widget-rows.png", () => Settings.Content, true);
                await Act("combine-horizontal-rows", () => Need<ComboBox>(Page, "WidgetRow").SelectedIndex = 0);
                await Check("merged-horizontal-row-drops-empty-strip", () => Find<Grid>(Page, "WidgetStrip_1") == null && Find<ToggleButton>(Page, "WidgetChip_1") is { IsChecked: true }, 4000);
                await Act("open-per-row-library", () => Click("WidgetStripAdd_0"));
                await Check("row-add-opens-component-library", () => Find<Button>(Page, "WidgetAdd_text") is { IsEnabled: true } add && Shown(add) && Find<ToggleButton>(Page, "WidgetChip_1")?.IsChecked == false, 4000);
                await Act("restore-clock-inspector-from-row", () => new ToggleButtonAutomationPeer(Need<ToggleButton>(Page, "WidgetChip_0")).Toggle());
                await Check("row-returns-from-library-to-clock", () => Find<ToggleSwitch>(Page, "WidgetSeconds") is { } originalClock && Shown(originalClock) && Frame.BackStackDepth == depth, 4000);
                await Act("use-accessible-order-list", () => Need<ComboBox>(Page, "WidgetOrderMode").SelectedIndex = 1);
                await Check("widget-list-and-selected-inspector", () => List() is { Items.Count: 2, SelectedIndex: 0, CanDragItems: true, CanReorderItems: true, AllowDrop: true } &&
                    Shown(List()!) && Find<Grid>(Page, "WidgetStrip_0") == null && Find<SelectorBar>(Page, "WidgetLibrary") is { Items.Count: 2 } && Find<ToggleSwitch>(Page, "WidgetSeconds") is { } seconds && Shown(seconds), 4000);
                await Act("open-component-library", () => { var tabs = Need<SelectorBar>(Page, "WidgetLibrary"); tabs.SelectedItem = tabs.Items[0]; });
                await Check("widget-library-has-add-and-drag", () => Find<Button>(Page, "WidgetAdd_text") is { IsEnabled: true } &&
                    Find<Grid>(Page, "Row_WidgetAdd_text") is { CanDrag: true } && Find<TextBox>(Page, "WidgetText") == null, 4000);
                await Act("add-text-component", () => Click("WidgetAdd_text"));
                await Check("new-widget-selected-in-place", () => List() is { Items.Count: 3, SelectedIndex: 2 } && Frame.BackStackDepth == depth &&
                    Find<TextBox>(Page, "WidgetText") is { } field && Shown(field) && SettingsService.Load().Widgets.Select(w => w.Type).SequenceEqual(["clock", "text", "text"]), 4000);
                await Act("edit-new-text-only", () =>
                {
                    var field = Need<TextBox>(Page, "WidgetText"); field.Focus(FocusState.Keyboard); field.Text = note;
                    Need<Button>(Page, "WidgetCopy").Focus(FocusState.Keyboard);
                });
                await Check("editing-one-widget-preserves-its-neighbor", () => SettingsService.Load().Widgets is [_, { Text: "班级公告" }, { Text: note }], 4000);
                await Act("duplicate-text-component", () => Click("WidgetCopy"));
                await Check("widget-copy-keeps-text-and-selection", () => List() is { SelectedIndex: 3 } &&
                    SettingsService.Load().Widgets is [_, _, { Text: note }, { Text: note }], 4000);
                await Act("hide-widget-copy", () => new ToggleSwitchAutomationPeer(Need<ToggleSwitch>(Page, "WidgetEnabled")).Toggle());
                await Check("hiding-copy-leaves-original-visible", () => SwitchShows("WidgetEnabled", false) &&
                    SettingsService.Load().Widgets is [_, _, { IsEnabled: true }, { IsEnabled: false }], 4000);
                await Act("move-widget-copy-up", () => Click("WidgetUp"));
                await Check("widget-move-keeps-selected-instance", () => List() is { SelectedIndex: 2 } &&
                    SettingsService.Load().Widgets is [{ Order: 0 }, { Order: 1 }, { Order: 2, IsEnabled: false, Text: note }, { Order: 3, IsEnabled: true, Text: note }], 4000);
                await Act("remove-selected-widget-copy", () => Click("WidgetRemove"));
                await Check("widget-remove-preserves-original", () => List() is { Items.Count: 3, SelectedIndex: 2 } &&
                    SettingsService.Load().Widgets is [_, { Text: "班级公告" }, { IsEnabled: true, Text: note }], 4000);
                await Act("select-clock-in-master-list", () => Need<ListView>(Page, "WidgetSelection").SelectedIndex = 0);
                await Check("widget-list-switches-properties-without-navigation", () => List() is { SelectedIndex: 0 } && Frame.BackStackDepth == depth &&
                    Find<ToggleSwitch>(Page, "WidgetSeconds") is { } clock && Shown(clock) && Find<TextBox>(Page, "WidgetText") == null, 4000);
                await Act("move-clock-to-new-row", () => { var row = Need<ComboBox>(Page, "WidgetRow"); row.SelectedIndex = row.Items.Count - 1; });
                await Check("moving-widget-to-new-row-preserves-its-inspector", () => List() is { SelectedIndex: 2, SelectedItem: ListViewItem { Content: string title } } &&
                    title.Contains("第 2 行") && Find<ComboBox>(Page, "WidgetRow") is { SelectedIndex: 1 } && Find<ToggleSwitch>(Page, "WidgetSeconds") != null &&
                    SettingsService.Load().Widgets is [{ Row: 0, Text: "班级公告" }, { Row: 0, Text: note }, { Row: 1, Type: "clock" }], 4000);
                await Act("return-clock-to-existing-row", () => Need<ComboBox>(Page, "WidgetRow").SelectedIndex = 0);
                await Check("empty-widget-row-is-removed-without-changing-other-instances", () => Find<ComboBox>(Page, "WidgetRow") is { Items.Count: 2, SelectedIndex: 0 } &&
                    Frame.BackStackDepth == depth && SettingsService.Load().Widgets is [{ Row: 0, Text: "班级公告" }, { Row: 0, Text: note }, { Row: 0, Type: "clock" }], 4000);
                await VerifyWidgetAppearanceAndGestures();
            }
            finally
            {
                await Ui(() => { Settings.NavigateTo("widgets"); AppServices.Settings.Widgets = saved; AppServices.SaveSettings(); return true; });
            }
            await VerifyWidgetPresetControls();
            await VerifyWidgetClassroomPeriods();
        }
    }
}
