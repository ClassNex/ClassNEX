using ClassNex.Models;
using ClassNex.Services.NexIntelligence;

namespace ClassNex.Core.Abstractions;

public interface IAiService
{
    INexAgent CreateAgent(NexPermission permission);
}

public interface INexAgent
{
    NexPermission Permission { get; set; }

    int MaxToolRounds { get; set; }

    bool IsRunning { get; }

    List<NexMessage> History { get; }

    event Action<NexDelta>? DeltaReceived;

    event Action<NexToolCall>? ToolStarted;

    event Action<NexToolCall, string, bool>? ToolFinished;

    event Action<NexMessage>? AssistantMessageCompleted;

    event Action<string>? ErrorOccurred;

    void Reset();

    Task RunAsync(string userInput, CancellationToken ct);
}

public interface IAiApplicationContext
{
    AppSettings Settings { get; }

    ScheduleProfile Profile { get; }

    IReadOnlyList<CourseSlot> GetTodaySlots(DateTime date);

    CourseSlot? GetCurrentCourse(IReadOnlyList<CourseSlot> slots, TimeSpan now);

    CourseSlot? GetNextCourse(IReadOnlyList<CourseSlot> slots, TimeSpan now);

    string WorkspaceRoot { get; }

    string Version { get; }
}
