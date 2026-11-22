using System.Runtime.InteropServices;
using Windows.Graphics;

namespace ClassNex.WinUI;

internal static class OverlayChrome
{
    internal readonly record struct Metrics(int Left, int Top, int Width, int Height, uint Dpi, bool Auto, int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Monitor { public uint Size; public Rect Bounds, Work; public uint Flags; }

    internal static Metrics Environment(nint hwnd, double left, double top)
    {
        var auto = !double.IsFinite(left) || !double.IsFinite(top) || left == 80 && top == 80;
        var x = auto ? 0 : (int)Math.Clamp(Math.Round(left), -1000000, 1000000);
        var y = auto ? 0 : (int)Math.Clamp(Math.Round(top), -1000000, 1000000);
        var monitor = MonitorFromPoint(new Point { X = x, Y = y }, auto ? 1u : 2u);
        var info = new Monitor { Size = (uint)Marshal.SizeOf<Monitor>() };
        var work = GetMonitorInfoW(monitor, ref info) ? info.Work : new Rect { Right = Math.Max(1, GetSystemMetrics(0)), Bottom = Math.Max(1, GetSystemMetrics(1)) };
        var dpi = GetDpiForWindow(hwnd);
        if (MonitorFromWindow(hwnd, 2) != monitor && GetDpiForMonitor(monitor, 0, out var targetDpi, out _) >= 0) dpi = targetDpi;
        return new Metrics(work.Left, work.Top, Math.Max(1, work.Right - work.Left), Math.Max(1, work.Bottom - work.Top), dpi == 0 ? 96u : dpi, auto, x, y);
    }

    internal static bool ContainsPointer(RectInt32 rect) => GetCursorPos(out var p) &&
        p.X >= rect.X && p.X < rect.X + rect.Width && p.Y >= rect.Y && p.Y < rect.Y + rect.Height;

    internal static bool TryCursor(out PointInt32 location)
    {
        var found = GetCursorPos(out var point);
        location = new PointInt32(point.X, point.Y);
        return found;
    }

    internal static void SetPassthrough(nint hwnd, bool enabled)
    {
        const int toolWindow = 0x80, noActivate = 0x08000000, appWindow = 0x40000, transparent = 0x20, layered = 0x80000;
        var oldStyle = IntPtr.Size == 8 ? GetWindowLongPtrW(hwnd, -20).ToInt64() : GetWindowLongW(hwnd, -20);
        var style = (oldStyle | toolWindow | noActivate) & ~(long)appWindow;
        style = enabled ? style | transparent | layered : style & ~(long)(transparent | layered);
        if (style != oldStyle)
        {
            if (IntPtr.Size == 8) SetWindowLongPtrW(hwnd, -20, (nint)style);
            else SetWindowLongW(hwnd, -20, (int)style);
            SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x37);
        }
        if (enabled) SetOpacity(hwnd, 1);
        int noBorder = -2;
        DwmSetWindowAttribute(hwnd, 34, ref noBorder, sizeof(int));
    }

    internal static void SetOpacity(nint hwnd, double opacity) =>
        SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255), 2);

    internal static void Place(nint hwnd, RectInt32 rect) => SetWindowPos(hwnd, 0, rect.X, rect.Y, rect.Width, rect.Height, 0x214);

    internal static void Round(nint hwnd, int width, int height, int radius)
    {
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        if (region != 0 && SetWindowRgn(hwnd, region, true) == 0) DeleteObject(region);
    }

    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern bool GetMonitorInfoW(nint monitor, ref Monitor info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern int GetWindowLongW(nint hwnd, int index);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern int SetWindowLongW(nint hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
}
