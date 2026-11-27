using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

internal static class PrivateWindowReview
{
    private const string Prefix = "ClassNEX-review-";

    internal static void RequirePrivateDesktop()
    {
        var desktop = GetThreadDesktop(GetCurrentThreadId());
        var name = new StringBuilder(128);
        if (desktop == 0 || !GetUserObjectInformationW(desktop, 2, name, name.Capacity * sizeof(char), out _) ||
            !name.ToString().StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name.ToString().AsSpan(Prefix.Length), "N", out _) ||
            !ReadDesktopInputState(desktop, 6, out var receivesInput, sizeof(int), out _) || receivesInput != 0)
            throw new InvalidOperationException("Native review requires a non-input desktop created by tools/run_review_offscreen.py.");
    }

    internal static object Capture(Window window, string path)
    {
        RequirePrivateDesktop();
        if (window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND", Handle: var hwnd } ||
            !IsWindowVisible(hwnd) || GetWindowThreadProcessId(hwnd, out var pid) == 0 || pid != Environment.ProcessId ||
            !GetClientRect(hwnd, out var bounds) || bounds.Right < 1 || bounds.Bottom < 1)
            throw new InvalidOperationException("No visible editor HWND owned by this review process.");

        var source = "PrintWindow/client";
        using var client = new System.Drawing.Bitmap(bounds.Right, bounds.Bottom, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        bool printed;
        using (var g = Graphics.FromImage(client))
        {
            var dc = g.GetHdc();
            try { printed = PrintWindow(hwnd, dc, 3); }
            finally { g.ReleaseHdc(dc); }
        }
        if (printed && HasContent(client)) client.Save(path, ImageFormat.Png);
        else
        {
            source = "Avalonia/real-window content (compositor not captured)";
            using var frame = new RenderTargetBitmap(PixelSize.FromSize(window.Bounds.Size, window.RenderScaling), new Vector(96, 96) * window.RenderScaling);
            frame.Render(window);
            frame.Save(path);
        }
        var dwm = DwmGetWindowAttribute(hwnd, 38, out var value, sizeof(int)) == 0 ? value.ToString() : "unavailable";
        Console.WriteLine($"FRAME {Path.GetFileName(path)} | {bounds.Right}x{bounds.Bottom} px | scale={window.RenderScaling} | theme={window.ActualThemeVariant} | Avalonia={window.ActualTransparencyLevel} | DWM backdrop={dwm} | {source}");
        return new { File = Path.GetFileName(path), Source = source, Width = bounds.Right, Height = bounds.Bottom,
            window.RenderScaling, Theme = window.ActualThemeVariant?.ToString(), Transparency = window.ActualTransparencyLevel.ToString(),
            Requested = string.Join(", ", window.TransparencyLevelHint), DwmBackdrop = dwm };
    }

    private static bool HasContent(System.Drawing.Bitmap image)
    {
        var colors = new HashSet<int>();
        for (var y = 12; y < image.Height - 12; y += 7)
        for (var x = 12; x < image.Width - 12; x += 7)
        {
            var c = image.GetPixel(x, y);
            colors.Add((c.R >> 3 << 10) | (c.G >> 3 << 5) | c.B >> 3);
            if (colors.Count >= 16) return true;
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    [DllImport("kernel32")] private static extern uint GetCurrentThreadId();
    [DllImport("user32")] private static extern nint GetThreadDesktop(uint threadId);
    [DllImport("user32", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformationW(nint desktop, int index, StringBuilder value, int size, out int required);
    [DllImport("user32", EntryPoint = "GetUserObjectInformationW", ExactSpelling = true)] private static extern bool ReadDesktopInputState(nint desktop, int index, out int value, int size, out int required);
    [DllImport("user32")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32")] private static extern bool GetClientRect(nint hwnd, out Rect bounds);
    [DllImport("user32")] private static extern bool PrintWindow(nint hwnd, nint dc, uint flags);
    [DllImport("dwmapi")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
}
