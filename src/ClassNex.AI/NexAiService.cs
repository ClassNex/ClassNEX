using ClassNex.Core.Abstractions;
using ClassNex.Services.NexIntelligence;

namespace ClassNex.Services;

public sealed class NexAiService : IAiService
{
    private readonly IAiApplicationContext _context;

    public NexAiService(IAiApplicationContext context)
    {
        _context = context;
    }

    public INexAgent CreateAgent(NexPermission permission) =>
        new NexAgent(_context)
        {
            Permission = permission,
        };
}
