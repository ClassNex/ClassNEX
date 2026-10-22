using System.Runtime.InteropServices;

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

    private static IntPtr GetExStyle(IntPtr hWnd) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, GwlExStyle) : new IntPtr(GetWindowLong32(hWnd, GwlExStyle));

    private static void SetExStyle(IntPtr hWnd, IntPtr value)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(hWnd, GwlExStyle, value);
        else
            SetWindowLong32(hWnd, GwlExStyle, value.ToInt32());
    }

    
    public static void SetClickThrough(IntPtr hWnd, bool enabled)
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
