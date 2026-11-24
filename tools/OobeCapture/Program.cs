using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

if (args.Length is not (2 or 3) || !int.TryParse(args[1], out var pid)) return 2;
var directory = args[0];
var fixture = new DirectoryInfo(Path.GetFullPath(directory));
if (fixture.Parent?.Name != "build" || !fixture.Name.StartsWith("oobe-review-", StringComparison.Ordinal) ||
    !Guid.TryParseExact(fixture.Name[12..], "N", out _) || File.ReadAllText(Path.Combine(fixture.FullName, ".classnex-oobe-review")) != "anonymous-oobe-v1") return 2;
using var process = Process.GetProcessById(pid);
if (!StringComparer.OrdinalIgnoreCase.Equals(process.MainModule?.FileName, Path.Combine(fixture.FullName, "app", "ClassNex.exe"))) return 2;
using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.FullName, "wizard.json")));
if (args is [_, _, "--classroom" or "--classroom-hidden" or "--legacy-classroom" or "--legacy-classroom-hidden"])
{
    var legacy = args[2].StartsWith("--legacy-", StringComparison.Ordinal);
    if ((manifest.RootElement.GetProperty("pid").GetInt32() == pid) == legacy) return 2;
    var expectHidden = args[2].EndsWith("-hidden", StringComparison.Ordinal);
    var timer = Stopwatch.StartNew();
    var hidden = new Stopwatch();
    while (!process.HasExited && timer.Elapsed.TotalSeconds < 20)
    {
        string? title = null;
        bool visible = false;
        foreach (ProcessThread thread in process.Threads)
        {
            Native.EnumThreadWindows((uint)thread.Id, (window, _) =>
            {
                if (Native.GetWindowThreadProcessId(window, out var windowOwner) == 0 || windowOwner != pid) return true;
                var text = new StringBuilder(100);
                Native.GetWindowText(window, text, text.Capacity);
                if (text.ToString() is not ("ClassNEX 主界面" or "ClassNEX 桌面课表")) return true;
                title = text.ToString();
                visible |= Native.IsWindowVisible(window);
                return !visible;
            }, 0);
            if (visible) break;
        }
        if (expectHidden && visible) return 3;
        if (expectHidden && title is not null) hidden.Start(); else hidden.Reset();
        if (title is not null && (expectHidden ? hidden.Elapsed.TotalSeconds >= 1.5 : visible))
        {
            File.WriteAllText(Path.Combine(fixture.FullName, "classroom.json"), JsonSerializer.Serialize(new { pid, title, visible, observedHiddenMs = hidden.ElapsedMilliseconds }));
            return 0;
        }
        await Task.Delay(60);
        process.Refresh();
    }
    return 3;
}
if (args.Length != 2 || manifest.RootElement.GetProperty("pid").GetInt32() != pid) return 2;
var hwnd = (nint)manifest.RootElement.GetProperty("hwnd").GetInt64();
if (hwnd == 0 || Native.GetWindowThreadProcessId(hwnd, out var owner) == 0 || owner != pid) return 2;
if (!GraphicsCaptureSession.IsSupported()) throw new PlatformNotSupportedException("Windows Graphics Capture is unavailable.");
var frames = Directory.CreateDirectory(Path.Combine(fixture.FullName, "window-frames"));
var item = Native.CaptureWindow(hwnd);
using var device = Native.CreateDevice();
using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
using var session = pool.CreateCaptureSession(item);
session.IsCursorCaptureEnabled = false;
session.StartCapture();
var clock = Stopwatch.StartNew();
int count = 0;
while (!process.HasExited && clock.ElapsedMilliseconds < 15000 && Native.GetWindowThreadProcessId(hwnd, out owner) != 0 && owner == pid)
{
    using var frame = pool.TryGetNextFrame();
    if (frame is not null)
    {
        var stamp = clock.ElapsedMilliseconds;
        using var image = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
        await using var file = File.Create(Path.Combine(frames.FullName, $"{stamp:D7}.png"));
        using var output = file.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetSoftwareBitmap(image);
        var scale = Math.Min(1, 650d / image.PixelWidth);
        encoder.BitmapTransform.ScaledWidth = (uint)Math.Round(image.PixelWidth * scale);
        encoder.BitmapTransform.ScaledHeight = (uint)Math.Round(image.PixelHeight * scale);
        encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        await encoder.FlushAsync();
        count++;
    }
    await Task.Delay(35);
}
return count < 8 ? 3 : 0;

internal static class Native
{
    internal delegate bool EnumWindow(nint window, nint parameter);
    [DllImport("user32.dll")] internal static extern bool EnumThreadWindows(uint thread, EnumWindow callback, nint parameter);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint window, StringBuilder title, int length);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string value, int length, out nint text);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint text);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, in Guid iid, out nint factory);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(nint adapter, uint type, nint software, uint flags, nint levels, uint count, uint sdk, out nint device, out uint level, out nint context);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgi, out nint inspectable);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateForWindow(nint factory, nint hwnd, in Guid iid, out nint item);

    internal static GraphicsCaptureItem CaptureWindow(nint hwnd)
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        var interop = new Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
        var itemId = new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
        Marshal.ThrowExceptionForHR(WindowsCreateString(className, className.Length, out var name));
        nint factory = 0, item = 0;
        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, in interop, out factory));
            var create = Marshal.GetDelegateForFunctionPointer<CreateForWindow>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), 3 * IntPtr.Size));
            Marshal.ThrowExceptionForHR(create(factory, hwnd, in itemId, out item));
            return MarshalInspectable<GraphicsCaptureItem>.FromAbi(item);
        }
        finally
        {
            if (item != 0) Marshal.Release(item);
            if (factory != 0) Marshal.Release(factory);
            WindowsDeleteString(name);
        }
    }

    internal static IDirect3DDevice CreateDevice()
    {
        nint device = 0, context = 0, dxgi = 0, inspectable = 0;
        var iid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        try
        {
            var hr = D3D11CreateDevice(0, 1, 0, 32, 0, 0, 7, out device, out _, out context);
            if (hr < 0) Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 5, 0, 32, 0, 0, 7, out device, out _, out context));
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in iid, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
            return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            if (inspectable != 0) Marshal.Release(inspectable);
            if (dxgi != 0) Marshal.Release(dxgi);
            if (context != 0) Marshal.Release(context);
            if (device != 0) Marshal.Release(device);
        }
    }
}
