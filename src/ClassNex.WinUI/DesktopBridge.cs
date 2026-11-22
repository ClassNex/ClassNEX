using ClassNex.Models;
using ClassNex.Core.Abstractions;

namespace ClassNex.WinUI;

public sealed class DesktopBridge
{
    public required Func<AppSettings> ReadSettings { get; init; }
    public required Action SaveSettings { get; init; }
    public required Func<OverlaySnapshot> ReadOverlay { get; init; }
    public required Action<string> Command { get; init; }
    public required Func<string, string> Status { get; init; }
    public required Func<string, Task<string>> ExecuteAsync { get; init; }
    public Func<IPluginManager?> ReadPlugins { get; init; } = () => null;
}
