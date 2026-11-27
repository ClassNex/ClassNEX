#if DEBUG
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;

namespace ClassNex;

internal sealed class DesktopPointerTarget : IDisposable
{
    private readonly nint _classroom, _underlay, _button, _desktopWindow;
    private readonly uint _uiThread;
    private bool _directHitCalibrated;
    internal bool UsesWindowFromPoint => _directHitCalibrated;

    internal DesktopPointerTarget(nint classroom, PixelRect area)
    {
        _uiThread = GetWindowThreadProcessId(classroom, out var pid);
        if (_uiThread == 0 || _uiThread != GetCurrentThreadId() || pid != Environment.ProcessId)
            throw new InvalidOperationException("Not the review process's classroom window.");
        RequirePrivateDesktop(_uiThread);
        _classroom = classroom;
        _desktopWindow = GetDesktopWindow();
        _underlay = CreateWindowExW(0x08000080, "STATIC", "ClassNEX · 验收底窗", 0x90000000, area.X, area.Y, area.Width, area.Height, 0, 0, 0, 0);
        if (_underlay == 0) throw new InvalidOperationException("Could not create the private backing window.");
        try
        {
            _button = CreateWindowExW(0, "BUTTON", "验收按钮", 0x50000003, 0, 0, area.Width, area.Height, _underlay, 1, 0, 0);
            if (_button == 0 || !SetWindowPos(_underlay, _classroom, 0, 0, 0, 0, 0x13))
                throw new InvalidOperationException("Could not place the native button behind the real classroom window.");
        }
        catch { DestroyWindow(_underlay); throw; }
    }

    internal void Calibrate(PixelPoint point)
    {
        CheckTop(point, through: false);
        var direct = WindowFromPoint(new(point.X, point.Y));
        _directHitCalibrated = IsClassroom(direct);
        if (!_directHitCalibrated && direct != _button && direct != _underlay && direct != 0)
            throw new InvalidOperationException("Another window covered the isolated hit-test point.");
    }

    internal void Click(PixelPoint point, bool through)
    {
        RequirePrivateDesktop(_uiThread);
        var hit = CheckTop(point, through);
        if (_directHitCalibrated)
        {
            var direct = WindowFromPoint(new(point.X, point.Y));
            if (through ? direct != _button : !IsClassroom(direct))
                throw new InvalidOperationException($"Native WindowFromPoint disagrees with desktop hit testing at {point}: direct={direct:X}, native route={hit:X}, classroom={_classroom:X}, button={_button:X}.");
            hit = direct;
        }
        if (GetWindowThreadProcessId(hit, out var pid) != _uiThread || pid != Environment.ProcessId || hit != _button && !IsClassroom(hit))
            throw new InvalidOperationException("Will not deliver a message to an unowned native window.");
        var local = ToClient(hit, point);
        nint packed = (local.X & 0xffff) | (local.Y << 16);
        var before = SendMessageW(_button, 0x00F0, 0, 0); // BM_GETCHECK on a native BS_AUTOCHECKBOX.
        SendMessageW(hit, 0x0201, 1, packed);
        SendMessageW(hit, 0x0202, 0, packed);
        var after = SendMessageW(_button, 0x00F0, 0, 0);
        if ((before != after) != through)
            throw new InvalidOperationException($"The button behind the classroom {((before == after) ? "did not change" : "changed")} at {point}, pass-through={through}.");
    }

