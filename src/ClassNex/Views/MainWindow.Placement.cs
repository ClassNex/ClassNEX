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
    private bool _placementQueued;
    private bool _readingOverflow;
    private (PixelRect Island, int Radius, PixelRect? Toolbar, int ToolRadius)? _inputRegion;

    private static double ClassroomHoverOpacity => double.IsFinite(AppServices.Settings.HoverOpacity)
        ? Math.Clamp(AppServices.Settings.HoverOpacity, 0, 1) : 1;

    private bool FadesOnHover => !_editMode && !_readingOverflow && !_nexPanelActive && ClassroomHoverOpacity < 1;

    private bool PassesPointerThrough => !LinePanel.IsHitTestVisible ||
        (!_editMode && !_readingOverflow && (AppServices.Settings.IsClickThrough || FadesOnHover));

    private void WirePlacement()
    {
        MaskTitleViewport.ScrollChanged += OnReminderScrollChanged;
        NotificationBodyViewport.ScrollChanged += OnReminderScrollChanged;
        LinePanel.Tapped += (_, e) =>
        {
            if (!CanUseGesture) return;
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
            if (!CanUseGesture || TapTarget(e.Source) != null || IsButton(e.Source)) return;
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
        IslandBackground.SizeChanged += (_, _) => { _lastHoverPoint = null; QueuePlacement(); };
        EditBarHost.SizeChanged += (_, _) => QueuePlacement();
    }

    private bool CanUseGesture => IsVisible && !_closed && !_editMode && !_showingNotification && !PassesPointerThrough;
    private bool CanPlaceIsland => IsVisible && !_closed && _editMode && !_showingNotification && !_nexPanelActive;

    private void OnReminderScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0) RefreshReminderInput();
    }

    private void RefreshReminderInput()
    {
        ScrollViewer? view = null;
        if (IsVisible && !_closed && !_editMode && !_nexPanelActive && _showingNotification && !_overlayExiting)
        {
            if (NotificationMask.IsVisible)
                view = NotificationMask.IsHitTestVisible ? MaskTitleViewport : null;
            else if (NotificationOverlay.IsVisible && NotificationOverlay.IsHitTestVisible)
                view = NotificationBodyViewport;
        }

        var reading = view != null && view.Viewport.Height > 0 && view.Extent.Height - view.Viewport.Height > .5;
        if (reading == _readingOverflow) return;
        _readingOverflow = reading;
        if (_closed) return;
        ApplyMotionPreference();
        UpdateRunningState();
    }

    private static bool IsButton(object? source) => source is Visual visual && visual.GetSelfAndVisualAncestors().Any(v => v is Button or Thumb);
    private static string? TapTarget(object? source) => (source as Visual)?.GetSelfAndVisualAncestors().OfType<Control>()
        .FirstOrDefault(x => x.Name is "OverlayImportHint" or "OverlayWidgetsHint" or "NexPanel")?.Name;

    private void IslandPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!CanPlaceIsland || sender is not Control handle || !e.Pointer.IsPrimary || _placementPointer != null ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || GetIslandScreenRect() is not { } bounds) return;
        _pressPoint = this.PointToScreen(e.GetPosition(this));
        _pressIsland = bounds;
        _pressScaling = RenderScaling;
        _placementPointer = e.Pointer;
        _captureOwner = handle;
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private void IslandMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer != _placementPointer || !CanPlaceIsland) return;
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
        EnforcePlacement();
        e.Handled = true;
    }

    private void IslandReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer != _placementPointer || (e.Pointer.Type == PointerType.Mouse && e.InitialPressMouseButton != MouseButton.Left)) return;
        e.Handled = true;
        FinishIslandDrag(true);
    }

    private void FinishIslandDrag(bool save)
    {
        if (_placementPointer is not { } pointer) return;
        var moved = _dragAnchor.HasValue;
        var position = save && moved && IsVisible && !_closed ? GetIslandScreenRect()?.Position : null;
        var owner = _captureOwner;
        _captureOwner = null;
        _placementPointer = null; _dragAnchor = null; _dragScreen = null;
        if (ReferenceEquals(pointer.Captured, owner)) pointer.Capture(null);
        if (position is { } point) SavePlacement(point);
        if (moved) QueuePlacement();
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

    private PixelPoint ReminderAnchor(PixelRect area, PixelPoint saved, PixelRect reminder)
    {
        var size = ClassroomReturnSize;
        var end = IslandBackground.PointToScreen(new Point(size.Width, size.Height));
        var classroom = new PixelSize(Math.Max(1, end.X - reminder.X), Math.Max(1, end.Y - reminder.Y));
        var resting = ClampIsland(area, classroom, saved);
        var x = resting.X == area.X ? area.X :
            resting.X + classroom.Width == area.Right ? area.Right - reminder.Width :
            resting.X + (classroom.Width - reminder.Width) / 2;
        var y = resting.Y + classroom.Height == area.Bottom ? area.Bottom - reminder.Height : resting.Y;
        return new PixelPoint(x, y);
    }

    private void EnforcePlacement()
    {
        if (!IsVisible || _closed || GetIslandScreenRect() is not { } island || PlacementScreen() is not { } screen) return;
        var area = screen.WorkingArea;
        var anchor = _dragAnchor ?? SavedAnchor();
        if (_dragAnchor is null && _showingNotification && anchor is { } saved)
            anchor = ReminderAnchor(area, saved, island);
        else if (!anchor.HasValue && AppServices.Settings.ImportedDock is { } d)
        {
            var width = Math.Max(0, area.Width - island.Width);
            var x = area.X + ((d.Anchor % 3) switch { 0 => 0, 2 => width, _ => width / 2 });
            var y = d.Anchor >= 3 ? area.Y + Math.Max(0, area.Height - island.Height) : area.Y;
            anchor = new(x + Math.Clamp(d.OffsetX, -area.Width, area.Width), y + Math.Clamp(d.OffsetY, -area.Height, area.Height));
        }
        var target = ClampIsland(area, island.Size, anchor);
        if (GetEditToolbarScreenRect() is { } toolbar)
        {
            var editor = island.Union(toolbar);
            var islandOffset = island.Position - editor.Position;
            target = ClampIsland(area, editor.Size, target - islandOffset) + islandOffset;
        }
        var offset = island.Position - Position;
        var origin = new PixelPoint(target.X - offset.X, target.Y - offset.Y);
        if (Position != origin) { Position = origin; _lastHoverPoint = null; }
        UpdateInputRegion(new PixelRect(offset, island.Size));
    }

    private PixelRect? GetEditToolbarScreenRect()
    {
        if (!_editMode || !EditBarHost.IsVisible || EditBarHost.Bounds.Width <= 0 || EditBarHost.Bounds.Height <= 0) return null;
        var start = EditBarHost.PointToScreen(default);
        var end = EditBarHost.PointToScreen(new Point(EditBarHost.Bounds.Width, EditBarHost.Bounds.Height));
        return new PixelRect(start.X, start.Y, Math.Max(1, end.X - start.X), Math.Max(1, end.Y - start.Y));
    }

    private void UpdateInputRegion(PixelRect island)
    {
        if (_hwnd == 0 || PassesPointerThrough) return;
        PixelRect? toolbar = GetEditToolbarScreenRect() is { } bar ? new PixelRect(bar.Position - Position, bar.Size) : null;
        var scale = Math.Clamp(ReminderScale, .4, 5) * RenderScaling;
        var radius = Math.Min(Math.Min(island.Width, island.Height) / 2, (int)Math.Round(IslandBackground.CornerRadius.TopLeft * scale));
        var region = (Island: island, Radius: radius, Toolbar: toolbar, ToolRadius: (int)Math.Round(12 * scale));
        if (_inputRegion == region) return;
        if (WindowsOverlay.SetInputRegion(_hwnd, region.Island, region.Radius, region.Toolbar, region.ToolRadius)) _inputRegion = region;
    }
}
