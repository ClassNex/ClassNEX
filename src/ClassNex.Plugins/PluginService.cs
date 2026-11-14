using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using ClassNex.Core.Abstractions;

namespace ClassNex.Services;

public sealed class PluginService : IPluginService
{
    private readonly string _pluginsDirectory;
    private readonly IServiceProvider _services;
    private readonly INotificationService _notification;
    private readonly IAiService _ai;
    private readonly List<PluginInfo> _loadedPlugins = new();
    private readonly List<(IClassNexPlugin Plugin, AssemblyLoadContext Context)> _instances = new();

    public PluginService(
        string pluginsDirectory,
        IServiceProvider services,
        INotificationService notification,
        IAiService ai)
    {
        _pluginsDirectory = pluginsDirectory;
        _services = services;
        _notification = notification;
        _ai = ai;
    }

    public IReadOnlyList<PluginInfo> LoadedPlugins => _loadedPlugins;

    public event Action? Changed;

    public async Task LoadAllAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        LoadAll(cancellationToken);
    }

    private void LoadAll(CancellationToken cancellationToken)
    {
        StopAll();
        Directory.CreateDirectory(_pluginsDirectory);

        foreach (var directory in Directory.EnumerateDirectories(_pluginsDirectory).Order())
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryLoad(directory, cancellationToken);
        }

        Changed?.Invoke();
    }

    private void TryLoad(string directory, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(directory, "plugin.json");
        if (!File.Exists(manifestPath))
            return;

        PluginManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PluginManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _loadedPlugins.Add(new PluginInfo(
                Path.GetFileName(directory),
                Path.GetFileName(directory),
                "",
                "",
                "清单错误：" + ex.Message));
            return;
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
            return;

        if (!manifest.Enabled)
        {
            _loadedPlugins.Add(new PluginInfo(
                manifest.Id,
                manifest.Name,
                manifest.Version,
                "",
                "已禁用"));
            return;
        }

        var assemblyPath = Path.GetFullPath(Path.Combine(directory, manifest.Assembly));
        if (!File.Exists(assemblyPath))
        {
            _loadedPlugins.Add(new PluginInfo(
                manifest.Id,
                manifest.Name,
                manifest.Version,
                assemblyPath,
                "程序集不存在"));
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = new PluginLoadContext(assemblyPath);
            var assembly = context.LoadFromAssemblyPath(assemblyPath);
            var type = assembly.GetType(manifest.EntryType, throwOnError: true);
            if (type is null ||
                !typeof(IClassNexPlugin).IsAssignableFrom(type) ||
                Activator.CreateInstance(type) is not IClassNexPlugin plugin)
            {
                context.Unload();
                _loadedPlugins.Add(new PluginInfo(
                    manifest.Id,
                    manifest.Name,
                    manifest.Version,
                    assemblyPath,
                    "入口类型无效"));
                return;
            }

            plugin.Initialize(new PluginContext
            {
                Services = _services,
                Notification = _notification,
                Ai = _ai,
            });

            _instances.Add((plugin, context));
            _loadedPlugins.Add(new PluginInfo(
                manifest.Id,
                manifest.Name,
                manifest.Version,
                assemblyPath,
                "已加载"));
        }
        catch (Exception ex)
        {
            _loadedPlugins.Add(new PluginInfo(
                manifest.Id,
                manifest.Name,
                manifest.Version,
                assemblyPath,
                "加载失败：" + ex.Message));
        }
    }

    public void StopAll()
    {
        foreach (var (plugin, context) in _instances)
        {
            try
            {
                plugin.Shutdown();
            }
            catch
            {
            }

            context.Unload();
        }

        _instances.Clear();
        _loadedPlugins.Clear();
        Changed?.Invoke();
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private static readonly HashSet<string> SharedAssemblies =
            new(StringComparer.OrdinalIgnoreCase) { "ClassNex.Core" };
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath)
            : base(Path.GetFileNameWithoutExtension(pluginPath), isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is { } name && SharedAssemblies.Contains(name))
                return null;

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}
