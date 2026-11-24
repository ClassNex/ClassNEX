using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassNex.Services;
using ClassNex.Styles;

namespace ClassNex.Views;

public partial class MainWindow
{
    private IPointer? _placementPointer;
    private IInputElement? _captureOwner;
    private PixelPoint _pressPoint;
    private PixelRect _pressIsland;
    private double _pressScaling;
    private PixelPoint? _dragAnchor;
    private Screen? _dragScreen;
    private bool _placementQueued, _suppressTap;
    private (PixelRect Island, int Radius, PixelRect? Toolbar, int ToolRadius)? _inputRegion;

    private void WirePlacement()
    {
        LinePanel.AddHandler(PointerPressedEvent, IslandPressed, RoutingStrategies.Bubble);
        LinePanel.AddHandler(PointerMovedEvent, IslandMoved, RoutingStrategies.Bubble, true);
        LinePanel.AddHandler(PointerReleasedEvent, IslandReleased, RoutingStrategies.Tunnel, true);
        LinePanel.AddHandler(PointerCaptureLostEvent, (_, e) =>
        {
            if (e.Pointer == _placementPointer) FinishIslandDrag(false);
        }, RoutingStrategies.Tunnel | RoutingStrategies.Direct, true);
        LinePanel.Tapped += (_, e) =>
        {
            if (!CanUseGesture || _suppressTap) return;
            switch (TapTarget(e.Source))
            {
                case "OverlayImportHint": App.OpenProfileEditor(0); break;
                case "OverlayWidgetsHint": App.OpenSettings("widget-layout"); break;
                case "NexPanel": App.OpenNexChat(); break;
                default: return;
            }
            e.Handled = true;
        };
        LinePanel.DoubleTapped += (_, e) =>
        {
            if (!CanUseGesture || _suppressTap || TapTarget(e.Source) != null || IsButton(e.Source)) return;
            App.OpenSettings();
            e.Handled = true;
        };
        WidgetViewport.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (!CanUseGesture || e.Delta.X != 0 || e.Delta.Y == 0 || WidgetViewport.Extent.Height > WidgetViewport.Viewport.Height + 1) return;
            var max = WidgetViewport.Extent.Width - WidgetViewport.Viewport.Width;
            if (max <= 1) return;
            WidgetViewport.Offset = new Vector(Math.Clamp(WidgetViewport.Offset.X - 60 * e.Delta.Y, 0, max), 0);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        SizeChanged += (_, _) => QueuePlacement();
        IslandBackground.SizeChanged += (_, _) => QueuePlacement();
        EditBarHost.SizeChanged += (_, _) => QueuePlacement();
    }

    private bool CanUseGesture => IsVisible && !_closed && !_editMode && !_showingNotification && !AppServices.Settings.IsClickThrough;
    private static bool IsButton(object? source) => source is Visual visual && visual.GetSelfAndVisualAncestors().Any(v => v is Button or Thumb);
    private static string? TapTarget(object? source) => (source as Visual)?.GetSelfAndVisualAncestors().OfType<Control>()
        .FirstOrDefault(x => x.Name is "OverlayImportHint" or "OverlayWidgetsHint" or "NexPanel")?.Name;

