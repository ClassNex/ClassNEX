using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace ClassNex.RailProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 7) throw new ArgumentException("Expected: [provider-]reference|candidate, runtime, original loader directory, source, output directory, dark|light, accent");
        var runtime = Path.GetFullPath(args[1]);
        var loader = Path.GetFullPath(args[2]);
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var root = name.Name == "Avalonia.Markup.Xaml.Loader" ? loader : runtime;
            var path = Path.Combine(root, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
        {
            var leaf = Path.HasExtension(name) ? name : name + ".dll";
            foreach (var root in new[] { runtime, Path.Combine(runtime, "runtimes", "win-x64", "native") })
                if (NativeLibrary.TryLoad(Path.Combine(root, leaf), out var handle)) return handle;
            return IntPtr.Zero;
        };
        try
        {
            return args[0] switch
            {
                "provider-reference" or "provider-candidate" => ProviderFixture.Run(args[0], args[3], args[4], args[5], args[6]),
                _ => RailFixture.Run(args[0], args[3], args[4], args[5], args[6])
            };
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
