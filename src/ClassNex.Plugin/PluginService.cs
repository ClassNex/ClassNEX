using System.Text.Json;
using ClassNex.Core.Abstractions;

namespace ClassNex.Services;

public sealed class PluginService : IPluginManager
{
    private static readonly StringComparer Ids = StringComparer.OrdinalIgnoreCase;
    private readonly string _root;
    private readonly string _state;
    private readonly IServiceProvider _services;
    private readonly INotificationService _notifications;
    private readonly IAiService _ai;
    private readonly object _gate = new();
    private readonly List<Running> _running = [];
    private IReadOnlyList<PluginInfo> _snapshot = [];
    private List<PluginEntry>? _catalog;
    private int _generation;

    private sealed record Running(string Id, IClassNexPlugin Plugin, PluginLoadContext Context);

    public PluginService(string pluginsDirectory, IServiceProvider services, INotificationService notification, IAiService ai)
        : this(pluginsDirectory, services, notification, ai, Path.Combine(SettingsService.DataDirectory, "PluginSettings")) { }

    public PluginService(string pluginsDirectory, IServiceProvider services, INotificationService notification, IAiService ai, string configurationDirectory)
    {
        _root = Path.GetFullPath(pluginsDirectory);
        _state = Path.GetFullPath(configurationDirectory);
        _services = services;
        _notifications = notification;
        _ai = ai;
    }

    public IReadOnlyList<PluginInfo> LoadedPlugins => Volatile.Read(ref _snapshot);
    public event Action? Changed;

    public Task LoadAllAsync(CancellationToken cancellationToken = default) => RefreshAsync(null, false, cancellationToken);

