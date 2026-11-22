using ClassNex.Core.Abstractions;
using ClassNex.Services;

namespace AlphaBinary;

public sealed class Legacy : IClassNexPlugin
{
    private static string TracePath => Path.Combine(AppContext.BaseDirectory, "alpha-lifecycle.txt");
    public void Initialize(PluginContext context)
    {
        if (context.Services.GetService(typeof(ITimeService)) is not ITimeService)
            throw new InvalidOperationException("Missing Alpha time contract");
        File.AppendAllLines(TracePath, ["initialize"]);
    }
    public void Shutdown() => File.AppendAllLines(TracePath, ["shutdown"]);
}
