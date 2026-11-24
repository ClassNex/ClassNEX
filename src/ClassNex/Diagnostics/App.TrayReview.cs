#if DEBUG
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.VisualTree;
using ClassNex.Services;
using ClassNex.Styles;
using ClassNex.Views;

namespace ClassNex;

public partial class App
{
    private async Task ReviewTrayAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        if (!File.Exists(Path.Combine(directory, ".classnex-review")) || AccountService.Current != null || AppServices.Settings.CentralManagement.IsEnabled)
        { desktop.Shutdown(2); return; }
        var checks = new List<string>();
        string? failure = null;
        try
        {
            void Check(bool passed, string reason)
            {
                if (!passed) throw new InvalidOperationException(reason);
                checks.Add(reason);
                File.AppendAllText(Path.Combine(directory, "tray-progress.log"), $"{DateTimeOffset.Now:O} {reason}{Environment.NewLine}");
            }
            var owner = (Window?)_mainWindow ?? _oobeWindow!;
            var simulatedPointer = new PixelPoint(32, 32);
            var pointerReads = 0;
            PixelPoint? ReadPointer() { pointerReads++; return simulatedPointer; }
            if (_oobeWindow is not null)
            {
                var cancelledAttempts = 0;
                var cancelled = new WindowsTrayIcon(owner, (_, _) => { }, ReadPointer, (_, _) => { cancelledAttempts++; return default; });
                var hadRetry = cancelled.RegistrationRetryPending;
                cancelled.Dispose();
                await Task.Delay(WindowsTrayIcon.FirstRegistrationRetry + 150);
                Check(hadRetry && !cancelled.RegistrationRetryPending && cancelledAttempts == 1, "Disposing a temporarily unregistered icon cancels its pending retry");
            }
            var attempts = new List<long>();
            var registrationClock = Stopwatch.StartNew();
            (bool Registered, bool Version4) DelayedShell(nint window, nint image)
            {
                attempts.Add(registrationClock.ElapsedMilliseconds);
                return attempts.Count < 3 ? default : WindowsTrayIcon.RegisterWithShell(window, image);
            }
            InstallTray(owner, ReadPointer, DelayedShell);
            var overlay = _mainWindow; var tray = _trayIcon!;
            var saved = AppServices.Settings.IsMainWindowVisible;
            Check(!tray.Registered && tray.RegistrationRetryPending && attempts.Count == 1, "An initial shell registration failure schedules one retry, even with no classroom visible");
            while (!tray.Registered && registrationClock.ElapsedMilliseconds < 8000) await Task.Delay(20);
            Check(tray.Registered && !tray.RegistrationRetryPending && attempts.Count == 3 &&
                attempts[1] - attempts[0] >= WindowsTrayIcon.FirstRegistrationRetry - 80 && attempts[2] - attempts[1] >= 2 * WindowsTrayIcon.FirstRegistrationRetry - 80,
                "Two temporary failures recover with increasing delays; the real registered shell icon is no longer polled");
            EnsureTrayMenu(); var menu = _trayMenu!;
            var hwnd = owner.TryGetPlatformHandle()!.Handle;
            var icon = tray.IconBounds();
            var work = (icon is { } area ? menu.Screens.ScreenFromPoint(area.Center) : menu.Screens.Primary)!.WorkingArea;
            var point = icon?.Center ?? new PixelPoint(work.Right - 12, work.Bottom - 12);
            simulatedPointer = point;
            var buttons = menu.GetVisualDescendants().OfType<Button>().ToArray();
            var opens = 0;
            menu.PropertyChanged += (_, e) => { if (e.Property == Visual.IsVisibleProperty && menu.IsVisible) opens++; };
            void Send(int signal, bool systemMenuKey = false, PixelPoint? mouseAt = null)
            {
                var at = mouseAt ?? (signal is >= 0x200 and <= 0x20e ? simulatedPointer : point);
                var coords = systemMenuKey ? -1 : (at.X & 0xffff) | ((at.Y & 0xffff) << 16);
                SendMessageW(hwnd, WindowsTrayIcon.Callback, coords, (WindowsTrayIcon.IconId << 16) | signal);
            }
            async Task Wait(Func<bool> done, string reason)
            {
                var watch = Stopwatch.StartNew();
                while (!done() && watch.ElapsedMilliseconds < 3000) await Task.Delay(20);
                if (!done()) throw new InvalidOperationException(reason + $" (visible={menu.IsVisible}, opens={opens}, registered={tray.Registered}, icon={icon})");
            }
            async Task Close()
            {
                menu.CloseAnimated();
                await Wait(() => !menu.IsVisible, "The menu did not close");
            }
            void CheckClassroom() => Check(ReferenceEquals(menu, _trayMenu) && AppServices.Settings.IsMainWindowVisible == saved && overlay?.IsVisible == saved && _desktop?.Settings is null && _profileEditor is null,
                "The gesture left the classroom, settings and profile unchanged");
            void KeyFromFocus(Key key)
            {
                var control = menu.FocusManager?.GetFocusedElement() as InputElement ?? menu;
                control.RaiseEvent(new KeyEventArgs { Key = key, RoutedEvent = InputElement.KeyDownEvent });
                control.RaiseEvent(new KeyEventArgs { Key = key, RoutedEvent = InputElement.KeyUpEvent });
            }
            if (_oobeWindow is { } setup && overlay is null)
            {
                var labels = buttons.Select(b => b.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).LastOrDefault()).ToArray();
                Check(labels.SequenceEqual(["继续首次设置", "退出"]), "Setup exposes only resume and exit in the same menu");
                setup.WindowState = WindowState.Minimized;
                Send(0x201); Send(0x202); Send(0x400);
                await Wait(() => menu.IsVisible, "Setup short press did not open a menu");
                Check(opens == 1 && setup.WindowState == WindowState.Minimized, "Setup short press does not silently restore the wizard");
                await Close(); Send(0x201);
                await Wait(() => menu.IsVisible, "Setup long press did not open before release");
                Send(0x202); Send(0x400);
                Check(opens == 2 && setup.WindowState == WindowState.Minimized, "A setup long press and release opens the menu once");
                await Close(); Send(0x401);
                await Wait(() => buttons[0].IsKeyboardFocusWithin, "Setup menu could not be opened with the keyboard");
                KeyFromFocus(Key.Enter);
                await Wait(() => setup.IsVisible && setup.WindowState == WindowState.Normal && !menu.IsVisible, "Explicit Resume did not restore the setup wizard");
                Check(!AppServices.Settings.IsOobeCompleted && _desktop is null && _runtime is null && _mainWindow is null, "Resuming setup does not complete it or start classroom services");
                return;
            }
            Check(overlay != null && overlay.IsVisible == saved && AppServices.Settings.TrayClickBehavior == 3, "Legacy configuration keeps its overlay visibility but always opens the menu");
            var idleReads = pointerReads;
            Send(0x200); Send(0x200);
            Check(pointerReads == idleReads && !menu.IsVisible, "Idle shell mouse moves neither sample the cursor nor open the menu");
            Send(0x201); Check(!menu.IsVisible, "A short press waits for release");
            Send(0x202); Send(0x400);
            await Wait(() => menu.IsVisible, "Left button up did not open the real menu");
            Check(opens == 1 && buttons.Length >= 5, "Short press and shell select open one populated menu"); CheckClassroom();
            await Close();
            Send(0x201);
            await Wait(() => menu.IsVisible, "Long press did not open before release");
            Check(opens == 2, "Long press opens once while the button is still down"); CheckClassroom();
            await Close(); await Task.Delay(WindowsTrayIcon.HoldMilliseconds + 125); Send(0x202); Send(0x400);
            Check(!menu.IsVisible && opens == 2, "The long-press release cannot reopen even an already dismissed menu");
            Send(0x201); simulatedPointer = new(simulatedPointer.X + 120, simulatedPointer.Y);
            await Task.Delay(WindowsTrayIcon.HoldMilliseconds + 150);
            Send(0x202); Send(0x400);
            Check(!menu.IsVisible && opens == 2, "Dragging away cancels the hold, release and synthesized select without moving the system pointer");
            var start = simulatedPointer;
            Send(0x201); Send(0x200, mouseAt: new(start.X + 120, start.Y)); Send(0x200, mouseAt: start);
            await Task.Delay(WindowsTrayIcon.HoldMilliseconds + 120);
            Send(0x202); Send(0x400);
            Check(!menu.IsVisible && opens == 2, "A shell move beyond the drag threshold stays cancelled after returning to the original point");
            Send(0x204); Send(0x7b); Send(0x205);
            await Wait(() => menu.IsVisible, "Right click did not open");
            Check(opens == 3, "Right-click context and button-up reach the same menu once"); CheckClassroom();
            await Close(); Send(0x401);
            await Wait(() => buttons[0].IsKeyboardFocusWithin, "Keyboard activation did not focus the first real command");
            KeyFromFocus(Key.Down);
            Check(opens == 4 && buttons[1].IsKeyboardFocusWithin, "Keyboard activation opens the menu once and Down selects the next command");
            KeyFromFocus(Key.Escape); await Wait(() => !menu.IsVisible, "Escape did not dismiss the keyboard menu");
            Send(0x7b, true);
            await Wait(() => buttons[0].IsKeyboardFocusWithin && menu.IsVisible, "Keyboard context invocation did not focus the menu");
            Check(opens == 5, "The context-menu key can immediately reopen the dismissed menu");
            var root = menu.FindControl<Border>("MenuRoot")!;
            var topLeft = root.PointToScreen(default); var bottomRight = root.PointToScreen(new Point(root.Bounds.Width, root.Bounds.Height));
            Check(topLeft.X >= work.X - 1 && topLeft.Y >= work.Y - 1 && bottomRight.X <= work.Right + 1 && bottomRight.Y <= work.Bottom + 1,
                $"Visible menu fits the tray working area; shell icon={icon}, menu={topLeft}..{bottomRight}, work={work}");
            if (icon is { } real)
                Check(topLeft.X <= real.Right + 16 && bottomRight.X >= real.X - 16 &&
                    Math.Min(Math.Abs(bottomRight.Y - real.Y), Math.Abs(topLeft.Y - real.Bottom)) <= Math.Max(24, real.Height + 12), "The visible menu is next to the registered icon");
            Check(TrayMenuWindow.PositionAt(new(-72, -20, 24, 20), new(-1280, -1024, 1280, 1000), new(420, 690), 42, 6) == new PixelPoint(-378, -674),
                "Placement supports a 150% monitor with a negative desktop origin");
            KeyFromFocus(Key.Enter);
            await Wait(() => overlay!.IsVisible != saved && !menu.IsVisible, "The explicit visibility command did not toggle the classroom");
            Check(AppServices.Settings.IsMainWindowVisible == !saved && SettingsService.Load().IsMainWindowVisible == !saved, "Only the explicit menu command changes and saves visibility");
            Send(0x401);
            await Wait(() => menu.IsVisible && buttons[0].IsKeyboardFocusWithin, "The menu did not reopen for the layout command");
            var edit = buttons.Single(button => AutomationProperties.GetName(button) == "编辑主界面…");
            edit.Focus(NavigationMethod.Directional);
            KeyFromFocus(Key.Enter);
            await Wait(() => _desktop?.Settings is { CurrentPageKey: "widget-layout", AppWindow.IsVisible: true } && !menu.IsVisible, "Edit classroom did not open the actual component layout editor");
            Check(overlay!.IsVisible == !saved && AppServices.Settings.IsMainWindowVisible == !saved, "The explicit layout command reaches the editor without changing classroom visibility");