    public Task SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken = default) => RefreshAsync(id, enabled, cancellationToken);

    private Task RefreshAsync(string? changeId, bool enabled, CancellationToken token)
    {
        var generation = Volatile.Read(ref _generation);
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var notify = false;
            try
            {
                lock (_gate)
                {
                    if (generation != _generation) return;
                    var cached = changeId is null ? null : _catalog;
                    var entries = cached ?? PluginCatalog.Read(_root, token);
                    var switches = ReadSwitches(entries, out var damaged);
                    token.ThrowIfCancellationRequested();
                    HashSet<string>? affected = null;
                    if (changeId is not null)
                    {
                        var matches = entries.Where(p => Ids.Equals(p.Manifest.Id, changeId)).ToArray();
                        if (matches.Length != 1 || matches[0].Error is not null)
                            throw new InvalidOperationException("找不到可切换的唯一插件。");
                        var unchanged = switches.TryGetValue(changeId, out var saved) && saved == enabled;
                        switches[changeId] = enabled;
                        if (cached is not null) affected = FindAffected(entries, switches);
                        if (!damaged && unchanged && affected is { Count: 0 }) return;
                        token.ThrowIfCancellationRequested();
                        SaveSwitches(switches, damaged);
                        damaged = false;
                    }
                    notify = true;
                    StopInstances(affected);
                    try
                    {
                        Load(entries, switches, affected, damaged, changeId is null ? token : CancellationToken.None);
                        _catalog = entries;
                    }
                    catch
                    {
                        StopInstances();
                        _catalog = null;
                        Volatile.Write(ref _snapshot, []);
                        throw;
                    }
                }
            }
            finally { if (notify) Changed?.Invoke(); }
        }, token);
    }

    private HashSet<string> FindAffected(List<PluginEntry> entries, Dictionary<string, bool> switches)
    {
        var affected = new HashSet<string>(Ids);
        var dependents = entries.Where(e => e.Error is null)
            .SelectMany(e => e.Manifest.Dependencies.Keys.Select(id => (Id: id, Child: e.Manifest.Id)))
            .ToLookup(pair => pair.Id, pair => pair.Child, Ids);
        foreach (var (entry, prior) in entries.Zip(_snapshot))
        {
            var manifest = entry.Manifest;
            if (switches.GetValueOrDefault(manifest.Id, manifest.Enabled) != prior.Enabled)
                affected.Add(manifest.Id);
        }
        var pending = new Queue<string>(affected);
        while (pending.TryDequeue(out var parent))
            foreach (var child in dependents[parent])
                if (affected.Add(child)) pending.Enqueue(child);
        return affected;
    }

    public void StopAll()
    {
        lock (_gate)
        {
            Interlocked.Increment(ref _generation);
            StopInstances();
            _catalog = null;
            Volatile.Write(ref _snapshot, []);
        }
        Changed?.Invoke();
    }

    private void StopInstances(HashSet<string>? affected = null)
    {
        for (var i = _running.Count - 1; i >= 0; i--)
        {
            var p = _running[i];
            if (affected is not null && !affected.Contains(p.Id)) continue;
            Release(p.Plugin, p.Context);
            _running.RemoveAt(i);
        }
    }

    private static void Release(IClassNexPlugin? plugin, PluginLoadContext context)
    {
        try { plugin?.Shutdown(); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Plugin shutdown: {0}", ex.Message); }
        finally { context.Unload(); }
    }

    private Dictionary<string, bool> ReadSwitches(List<PluginEntry> entries, out bool damaged)
    {
        damaged = false;
        var path = Path.Combine(_state, "enabled.json");
        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(path));
            return new(values ?? throw new JsonException("启停配置为空。"), Ids);
        }
        catch (FileNotFoundException) { return new(Ids); }
        catch (DirectoryNotFoundException) { return new(Ids); }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            damaged = true;
            return entries.Select(e => e.Manifest.Id).Distinct(Ids).ToDictionary(id => id, _ => false, Ids);
        }
    }

    private void SaveSwitches(Dictionary<string, bool> switches, bool backupDamaged)
    {
        Directory.CreateDirectory(_state);
        var temporary = Path.Combine(_state, $"enabled-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(switches, PluginCatalog.Json));
            var path = Path.Combine(_state, "enabled.json");
            if (backupDamaged) File.Copy(path, Path.Combine(_state, $"enabled.invalid-{Guid.NewGuid():N}.json"));
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private void Load(List<PluginEntry> entries, Dictionary<string, bool> switches, HashSet<string>? affected, bool damaged, CancellationToken token)
    {
        var groups = entries.GroupBy(e => e.Manifest.Id, Ids).ToDictionary(g => g.Key, g => g.ToArray(), Ids);
        var states = new Dictionary<PluginEntry, string>();
        var visiting = new HashSet<string>(Ids);
        if (affected is not null)
            foreach (var (entry, prior) in entries.Zip(_snapshot))
                if (!affected.Contains(entry.Manifest.Id) && prior.Enabled) states[entry] = prior.Status;
        foreach (var entry in entries) Visit(entry);
        Volatile.Write(ref _snapshot, Array.AsReadOnly(entries.Select(e => new PluginInfo(e.Manifest.Id, e.Manifest.Name, e.Manifest.Version, e.AssemblyPath, states[e])
        {
            CanToggle = e.Error is null && groups[e.Manifest.Id].Length == 1,
            Enabled = switches.GetValueOrDefault(e.Manifest.Id, e.Manifest.Enabled),
            Permissions = Array.AsReadOnly(e.Manifest.Permissions ?? ["兼容模式：全部宿主服务"])
        }).ToArray()));

        bool Visit(PluginEntry e)
        {
            token.ThrowIfCancellationRequested();
            var m = e.Manifest;
            if (states.TryGetValue(e, out var state)) return state == "已加载";
            if (visiting.Contains(m.Id)) { states[e] = "插件依赖存在循环"; return false; }
            if (e.Error is not null) states[e] = e.Error;
            else if (groups[m.Id].Length != 1) states[e] = "重复的插件 ID";
            else if (!switches.GetValueOrDefault(m.Id, m.Enabled)) states[e] = damaged ? "启停配置损坏，未加载" : "已禁用";
            else if (m.ApiVersion != PluginCatalog.ApiVersion) states[e] = $"不兼容的插件 API {m.ApiVersion}（宿主：{PluginCatalog.ApiVersion}）";
            else if (!File.Exists(e.AssemblyPath)) states[e] = "程序集不存在";
            else
            {
                visiting.Add(m.Id);
                foreach (var (id, minimum) in m.Dependencies)
                {
                    if (!groups.TryGetValue(id, out var candidates) || candidates.Length != 1)
                    { states[e] = "依赖缺失或重复：" + id; break; }
                    var candidate = candidates[0];
                    if (!Version.TryParse(candidate.Manifest.Version, out var actual) || actual < Version.Parse(minimum))
                    { states[e] = $"需要依赖 {id} ≥ {minimum}"; break; }
                    if (!Visit(candidate)) { states.TryAdd(e, "依赖不可用：" + id); break; }
                }
                if (!states.ContainsKey(e)) states[e] = Activate(e);
                visiting.Remove(m.Id);
            }
            return states[e] == "已加载";
        }
    }

    private string Activate(PluginEntry entry)
    {
        PluginLoadContext? context = null;
        IClassNexPlugin? plugin = null;
        try
        {
            var m = entry.Manifest;
            context = new PluginLoadContext(entry.AssemblyPath);
            var type = context.OpenManaged(entry.AssemblyPath).GetType(m.EntryType, throwOnError: true)!;
            if (!typeof(IClassNexPlugin).IsAssignableFrom(type) || type.IsAbstract) throw new InvalidDataException("入口类型必须实现 IClassNexPlugin。");
            plugin = (IClassNexPlugin)Activator.CreateInstance(type)!;
            var permissions = m.Permissions is null ? null : new PluginPermissions(_services, m.Permissions);
            var config = Path.Combine(_state, "plugins", m.Id);
            Directory.CreateDirectory(config);
            plugin.Initialize(new PluginContext
            {
                Services = permissions is null ? _services : permissions,
                Notification = permissions?.Allows("notifications") == false ? PluginPermissions.Denied.Instance : _notifications,
                Ai = permissions?.Allows("ai") == false ? PluginPermissions.Denied.Instance : _ai,
                ConfigurationDirectory = config
            });
            _running.Add(new(m.Id, plugin, context));
            return "已加载";
        }
        catch (Exception ex)
        {
            if (context is not null) Release(plugin, context);
            return "加载失败：" + ex.GetBaseException().Message;
        }
    }
}