    private nint CheckTop(PixelPoint point, bool through)
    {
        var inClassroom = Covers(_classroom, point, out var classroomRect);
        var inUnderlay = Covers(_underlay, point, out var underlayRect);
        var zOrder = new HashSet<nint>();
        for (var next = GetWindow(_classroom, 2); next != 0 && zOrder.Add(next) && next != _underlay; next = GetWindow(next, 2)) { }
        if (!inClassroom || !inUnderlay || !zOrder.Contains(_underlay))
            throw new InvalidOperationException($"The two visible HWNDs did not overlap in the required Z-order at {point}: classroom={classroomRect} contains={inClassroom}, underlay={underlayRect} contains={inUnderlay}, underlay is below classroom={zOrder.Contains(_underlay)}.");
        var p = ToClient(_desktopWindow, point);
        var front = ChildWindowFromPointEx(_desktopWindow, p, 3);
        var eligible = ChildWindowFromPointEx(_desktopWindow, p, 7);
        // Some layered surfaces are skipped even without CWP_SKIPTRANSPARENT. Z-order and both window regions above are the occlusion control.
        if (front != _classroom && !(through && front == _underlay) || (through ? eligible != _underlay : eligible != _classroom))
            throw new InvalidOperationException($"Native overlap/hit mismatch at {point}: front={front:X}, eligible={eligible:X}, classroom={_classroom:X}, underlay={_underlay:X}; pass-through={through}.");
        if (!through) return _classroom;
        var child = ChildWindowFromPointEx(eligible, ToClient(eligible, point), 7);
        return child == _button ? child : throw new InvalidOperationException("The point in the backing window did not hit its real button.");
    }

    private bool IsClassroom(nint hit) => hit == _classroom || hit != 0 && IsChild(_classroom, hit);
    public void Dispose() => DestroyWindow(_underlay);

    private static bool Covers(nint window, PixelPoint point, out PixelRect bounds)
    {
        bounds = default;
        if (!IsWindowVisible(window) || !GetWindowRect(window, out var r)) return false;
        bounds = new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        if (!bounds.ContainsExclusive(point)) return false;
        var region = CreateRectRgn(0, 0, 0, 0);
        if (region == 0) throw new InvalidOperationException("Could not query the native window region.");
        try { return GetWindowRgn(window, region) == 0 || PtInRegion(region, point.X - r.Left, point.Y - r.Top); }
        finally { DeleteObject(region); }
    }

    private static NativePoint ToClient(nint window, PixelPoint point)
    {
        var p = new NativePoint(point.X, point.Y);
        if (!ScreenToClient(window, ref p)) throw new InvalidOperationException("No client coordinates for the review HWND.");
        return p;
    }

    private static void RequirePrivateDesktop(uint thread)
    {
        const string prefix = "ClassNEX-review-";
        var desktop = GetThreadDesktop(thread);
        var name = new StringBuilder(128);
        if (desktop == 0 || !GetUserObjectInformationW(desktop, 2, name, name.Capacity * sizeof(char), out _) ||
            !name.ToString().StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name.ToString().AsSpan(prefix.Length), "N", out _) ||
            !ReadInputState(desktop, 6, out var receivesInput, sizeof(int), out _) || receivesInput != 0)
            throw new InvalidOperationException("Desktop pointer review requires a non-input desktop created by tools/run_review_offscreen.py.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y) { public int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("kernel32")] private static extern uint GetCurrentThreadId();
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32")] private static extern nint GetThreadDesktop(uint thread);
    [DllImport("user32", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformationW(nint desktop, int index, StringBuilder value, int size, out int needed);
    [DllImport("user32", EntryPoint = "GetUserObjectInformationW", ExactSpelling = true)] private static extern bool ReadInputState(nint desktop, int index, out int value, int size, out int needed);
    [DllImport("user32", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint ex, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32")] private static extern nint GetDesktopWindow();
    [DllImport("user32")] private static extern nint GetWindow(nint window, uint direction);
    [DllImport("user32")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32")] private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("gdi32")] private static extern nint CreateRectRgn(int x, int y, int right, int bottom);
    [DllImport("gdi32")] private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32")] private static extern bool DeleteObject(nint handle);
    [DllImport("user32")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32")] private static extern nint ChildWindowFromPointEx(nint parent, NativePoint point, uint flags);
    [DllImport("user32")] private static extern bool IsChild(nint parent, nint child);
    [DllImport("user32")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32")] private static extern bool ScreenToClient(nint window, ref NativePoint point);
    [DllImport("user32")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32", ExactSpelling = true)] private static extern nint SendMessageW(nint window, uint msg, nint wp, nint lp);
}
#endif
