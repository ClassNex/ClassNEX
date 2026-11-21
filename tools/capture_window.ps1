param(
    [string]$ProcessName = "ClassNex",
    [string]$Title = "ClassNEX 主界面",
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class ClassNexWindowCapture
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

    public static IntPtr Find(uint processId, string title)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint owner);
            if (owner != processId || !IsWindowVisible(hWnd))
                return true;

            var text = new StringBuilder(512);
            GetWindowText(hWnd, text, text.Capacity);
            if (!string.Equals(text.ToString(), title, StringComparison.Ordinal))
                return true;

            result = hWnd;
            return false;
        }, IntPtr.Zero);
        return result;
    }
}
"@

$process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $process) {
    throw "Process $ProcessName is not running."
}

$handle = [ClassNexWindowCapture]::Find([uint32]$process.Id, $Title)
if ($handle -eq [IntPtr]::Zero) {
    throw "Window '$Title' was not found."
}

$rect = New-Object ClassNexWindowCapture+RECT
if (-not [ClassNexWindowCapture]::GetWindowRect($handle, [ref]$rect)) {
    throw "GetWindowRect failed."
}

$width = [Math]::Max(1, $rect.Right - $rect.Left)
$height = [Math]::Max(1, $rect.Bottom - $rect.Top)
$bitmap = New-Object System.Drawing.Bitmap($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()
try {
    if (-not [ClassNexWindowCapture]::PrintWindow($handle, $hdc, 2)) {
        throw "PrintWindow failed."
    }
}
finally {
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
}

$directory = Split-Path -Parent $OutputPath
if ($directory) {
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
}
$bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
Write-Output $OutputPath