    private void IslandPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!CanUseGesture || !e.Pointer.IsPrimary || IsButton(e.Source) || _placementPointer != null ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || GetIslandScreenRect() is not { } bounds) return;
        if (e.Pointer.Type == PointerType.Touch && (WidgetViewport.Extent.Width > WidgetViewport.Viewport.Width + 1 || WidgetViewport.Extent.Height > WidgetViewport.Viewport.Height + 1)) return;
        _suppressTap = false;
        _pressPoint = this.PointToScreen(e.GetPosition(this));
        if (!bounds.Contains(_pressPoint)) return;
        _pressIsland = bounds;
        _pressScaling = RenderScaling;
        _placementPointer = e.Pointer;
        _captureOwner = e.Source as IInputElement ?? LinePanel;
        e.Pointer.Capture(_captureOwner);
    }

    private void IslandMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer != _placementPointer || !CanUseGesture) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { FinishIslandDrag(true); return; }
        var p = this.PointToScreen(e.GetPosition(this));
        var threshold = Math.Max(3, 4 * _pressScaling);
        if (_dragAnchor == null && Math.Abs(p.X - _pressPoint.X) < threshold && Math.Abs(p.Y - _pressPoint.Y) < threshold) return;
        if (ScreenFor(p) is not { } screen) return;
        if (_dragScreen != screen) { _dragScreen = screen; UpdatePlacementBudget(screen); }
        var ratio = DpiFor(screen) / _pressScaling;
        var size = new PixelSize((int)Math.Round(_pressIsland.Width * ratio), (int)Math.Round(_pressIsland.Height * ratio));
        var desired = new PixelPoint(p.X - (int)Math.Round((_pressPoint.X - _pressIsland.X) * ratio), p.Y - (int)Math.Round((_pressPoint.Y - _pressIsland.Y) * ratio));
        _dragAnchor = ClampIsland(screen.WorkingArea, size, desired);
        _suppressTap = true;
        EnforcePlacement();
        e.Handled = true;
    }

    private void IslandReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer != _placementPointer || e.InitialPressMouseButton != MouseButton.Left) return;
        if (_dragAnchor != null) e.Handled = true;
        FinishIslandDrag(true);
    }

    private void FinishIslandDrag(bool save)
    {
        if (_placementPointer is not { } pointer) return;
        var anchor = _dragAnchor;
        var owner = _captureOwner;
        _captureOwner = null;
        _placementPointer = null; _dragAnchor = null; _dragScreen = null;
        if (ReferenceEquals(pointer.Captured, owner)) pointer.Capture(null);
        if (anchor is not { } point) return;
        if (save && !_closed && IsVisible) SavePlacement(point);
        QueuePlacement();
    }

    internal void ResetPlacement()
    {
        FinishIslandDrag(false);
        SavePlacement(new PixelPoint(80, 80));
        QueuePlacement();
    }

    private static void SavePlacement(PixelPoint point)
    {
        var settings = AppServices.Settings;
        if (settings.ImportedDock == null && settings.MainWindowLeft == point.X && settings.MainWindowTop == point.Y) return;
        settings.ImportedDock = null;
        settings.MainWindowLeft = point.X;
        settings.MainWindowTop = point.Y;
        AppServices.SaveSettings();
    }

    private static PixelPoint? SavedAnchor()
    {
        var settings = AppServices.Settings;
        var x = settings.MainWindowLeft; var y = settings.MainWindowTop;
        return !double.IsFinite(x) || !double.IsFinite(y) || (x == 80 && y == 80) ? null :
            new((int)Math.Clamp(Math.Round(x), -1_000_000, 1_000_000), (int)Math.Clamp(Math.Round(y), -1_000_000, 1_000_000));
    }

    private Screen? ScreenFor(PixelPoint point) => Screens.ScreenFromPoint(point) ?? Screens.All.MinBy(s =>
    {
        var r = s.WorkingArea;
        var x = (long)point.X - Math.Clamp(point.X, r.X, r.Right);
        var y = (long)point.Y - Math.Clamp(point.Y, r.Y, r.Bottom);
        return x * x + y * y;
    });

    private Screen? PlacementScreen() => _dragScreen ?? (SavedAnchor() is { } p ? ScreenFor(p) :
        AppServices.Settings.ImportedDock is { MonitorIndex: >= 0 } dock && dock.MonitorIndex < Screens.All.Count ? Screens.All[dock.MonitorIndex] : Screens.Primary);
    private double DpiFor(Screen screen) => IsVisible && RenderScaling > 0 && Screens.ScreenFromWindow(this)?.Bounds == screen.Bounds ? RenderScaling : screen.Scaling;

    private void UpdatePlacementBudget(Screen? screen)
    {
        if (screen is null) return;
        var dpi = DpiFor(screen);
        var scale = Math.Clamp(ReminderScale, .4, 5);
        MaxWidth = Math.Max(1, screen.WorkingArea.Width / dpi);
        MaxHeight = Math.Max(1, screen.WorkingArea.Height / dpi);
        _availableIslandWidth = Math.Max(1, (MaxWidth - 32) / scale);
        CardContent.MaxWidth = Math.Max(1, _availableIslandWidth - 2 * IslandContentMargin);
        CardContent.MaxHeight = Math.Max(CiPalette.IslandHeight, (MaxHeight - 16) / scale - (_editMode ? 65 : 0));
        NexPanel.MaxWidth = Math.Max(1, Math.Min(820, CardContent.MaxWidth));
        NexPanel.MinWidth = Math.Min(240, NexPanel.MaxWidth);
    }

    private void QueuePlacement()
    {
        if (_closed || _placementQueued || !IsVisible) return;
        _placementQueued = true;
        Dispatcher.UIThread.Post(() => { _placementQueued = false; EnforcePlacement(); }, DispatcherPriority.Loaded);
    }

    internal static PixelPoint ClampIsland(PixelRect work, PixelSize island, PixelPoint? anchor) => new(
        anchor is { } p ? Math.Clamp(p.X, work.X, Math.Max(work.X, work.Right - island.Width)) : work.X + Math.Max(0, work.Width - island.Width) / 2,
        anchor is { } q ? Math.Clamp(q.Y, work.Y, Math.Max(work.Y, work.Bottom - island.Height)) : work.Y);

    private void EnforcePlacement()
    {
        if (!IsVisible || _closed || GetIslandScreenRect() is not { } island || PlacementScreen() is not { } screen) return;
        var area = screen.WorkingArea;
        var anchor = _dragAnchor ?? SavedAnchor();
        if (!anchor.HasValue && AppServices.Settings.ImportedDock is { } d)
        {
            var width = Math.Max(0, area.Width - island.Width);
            var x = area.X + ((d.Anchor % 3) switch { 0 => 0, 2 => width, _ => width / 2 });
            var y = d.Anchor >= 3 ? area.Y + Math.Max(0, area.Height - island.Height) : area.Y;
            anchor = new(x + Math.Clamp(d.OffsetX, -area.Width, area.Width), y + Math.Clamp(d.OffsetY, -area.Height, area.Height));
        }
        var target = ClampIsland(area, island.Size, anchor);
        var offset = island.Position - Position;
        var origin = new PixelPoint(target.X - offset.X, target.Y - offset.Y);
        if (Position != origin) Position = origin;
        UpdateInputRegion(new PixelRect(offset, island.Size));
    }

    private void UpdateInputRegion(PixelRect island)
    {
        if (_hwnd == 0) return;
        if (AppServices.Settings.IsClickThrough && !_editMode)
        {
            if (_inputRegion != null) { WindowsOverlay.SetInputRegion(_hwnd, null, 0, null, 0); _inputRegion = null; }
            return;
        }
        PixelRect? toolbar = null;
        if (_editMode && EditBarHost.IsVisible && EditBarHost.Bounds.Width > 0)
        {
            var start = EditBarHost.PointToScreen(default) - Position;
            var end = EditBarHost.PointToScreen(new Point(EditBarHost.Bounds.Width, EditBarHost.Bounds.Height)) - Position;
            toolbar = new PixelRect(start.X, start.Y, Math.Max(1, end.X - start.X), Math.Max(1, end.Y - start.Y));
        }
        var scale = Math.Clamp(ReminderScale, .4, 5) * RenderScaling;
        var radius = Math.Min(Math.Min(island.Width, island.Height) / 2, (int)Math.Round(IslandBackground.CornerRadius.TopLeft * scale));
        var region = (Island: island, Radius: radius, Toolbar: toolbar, ToolRadius: (int)Math.Round(12 * scale));
        if (_inputRegion == region) return;
        if (WindowsOverlay.SetInputRegion(_hwnd, region.Island, region.Radius, region.Toolbar, region.ToolRadius)) _inputRegion = region;
    }
}
