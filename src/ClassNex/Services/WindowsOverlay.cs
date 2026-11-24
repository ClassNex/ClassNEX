using System.Runtime.InteropServices;
using System.Text;
using Avalonia;

namespace ClassNex.Services;







internal static class WindowsOverlay
{
    private const int GwlExStyle = -20;

    private const int WsExTransparent = 0x00000020; 
    private const int WsExToolWindow = 0x00000080;  
    private const int WsExLayered = 0x00080000;     
    private const int WsExNoActivate = 0x08000000;  

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetClassNameW(nint window, StringBuilder name, int length);

    internal static bool ForegroundCovers(PixelRect screen)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var foreground = GetForegroundWindow();
        if (foreground == 0 || !GetWindowRect(foreground, out var rect) || rect.Left > screen.X || rect.Top > screen.Y || rect.Right < screen.Right || rect.Bottom < screen.Bottom) return false;
        var name = new StringBuilder(256);
        return GetClassNameW(foreground, name, name.Capacity) > 0 && name.ToString() is not ("WorkerW" or "Progman");
    }

    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint result, nint first, nint second, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, bool redraw);

    internal static bool SetInputRegion(nint window, PixelRect? island, int radius, PixelRect? toolbar, int toolRadius)
    {
        if (!OperatingSystem.IsWindows() || window == 0) return false;
        if (island is not { } body) return SetWindowRgn(window, 0, true) != 0;
        static nint Round(PixelRect r, int radius) => CreateRoundRectRgn(r.X, r.Y, r.Right + 1, r.Bottom + 1, 2 * radius, 2 * radius);
        var region = Round(body, radius);
        if (region == 0) return false;
        if (toolbar is { } edit)
        {
            var extra = Round(edit, toolRadius);
            if (extra == 0) { DeleteObject(region); return false; }
            var combined = CombineRgn(region, region, extra, 2);
            DeleteObject(extra);
            if (combined == 0) { DeleteObject(region); return false; }
        }
        if (SetWindowRgn(window, region, true) != 0) return true;
        DeleteObject(region);
        return false;
    }

    private static IntPtr GetExStyle(IntPtr hWnd) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, GwlExStyle) : new IntPtr(GetWindowLong32(hWnd, GwlExStyle));

    private static void SetExStyle(IntPtr hWnd, IntPtr value)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(hWnd, GwlExStyle, value);
        else
            SetWindowLong32(hWnd, GwlExStyle, value.ToInt32());
    }

    
    public static void SetClickThrough(IntPtr hWnd, bool enabled, bool keepNonActivating = false)
    {
        if (!OperatingSystem.IsWindows() || hWnd == IntPtr.Zero)
            return;

        try
        {
            var style = GetExStyle(hWnd).ToInt64();

            if (enabled)
                style |= WsExTransparent | WsExLayered | WsExNoActivate;
            else
                style &= ~(long)(WsExTransparent | WsExNoActivate);
            if (keepNonActivating) style |= WsExNoActivate;

            SetExStyle(hWnd, new IntPtr(style));
        }
        catch
        {
            
        }
    }

    
    
    
    
    
    public static void SetToolWindow(IntPtr hWnd, bool enabled)
    {
        if (!OperatingSystem.IsWindows() || hWnd == IntPtr.Zero)
            return;

        try
        {
            var style = GetExStyle(hWnd).ToInt64();

            if (enabled)
                style |= WsExToolWindow;
            else
                style &= ~(long)WsExToolWindow;

            SetExStyle(hWnd, new IntPtr(style));
        }
        catch
        {
            
        }
    }

    
    public static (int X, int Y)? GetCursorPosition()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            return GetCursorPos(out var p) ? (p.X, p.Y) : null;
        }
        catch
        {
            return null;
        }
    }
}
