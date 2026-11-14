namespace ClassNex.Core.Abstractions;

public interface IPluginService
{
    IReadOnlyList<PluginInfo> LoadedPlugins { get; }

    event Action? Changed;

    Task LoadAllAsync(CancellationToken cancellationToken = default);

    void StopAll();
}

public sealed record PluginInfo(
    string Id,
    string Name,
    string Version,
    string AssemblyPath,
    string Status);

public sealed record PluginManifest(
    string Id,
    string Name,
    string Version,
    string Assembly,
    string EntryType,
    bool Enabled = true);

public interface IClassNexPlugin
{
    void Initialize(PluginContext context);

    void Shutdown();
}

public sealed class PluginContext
{
    public required IServiceProvider Services { get; init; }

    public required INotificationService Notification { get; init; }

    public required IAiService Ai { get; init; }
}

public sealed class PluginServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = new();

    public void Add<T>(T service) where T : class
    {
        _services[typeof(T)] = service;
    }

    public object? GetService(Type serviceType) =>
        _services.TryGetValue(serviceType, out var service) ? service : null;
}
