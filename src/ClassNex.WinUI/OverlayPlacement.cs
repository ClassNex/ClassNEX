using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace ClassNex.WinUI;

public sealed partial class NativeOverlayWindow
{
    private Pointer? _dragPointer;
    private UIElement? _dragOwner;
    private PointInt32 _pressAt;
    private RectInt32 _pressBounds;
    private uint _pressDpi;
    private bool _moving, _suppressTap;

    private void WirePlacementGestures()
    {
        _view.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(DragPressed), true);
        _view.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(DragMoved), true);
        _view.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, e) =>
        {
            if (_dragPointer?.PointerId != e.Pointer.PointerId) return;
            if (_moving) e.Handled = true;
            FinishDrag(true);
        }), true);
        _view.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler((_, e) =>
        {
            if (_dragPointer?.PointerId == e.Pointer.PointerId) FinishDrag(false);
        }), true);
        _view.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, e) =>
        {
            if (_dragPointer?.PointerId == e.Pointer.PointerId) FinishDrag(!e.GetCurrentPoint(_view).Properties.IsLeftButtonPressed);
        }), true);
        _scroller.PointerWheelChanged += (_, e) =>
        {
            if (_passthrough != false || _scroller.ScrollableWidth <= 1 || _scroller.ScrollableHeight > 1) return;
            var p = e.GetCurrentPoint(_scroller).Properties;
            if (p.IsHorizontalMouseWheel || p.MouseWheelDelta == 0) return;
            _scroller.ChangeView(Math.Clamp(_scroller.HorizontalOffset - p.MouseWheelDelta / 2d, 0, _scroller.ScrollableWidth), null, null, true);
            e.Handled = true;
        };
    }

    private void DragPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_visible || !_haveBounds || _passthrough != false || _surface is not (Surface.Widgets or Surface.Nex)) return;
        var point = e.GetCurrentPoint(_view);
        if (!point.Properties.IsPrimary || !point.Properties.IsLeftButtonPressed || _moving) return;
        FinishDrag(false);
        _suppressTap = false;
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch && (_scroller.ScrollableWidth > 1 || _scroller.ScrollableHeight > 1))
        {
            for (var hit = e.OriginalSource as UIElement; hit is not null; hit = VisualTreeHelper.GetParent(hit) as UIElement)
                if (hit == _scroller) return;
        }
        _pressAt = ScreenPoint(e);
        _pressBounds = _bounds;
        _pressDpi = _lastMetrics.Dpi == 0 ? 96u : _lastMetrics.Dpi;
        var owner = e.OriginalSource as UIElement ?? _view;
        _dragOwner = owner.CapturePointer(e.Pointer) ? owner : null;
        _dragPointer = e.Pointer;
    }

    private PointInt32 ScreenPoint(PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && OverlayChrome.TryCursor(out var cursor)) return cursor;
        var point = e.GetCurrentPoint(_view).Position;
        var dpi = _view.XamlRoot?.RasterizationScale ?? 1;
        return new PointInt32(_bounds.X + (int)Math.Round(point.X * dpi), _bounds.Y + (int)Math.Round(point.Y * dpi));
    }

    private void DragMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointer?.PointerId != e.Pointer.PointerId || !_visible || _passthrough != false) return;
        if (!e.GetCurrentPoint(_view).Properties.IsLeftButtonPressed) { FinishDrag(true); return; }
        var p = ScreenPoint(e);
        var threshold = Math.Max(3, 4 * _pressDpi / 96d);
        if (!_moving && Math.Abs(p.X - _pressAt.X) < threshold && Math.Abs(p.Y - _pressAt.Y) < threshold) return;
        _moving = _suppressTap = true;
        var work = OverlayChrome.Environment(_hwnd, p.X, p.Y);
        var ratio = work.Dpi / (double)_pressDpi;
        var w = Math.Clamp((int)Math.Round(_pressBounds.Width * ratio), 1, work.Width);
        var h = Math.Clamp((int)Math.Round(_pressBounds.Height * ratio), 1, work.Height);
        var x = Math.Clamp(p.X - (int)Math.Round((_pressAt.X - _pressBounds.X) * ratio), work.Left, work.Left + work.Width - w);
        var y = Math.Clamp(p.Y - (int)Math.Round((_pressAt.Y - _pressBounds.Y) * ratio), work.Top, work.Top + work.Height - h);
        if (x != _bounds.X || y != _bounds.Y || w != _bounds.Width || h != _bounds.Height)
        {
            var rect = new RectInt32(x, y, w, h);
            OverlayChrome.Place(_hwnd, rect);
            if (w != _bounds.Width || h != _bounds.Height)
            {
                _regionRadius = Math.Max(1, (int)Math.Round(14 * _scale * work.Dpi / 96d));
                OverlayChrome.Round(_hwnd, w, h, _regionRadius);
            }
            _bounds = rect;
        }
        e.Handled = true;
    }

    private void FinishDrag(bool save)
    {
        var pointer = _dragPointer;
        if (pointer is null) return;
        var owner = _dragOwner; var moved = _moving;
        _dragPointer = null; _dragOwner = null; _moving = false;
        owner?.ReleasePointerCapture(pointer);
        if (!moved) return;
        if (save && _visible) SavePlacement(_bounds.X, _bounds.Y);
        _layoutDirty = true;
        if (_visible) RefreshCore(true);
    }

    private void SavePlacement(double left, double top)
    {
        var settings = _bridge.ReadSettings();
        if (settings.MainWindowLeft == left && settings.MainWindowTop == top) return;
        settings.MainWindowLeft = left; settings.MainWindowTop = top;
        _bridge.SaveSettings();
    }

    private void ResetPlacement()
    {
        FinishDrag(false);
        SavePlacement(80, 80);
        _layoutDirty = true;
        RefreshCore(true);
    }

    private void GestureCommand(string command)
    {
        if (_visible && _passthrough == false && !_suppressTap) _bridge.Command(command);
    }
}
