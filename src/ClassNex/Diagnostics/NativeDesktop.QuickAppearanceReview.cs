using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClassNex.Models;

namespace ClassNex.Services;

internal sealed partial class NativeDesktop
{
    private sealed partial class NativeVerification
    {
        private async Task VerifyDesktopQuickAppearance()
        {
            var window = host._overlay;
            var before = await Ui(() => (window.IsEditMode, AppServices.Settings.BackgroundOpacity, AppServices.Settings.MainWindowScale));
            var originalWidgets = await Ui(() => AppServices.Settings.Widgets);
            Button? Entry() => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => AutomationProperties.GetName(b) == "桌面外观");
            Flyout? OpenDrawer() => Entry() is { } b ? FlyoutBase.GetAttachedFlyout(b) as Flyout : null;
            var nextScale = before.MainWindowScale > 1.2 ? 1.1 : 1.35;
            var nextOpacity = before.BackgroundOpacity > .5 ? .4 : .65;
            try
            {
                await Act("enter-desktop-quick-appearance", window.EnterEditMode);
                await Check("desktop-appearance-button-is-reachable", () => Entry() is { IsEffectivelyVisible: true, Bounds.Width: >= 32 }, 3500);
                var entry = await Ui(() => Entry()!);
                await Act("open-desktop-quick-appearance", () => entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                await Check("desktop-appearance-opens-with-two-live-sliders", () => OpenDrawer() is { IsOpen: true, Content: StackPanel p } && p.GetVisualDescendants().OfType<Slider>().Count() == 2, 3500);
                var immediate = false;
                await Act("drag-quick-appearance-sliders", () =>
                {
                    var p = (StackPanel)OpenDrawer()!.Content!;
                    Slider Level(string name) => p.GetVisualDescendants().OfType<Slider>().Single(s => AutomationProperties.GetName(s) == name);
                    Level("主界面缩放").Value = nextScale; Level("背景不透明度").Value = nextOpacity;
                    var root = window.FindControl<LayoutTransformControl>("RootScale")!;
                    immediate = root.LayoutTransform is ScaleTransform scale && Math.Abs(scale.ScaleX - nextScale * AppServices.Settings.FontScale) < .001 &&
                        Math.Abs(window.FindControl<Border>("IslandBackground")!.Opacity - nextOpacity) < .001 &&
                        AppServices.Settings.MainWindowScale == before.MainWindowScale && AppServices.Settings.BackgroundOpacity == before.BackgroundOpacity;
                });
                await Check("quick-appearance-previews-before-saving-and-keeps-its-anchor", () => immediate && OpenDrawer()?.IsOpen == true && ReferenceEquals(entry, Entry()) &&
                    SettingsService.Load() is { } saved && Math.Abs(saved.MainWindowScale - nextScale) < .001 && Math.Abs(saved.BackgroundOpacity - nextOpacity) < .001, 4000);
                await Act("close-desktop-appearance", () => OpenDrawer()!.Hide());
                var clock = new WidgetConfig { Type = "clock", ShowSeconds = true };
                await Act("arrange-quick-row-actions", () => { AppServices.Settings.Widgets = [clock, new() { Type = "date", Order = 1 }]; AppServices.SaveSettings(); });
                MenuItem RowItem(string caption)
                {
                    var handle = window.GetVisualDescendants().OfType<TextBlock>().Single(h => ReferenceEquals(h.Tag, clock));
                    var item = ((Panel)handle.Parent!).Children.OfType<Button>().Select(b => b.Flyout).OfType<MenuFlyout>().Single();
                    return item.Items.OfType<MenuItem>().Single(i => Equals(i.Header, caption));
                }
                await Act("move-clock-to-new-row-from-desktop", () => RowItem("移到新的一行").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));
                await Check("desktop-new-row-updates-order-and-available-actions", () => SettingsService.Load().Widgets is [{ Type: "date", Row: 0 }, { Type: "clock", Row: 1, ShowSeconds: true }] &&
                    RowItem("移到上一行").IsEnabled && !RowItem("移到下一行").IsEnabled && !RowItem("移到新的一行").IsEnabled, 3500);
                await Act("return-clock-to-first-row-from-desktop", () => RowItem("移到上一行").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));
                await Check("desktop-moving-back-removes-empty-row", () => SettingsService.Load().Widgets is [{ Type: "date", Row: 0 }, { Type: "clock", Row: 0, ShowSeconds: true }] && !RowItem("移到上一行").IsEnabled, 3500);
            }
            finally
            {
                await Ui(() => { OpenDrawer()?.Hide(); if (!before.IsEditMode) window.ExitEditMode();
                    AppServices.Settings.Widgets = originalWidgets; AppServices.Settings.MainWindowScale = before.MainWindowScale; AppServices.Settings.BackgroundOpacity = before.BackgroundOpacity; AppServices.SaveSettings(); return true; });
            }
        }
    }
}
