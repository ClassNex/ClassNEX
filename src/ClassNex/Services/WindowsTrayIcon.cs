using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ClassNex.Services;

internal sealed class WindowsTrayIcon : IDisposable
{
    internal const uint Callback = 0x8000 + 0x391;
    internal const int IconId = 1, HoldMilliseconds = 500;
    internal const int FirstRegistrationRetry = 500;
    internal delegate (bool Registered, bool Version4) RegisterIcon(nint window, nint image);
    private const uint Add = 0, Delete = 2, SetFocus = 3, SetVersion = 4;
    private static readonly uint TaskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    private readonly Window _owner;
    private readonly nint _handle, _icon;
    private readonly Action<PixelRect?, bool> _show;
    private readonly Func<PixelPoint?> _cursor;
    private readonly RegisterIcon _register;
    private readonly DispatcherTimer _hold = new() { Interval = TimeSpan.FromMilliseconds(HoldMilliseconds) };
    private readonly DispatcherTimer _registrationRetry = new();
    private bool _version4, _disposed, _delivered, _dragged;
    private int _button, _dragWidth, _dragHeight, _nextRetry = FirstRegistrationRetry;
    private long _lastOpen = -1000;
    private PixelRect? _pressAnchor;
    private PixelPoint? _pressCursor;
    internal bool Registered { get; private set; }
    internal bool RegistrationRetryPending => _registrationRetry.IsEnabled;

    internal WindowsTrayIcon(Window owner, Action<PixelRect?, bool> show, Func<PixelPoint?>? cursor = null, RegisterIcon? register = null)
    {
        _owner = owner;
        _show = show;
        _cursor = cursor ?? CursorPosition;
        _register = register ?? RegisterWithShell;
        _handle = owner.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("托盘图标没有关联的窗口");
        _icon = LoadImageW(0, Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico"), 1, GetSystemMetrics(49), GetSystemMetrics(50), 0x10);
        _hold.Tick += OnHold;
        _registrationRetry.Tick += OnRegistrationRetry;
        Win32Properties.AddWndProcHookCallback(owner, OnMessage);
        owner.Closed += OwnerClosed;
        Register();
    }

    private static NotifyData Data(nint window) => new() { Size = (uint)Marshal.SizeOf<NotifyData>(), Window = window, Id = IconId };

    internal static (bool Registered, bool Version4) RegisterWithShell(nint window, nint image)
    {
        var data = Data(window);
        data.Flags = 0x87; // NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP
        data.Message = Callback;
        data.Icon = image != 0 ? image : LoadIconW(0, 32512);
        data.Tip = "ClassNEX 课表";
        var registered = Shell_NotifyIconW(Add, ref data) || Shell_NotifyIconW(1, ref data);
        data.Version = 4;
        return (registered, registered && Shell_NotifyIconW(SetVersion, ref data));
    }

    private void Register()
    {
        _registrationRetry.Stop();
        if (_disposed) return;
        (Registered, _version4) = _register(_handle, _icon);
        if (Registered) { _nextRetry = FirstRegistrationRetry; return; }
        _registrationRetry.Interval = TimeSpan.FromMilliseconds(_nextRetry);
        _nextRetry = Math.Min(_nextRetry * 2, 30000);
        _registrationRetry.Start();
    }

    private void OnRegistrationRetry(object? sender, EventArgs e)
    {
        _registrationRetry.Stop();
        if (!_disposed && !Registered) Register();
    }

    internal PixelRect? IconBounds()
    {
        var id = new IconIdentifier { Size = (uint)Marshal.SizeOf<IconIdentifier>(), Window = _handle, Id = IconId };
        return Shell_NotifyIconGetRect(ref id, out var rect) >= 0 && rect.Right > rect.Left && rect.Bottom > rect.Top
            ? new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top) : null;
    }

    internal void RestoreKeyboardFocus()
    {
        if (!Registered) return;
        var data = Data(_handle);
        Shell_NotifyIconW(SetFocus, ref data);
    }

    private void OnHold(object? sender, EventArgs args)
    {
        _hold.Stop();
        if (_button == 1 && !_disposed) OpenOnce(_pressAnchor, false);
    }

    private void OpenOnce(PixelRect? anchor, bool keyboard, PixelPoint? reported = null)
    {
        if (_button != 0)
        {
            ObserveDrag(reported);
            if (_delivered || _dragged) return;
            _delivered = true;
        }
        else if (!keyboard && Environment.TickCount64 - _lastOpen <= 500) return;
        _lastOpen = Environment.TickCount64;
        _hold.Stop();
        _show(anchor, keyboard);
    }

