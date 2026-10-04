using System.Runtime.InteropServices;

namespace ClassNex.Services;

/// <summary>
/// Windows 桌面浮层辅助：
///   - 点击穿透（WS_EX_TRANSPARENT）：鼠标点击直接落到后方的窗口/桌面
///   - 全局光标位置轮询：因为穿透后窗口收不到鼠标消息，悬停判断只能靠轮询
/// 非 Windows 平台下所有方法都安全空转。
/// </summary>
internal static class WindowsOverlay
{
    private const int GwlExStyle = -20;

    private const int WsExTransparent = 0x00000020; // 点击穿透
    private const int WsExLayered = 0x00080000;     // 分层窗口（与穿透配合）
    private const int WsExNoActivate = 0x08000000;  // 不抢焦点

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

    /// <summary>设置是否点击穿透（穿透后鼠标事件全部落向后方的窗口）。</summary>
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
            // 忽略：非 Windows 或句柄失效
        }
    }

    /// <summary>取全局光标位置（物理像素）。</summary>
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
