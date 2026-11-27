using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using ClassNex.Models;
using Microsoft.UI.Xaml.Automation.Peers;
using Ac = Avalonia.Controls;
using Wu = Microsoft.UI.Xaml.Controls;

namespace ClassNex.Services;

internal sealed partial class NativeDesktop
{
    private sealed partial class NativeVerification
    {
        private async Task VerifyWidgetAppearanceAndGestures()
        {
            var window = host._overlay;
            var clock = await Ui(() => AppServices.Settings.Widgets.Single(w => w.Type == "clock"));
            var first = await Ui(() => AppServices.Settings.Widgets[0]);
            Ac.Border? Surface() => window.GetVisualDescendants().OfType<Ac.Border>().FirstOrDefault(b => ReferenceEquals(b.Tag, clock));
            await Act("open-selected-widget-appearance", () => { var panel = Need<Wu.Expander>(Page, "WidgetAppearance"); panel.IsExpanded = true; panel.StartBringIntoView(); });
            await Check("widget-appearance-is-optional", () => Find<Wu.NumberBox>(Page, "WidgetWidth") is { } width && Shown(width) && double.IsNaN(width.Value) && Surface() == null, 4000);
            await Act("adjust-only-the-clock-surface", () => { Need<Wu.NumberBox>(Page, "WidgetWidth").Value = 160; Need<Wu.NumberBox>(Page, "WidgetMarginLeft").Value = 9;
                Need<Wu.ComboBox>(Page, "WidgetAlign").SelectedIndex = 3; Need<Wu.Slider>(Page, "WidgetOpacity").Value = .68; });
            await Check("widget-width-margin-alignment-opacity-reach-live-classroom", () => Surface() is { Width: 160, Margin.Left: 9, Child: { HorizontalAlignment: Avalonia.Layout.HorizontalAlignment.Right } } surface &&
                Math.Abs(surface.Opacity - .68) < .001 && SettingsService.Load().Widgets.Last().Presentation is { Width: 160, Margin: [9, 0, 0, 0], Alignment: "right", Opacity: .68 } && first.Presentation == null, 5000);
            var child = await Ui(() => Surface()!.Child);
            await Act("modify-surface-without-replacing-clock", () => { Need<Wu.NumberBox>(Page, "WidgetBackgroundOpacity").Value = 45; Need<Wu.NumberBox>(Page, "WidgetRadius").Value = 10; });
            await Check("widget-radius-and-background-update-without-recreating-clock", () => Surface() is { CornerRadius.TopLeft: 10, Background: not null } shell && ReferenceEquals(child, shell.Child) &&
                SettingsService.Load().Widgets.Last().Presentation is { CornerRadius: 10, BackgroundOpacity: .45 }, 4000);
            await Snapshot("native-widget-appearance.png", () => Settings.Content, true);
            await Act("restore-clock-surface", () => new ButtonAutomationPeer(Need<Wu.Button>(Page, "WidgetAppearanceReset")).Invoke());
            await Check("reset-appearance-keeps-original-clock-control-and-settings", () => Surface() == null && child?.IsAttachedToVisualTree() == true &&
                SettingsService.Load().Widgets is [{ Text: "班级公告" }, _, { Type: "clock", Presentation: null }], 4000);

            Ac.TextBlock? Handle(WidgetConfig config) => window.GetVisualDescendants().OfType<Ac.TextBlock>().FirstOrDefault(h => ReferenceEquals(h.Tag, config));
            Ac.Button? Gear() => window.GetVisualDescendants().OfType<Ac.Button>().FirstOrDefault(b => ReferenceEquals(b.Tag, clock));
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            Point Center(Ac.Control target) => target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window) ?? throw new InvalidOperationException("Gesture target has no classroom window");
            void Route(RawPointerEventType type, Point point, RawInputModifiers modifiers = RawInputModifiers.None)
            {
                var target = (pointer.Captured ?? window.InputHitTest(point)) as InputElement ?? throw new InvalidOperationException("No hit target for the isolated pointer");
                var time = (ulong)Environment.TickCount64;
                var props = new PointerPointProperties(modifiers, type switch { RawPointerEventType.LeftButtonDown => PointerUpdateKind.LeftButtonPressed,
                    RawPointerEventType.LeftButtonUp => PointerUpdateKind.LeftButtonReleased, _ => PointerUpdateKind.Other });
                target.RaiseEvent(type switch {
                    RawPointerEventType.LeftButtonDown => new PointerPressedEventArgs(target, pointer, window, point, time, props, KeyModifiers.None, 1),
                    RawPointerEventType.LeftButtonUp => new PointerReleasedEventArgs(target, pointer, window, point, time, props, KeyModifiers.None, MouseButton.Left),
                    _ => new PointerEventArgs(InputElement.PointerMovedEvent, target, pointer, window, point, time, props, KeyModifiers.None) });
            }
            void Click(Ac.Control c) { var p = Center(c); Route(RawPointerEventType.Move, p); Route(RawPointerEventType.LeftButtonDown, p, RawInputModifiers.LeftMouseButton); Route(RawPointerEventType.LeftButtonUp, p); }
            static Ac.Flyout? Drawer(Ac.Button button) => Ac.Primitives.FlyoutBase.GetAttachedFlyout(button) as Ac.Flyout;
            var wasEditing = await Ui(() => window.IsEditMode);
            var hwnd = await Ui(() => window.TryGetPlatformHandle()?.Handle ?? 0);
            var ownsPrivateWindow = await Ui(() =>
            {
                if (hwnd == 0 || GetWindowThreadProcessId(hwnd, out var pid) is not (> 0) || pid != Environment.ProcessId) return false;
                var desktop = WidgetThreadDesktop(GetWindowThreadProcessId(hwnd, out _)); var name = new StringBuilder(128);
                return WidgetDesktopName(desktop, 2, name, name.Capacity * 2, out _) && name.ToString().StartsWith("ClassNEX-review-", StringComparison.Ordinal);
            });
            void NativeMouse(uint message, Point at, bool down = false)
            {
                if (!ownsPrivateWindow) throw new InvalidOperationException("Native pointer messages require this fixture's private desktop.");
                var origin = window.PointToScreen(default); var pixel = window.PointToScreen(at) - origin;
                WidgetMouse(hwnd, message, down ? 1 : 0, ((pixel.Y & 0xffff) << 16) | (pixel.X & 0xffff));
            }
            bool nativeGear = false, nativeDrag = false;
            try
            {
                await Act("enter-desktop-widget-editor", window.EnterEditMode);
                await Check("desktop-gear-and-grips-are-real-hit-targets", () => Gear() is { } gear && Handle(first) is { } start && Handle(clock) is { } end &&
                    window.InputHitTest(Center(start)) == start && window.InputHitTest(Center(end)) == end && window.InputHitTest(Center(gear)) is Avalonia.Visual hit &&
                    (hit == gear || hit.GetVisualAncestors().Contains(gear)), 4500);
                await Act("press-real-gear-in-classroom", () =>
                {
                    if (ownsPrivateWindow) { var p = Center(Gear()!); NativeMouse(0x0200, p); NativeMouse(0x0201, p, true); NativeMouse(0x0202, p); }
                    nativeGear = Drawer(Gear()!)?.IsOpen == true;
                    if (!nativeGear) Click(Gear()!);
                });
                await Check("desktop-gear-opens-selected-clock-drawer", () => Gear() is { } b && Drawer(b) is { IsOpen: true, Content: Ac.StackPanel p } && p.GetVisualDescendants().OfType<Ac.ToggleSwitch>()
                    .Any(t => Avalonia.Automation.AutomationProperties.GetName(t) == "显示秒数") && !p.GetVisualDescendants().OfType<Ac.TextBox>().Any(), 4000);
                var drawer = await Ui(() => Drawer(Gear()!)!);
                await Act("change-opacity-in-live-drawer", () =>
                {
                    var p = (Ac.StackPanel)drawer.Content!;
                    p.GetVisualDescendants().OfType<Ac.Slider>().Single(s => Avalonia.Automation.AutomationProperties.GetName(s) == "组件不透明度").Value = .75;
                    var scale = Ac.TopLevel.GetTopLevel(p)!.RenderScaling;
                    using var image = new Avalonia.Media.Imaging.RenderTargetBitmap(PixelSize.FromSize(p.Bounds.Size, scale), new Vector(96 * scale, 96 * scale));
                    image.Render(p); image.Save(Path.Combine(SettingsService.DataDirectory, "desktop-clock-properties.png"));
                });
                await Check("drawer-edit-updates-same-clock-without-closing", () => drawer.IsOpen && Surface() is { Opacity: .75 } && SettingsService.Load().Widgets.Last().Presentation?.Opacity == .75, 4000);
                await Act("close-clock-drawer", drawer.Hide);
                await Check("closing-drawer-preserves-desktop-editor", () => !drawer.IsOpen && window.IsEditMode && Handle(clock) is { IsEffectivelyVisible: true }, 3000);

                Point begin = default, end = default;
                await Act("desktop-grip-down", () =>
                {
                    begin = Center(Handle(first)!); end = Center(Handle(clock)!);
                    if (ownsPrivateWindow)
                    {
                        NativeMouse(0x0200, begin); NativeMouse(0x0201, begin, true);
                        for (var i = 1; i <= 5; i++) NativeMouse(0x0200, begin + (end - begin) * (i / 5d), true);
                        NativeMouse(0x0202, end); nativeDrag = ReferenceEquals(AppServices.Settings.Widgets.Last(), first);
                    }
                    if (!nativeDrag) { Route(RawPointerEventType.Move, begin); Route(RawPointerEventType.LeftButtonDown, begin, RawInputModifiers.LeftMouseButton); }
                });
                if (!nativeDrag)
                {
                    await Act("desktop-grip-move-past-threshold", () => { for (var i = 1; i <= 5; i++) Route(RawPointerEventType.Move, begin + (end - begin) * (i / 5d), RawInputModifiers.LeftMouseButton); });
                    await Act("desktop-grip-release-on-clock", () => Route(RawPointerEventType.LeftButtonUp, end));
                }
                await Check("pointer-drag-reorders-live-desktop-and-disk", () => SettingsService.Load().Widgets is [{ Text: "本周值日班长" }, { Type: "clock" }, { Text: "班级公告" }] &&
                    Handle(clock) is { } left && Handle(first) is { } right && Center(left).X < Center(right).X, 4500);
                static string PathUsed(bool native) => native ? "targeted Win32 mouse messages to the private fixture HWND" : "hit-tested Avalonia routed pointer packets with capture";
                await File.WriteAllTextAsync(Path.Combine(SettingsService.DataDirectory, "widget-input-path.txt"), $"Gear: {PathUsed(nativeGear)}\nDesktop drag: {PathUsed(nativeDrag)}\nNo hardware input, SendInput, cursor move, or desktop switch.\n");
            }
            finally { await Ui(() => { if (Gear() is { } b) Drawer(b)?.Hide(); if (!wasEditing) window.ExitEditMode(); return true; }); }
        }

        [DllImport("user32", EntryPoint = "GetThreadDesktop")] private static extern nint WidgetThreadDesktop(uint thread);
        [DllImport("user32", EntryPoint = "GetUserObjectInformationW", CharSet = CharSet.Unicode)] private static extern bool WidgetDesktopName(nint desk, int index, StringBuilder value, int size, out uint needed);
        [DllImport("user32", EntryPoint = "SendMessageW")] private static extern nint WidgetMouse(nint hwnd, uint msg, nint wp, nint lp);
    }
}