            menu.OpenAt(icon, false);
            await Wait(() => menu.IsVisible && root.Opacity >= .999, "Menu did not reach full opacity");
            var originalBounds = buttons.Select(b => b.Bounds).ToArray();
            if (DesktopMotion.IsEnabled)
            {
                var intermediate = false;
                root.PropertyChanged += ObserveFade;
                void ObserveFade(object? sender, AvaloniaPropertyChangedEventArgs e)
                {
                    if (e.Property == Visual.OpacityProperty && root.Opacity is > .002 and < .998) intermediate = true;
                }
                try
                {
                    menu.CloseAnimated();
                    await Wait(() => intermediate, "Menu closed without an intermediate fade sample");
                    menu.OpenAt(icon, false);
                    await Task.Delay(2 * DesktopMotion.Enter);
                    Check(menu.IsVisible && root.Opacity >= .999 && buttons.Select(b => b.Bounds).SequenceEqual(originalBounds),
                        "Reopening during fade-out keeps the same menu and row bounds; no stale callback hides it");
                    await Close();
                    Check(intermediate, "The live tray fades as one surface, with intermediate opacity before hiding");
                }
                finally { root.PropertyChanged -= ObserveFade; }
            }
            else
            {
                menu.CloseAnimated();
                Check(!menu.IsVisible, "Reduced motion closes the tray immediately");
                menu.OpenAt(icon, false);
                Check(menu.IsVisible && root.Opacity == 1 && root.Transitions == null, "Reduced motion opens a fully readable tray without a fade");
                await Close();
            }
        }
        catch (Exception ex) { failure = ex.ToString(); }
        finally
        {
            _trayIcon?.Dispose();
            await File.WriteAllTextAsync(Path.Combine(directory, "tray-review.json"), JsonSerializer.Serialize(new { Passed = failure == null, Checks = checks, Error = failure }, new JsonSerializerOptions { WriteIndented = true }));
            desktop.Shutdown(failure is null ? 0 : 1);
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)] private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
}
#endif
