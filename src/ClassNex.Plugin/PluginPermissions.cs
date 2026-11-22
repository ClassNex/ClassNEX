using ClassNex.Core.Abstractions;
using ClassNex.Services.NexIntelligence;

namespace ClassNex.Services;

internal sealed class PluginPermissions(IServiceProvider services, IEnumerable<string> names) : IServiceProvider
{
    private readonly HashSet<string> _grants = new(names, StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Type, string> Capabilities = new()
    {
        [typeof(IScheduleService)] = "schedule", [typeof(ITimeService)] = "schedule",
        [typeof(ITimeLayoutService)] = "schedule", [typeof(IWidgetService)] = "widgets",
        [typeof(INotificationService)] = "notifications", [typeof(INotificationPublisher)] = "notifications",
        [typeof(IAiService)] = "ai"
    };

    internal static bool IsKnown(string name) => Capabilities.Values.Contains(name, StringComparer.OrdinalIgnoreCase);
    internal bool Allows(string name) => _grants.Contains(name);
    public object? GetService(Type type) => Capabilities.TryGetValue(type, out var cap) && Allows(cap) ? services.GetService(type) : null;

    internal sealed class Denied : INotificationService, IAiService
    {
        internal static readonly Denied Instance = new();
        private static UnauthorizedAccessException Error() => new("此插件没有声明对应宿主服务权限。");
        public INexAgent CreateAgent(NexPermission permission) => throw Error();
        public void Start() => throw Error();
        public void Stop() => throw Error();
        public void DebugTriggerClassPreparing() => throw Error();
        public void DebugTriggerClassOn() => throw Error();
        public void DebugTriggerClassOff() => throw Error();
        public void DebugTriggerTestNotification() => throw Error();
    }
}
