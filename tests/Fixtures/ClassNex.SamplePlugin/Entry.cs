using ClassNex.Core.Abstractions;

namespace ClassNex.SamplePlugin;

public class Entry : IClassNexPlugin
{
    private string? _directory;

    public virtual void Initialize(PluginContext context)
    {
        _directory = context.ConfigurationDirectory;
        File.AppendAllLines(Path.Combine(_directory, "lifecycle.txt"), ["init"]);
        context.Notification.DebugTriggerTestNotification();
    }

    public void Shutdown()
    {
        if (_directory is not null) File.AppendAllLines(Path.Combine(_directory, "lifecycle.txt"), ["stop"]);
    }
}

public sealed class PartialFailure : Entry
{
    public override void Initialize(PluginContext context)
    {
        base.Initialize(context);
        throw new InvalidOperationException("fixture initialization failure");
    }
}