    private void ObserveDrag(PixelPoint? reported)
    {
        if (_button != 1 || _delivered || _dragged || _pressCursor is not { } start || (reported ?? _cursor()) is not { } current) return;
        if (Math.Abs((long)current.X - start.X) < _dragWidth && Math.Abs((long)current.Y - start.Y) < _dragHeight) return;
        _dragged = true;
        _hold.Stop();
    }

    private void Down(int button, PixelRect? anchor, PixelPoint? reported)
    {
        _button = button;
        _delivered = _dragged = false;
        _pressCursor = button == 1 ? reported ?? _cursor() : null;
        _pressAnchor = anchor;
        _hold.Stop();
        if (button == 1)
        {
            _dragWidth = Math.Max(2, GetSystemMetrics(68));
            _dragHeight = Math.Max(2, GetSystemMetrics(69));
            _hold.Start();
        }
    }

    private void Up(int button, PixelRect? anchor, PixelPoint? reported)
    {
        if (_button != 0 && _button != button) return;
        OpenOnce(_pressAnchor ?? anchor, false, reported);
        _lastOpen = Environment.TickCount64;
        _button = 0;
        _pressAnchor = null;
        _hold.Stop();
    }

    private nint OnMessage(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (_disposed) return 0;
        if (message == TaskbarCreated && message != 0)
        {
            _hold.Stop(); _button = 0; _pressAnchor = null; _nextRetry = FirstRegistrationRetry;
            Register();
            return 0;
        }
        if (message != Callback) return 0;
        var data = (long)lParam;
        if ((_version4 ? (data >> 16) & 0xffff : (long)wParam) != IconId) return 0;
        var signal = (int)(data & 0xffff);
        if (signal == 0x200 && _button != 1) return 0;
        var x = (short)((long)wParam & 0xffff); var y = (short)(((long)wParam >> 16) & 0xffff);
        PixelPoint? mousePoint = _version4 && signal is >= 0x200 and <= 0x20e ? new(x, y) : null;
        if (signal == 0x200) { ObserveDrag(mousePoint); return 0; }
        if (signal is not (0x201 or 0x202 or 0x203 or 0x204 or 0x205 or 0x206 or 0x7b or 0x400 or 0x401)) return 0;
        handled = true;
        var keyboard = signal == 0x401 || (_version4 && signal == 0x7b && x == -1 && y == -1);
        var anchor = IconBounds();
        if (anchor is null && (mousePoint is not null || (_version4 && signal is 0x400 or 0x401 && (x != -1 || y != -1)))) anchor = new(x, y, 1, 1);
        if (anchor is null && !keyboard && _cursor() is { } cursor) anchor = new(cursor.X, cursor.Y, 1, 1);
        switch (signal)
        {
            case 0x201 or 0x203: Down(1, anchor, mousePoint); break; // WM_LBUTTONDOWN / DBLCLK
            case 0x202: Up(1, anchor, mousePoint); break;
            case 0x204 or 0x206: Down(2, anchor, mousePoint); break;
            case 0x205: Up(2, anchor, mousePoint); break;
            default:
                if (keyboard) { _hold.Stop(); _button = 0; _pressAnchor = null; }
                OpenOnce(anchor, keyboard); break;
        }
        return 0;
    }

    private void OwnerClosed(object? sender, EventArgs args) => Dispose();
    internal static PixelPoint? CursorPosition() => GetCursorPos(out var point) ? new(point.X, point.Y) : null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hold.Stop(); _hold.Tick -= OnHold;
        _registrationRetry.Stop(); _registrationRetry.Tick -= OnRegistrationRetry;
        _owner.Closed -= OwnerClosed;
        Win32Properties.RemoveWndProcHookCallback(_owner, OnMessage);
        if (Registered) { var data = Data(_handle); Shell_NotifyIconW(Delete, ref data); Registered = false; }
        if (_icon != 0) DestroyIcon(_icon);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NotifyData
    {
        public uint Size; public nint Window; public uint Id, Flags, Message; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string? Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string? Title;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IconIdentifier { public uint Size; public nint Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point32 { public int X, Y; }
    [DllImport("shell32.dll", ExactSpelling = true)] private static extern bool Shell_NotifyIconW(uint message, ref NotifyData data);
    [DllImport("shell32.dll", ExactSpelling = true)] private static extern int Shell_NotifyIconGetRect(ref IconIdentifier id, out Rect rect);
    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern nint LoadImageW(nint instance, string path, uint type, int width, int height, uint flags);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern nint LoadIconW(nint instance, nint id);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point32 point);
}
