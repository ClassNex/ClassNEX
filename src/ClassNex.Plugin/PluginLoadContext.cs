using System.Reflection;
using System.Runtime.Loader;
using ClassNex.Core.Abstractions;

namespace ClassNex.Services;

internal sealed class PluginLoadContext(string entryPath) : AssemblyLoadContext(entryPath, isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(entryPath);
    private readonly string _directory = Path.GetDirectoryName(entryPath)!;

    internal Assembly OpenManaged(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return LoadFromStream(file);
    }

    protected override Assembly? Load(AssemblyName name)
    {
        var contract = typeof(IClassNexPlugin).Assembly;
        if (string.Equals(contract.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase))
            return contract;
        var path = _resolver.ResolveAssemblyToPath(name);
        if (path is null && name.Name is { } simple && Path.GetFileName(simple) == simple)
        {
            var sibling = Path.Combine(_directory, simple + ".dll");
            if (File.Exists(sibling)) path = sibling;
        }
        return path is null ? null : OpenManaged(path);
    }

    protected override nint LoadUnmanagedDll(string name) => _resolver.ResolveUnmanagedDllToPath(name) is { } path
        ? LoadUnmanagedDllFromPath(path) : 0;
}
