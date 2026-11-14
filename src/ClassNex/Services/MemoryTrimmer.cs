using System;
using System.Runtime.InteropServices;

namespace ClassNex.Services;

public static class MemoryTrimmer
{
    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr process);

    public static void Trim()
    {
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);

        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            EmptyWorkingSet(process.Handle);
        }
        catch
        {
        }
    }
}
