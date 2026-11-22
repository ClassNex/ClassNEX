using ClassNex.Models;
using ClassNex.Services.NexIntelligence;

namespace ClassNex.Core.Abstractions;

/// <summary>创建独立的 NEX 会话，不要求调用方持有桌面窗口。</summary>
public interface IAiService
{
    /// <summary>建立一段空历史记录的会话，并指定可使用的工具权限。</summary>
    INexAgent CreateAgent(NexPermission permission);
}

/// <summary>一段可取消的 NEX 对话，包含流式回复、工具调用以及会话历史。</summary>
public interface INexAgent
{
    /// <summary>本会话允许的工具能力；不等同于操作系统沙箱。</summary>
    NexPermission Permission { get; set; }

    /// <summary>单轮用户输入最多允许多少次模型和工具往返。</summary>
    int MaxToolRounds { get; set; }

    /// <summary>是否正在处理当前用户消息。</summary>
    bool IsRunning { get; }

    /// <summary>按时间排列的消息，包括工具调用结果；不要在运行中并发修改。</summary>
    List<NexMessage> History { get; }

    /// <summary>收到正文或推理文本的流式片段时触发。</summary>
    event Action<NexDelta>? DeltaReceived;

    /// <summary>即将执行一项工具调用时触发。</summary>
    event Action<NexToolCall>? ToolStarted;

    /// <summary>工具返回时提供原调用、结果文本与成功标志。</summary>
    event Action<NexToolCall, string, bool>? ToolFinished;

    /// <summary>完整的助手消息已加入历史时触发。</summary>
    event Action<NexMessage>? AssistantMessageCompleted;

    /// <summary>请求或工具处理出现需要呈现给调用方的错误时触发。</summary>
    event Action<string>? ErrorOccurred;

    /// <summary>清空本段会话历史，不更改权限或模型配置。</summary>
    void Reset();

    /// <summary>将一条用户输入加入会话，直到回复完成、取消或报告错误。</summary>
    Task RunAsync(string userInput, CancellationToken ct);
}

/// <summary>助手从宿主读取课表和本机状态的窄接口，不依赖具体桌面组件。</summary>
public interface IAiApplicationContext
{
    /// <summary>本机当前生效的应用设置。</summary>
    AppSettings Settings { get; }

    /// <summary>当前启用的课表档案。</summary>
    ScheduleProfile Profile { get; }

    /// <summary>按给定本地日期及其周次解析课程。</summary>
    IReadOnlyList<CourseSlot> GetTodaySlots(DateTime date);

    /// <summary>返回该时刻正在进行的课程；课间或空档返回 null。</summary>
    CourseSlot? GetCurrentCourse(IReadOnlyList<CourseSlot> slots, TimeSpan now);

    /// <summary>返回当日列表中尚未开始的第一节课。</summary>
    CourseSlot? GetNextCourse(IReadOnlyList<CourseSlot> slots, TimeSpan now);

    /// <summary>宿主为助手指定的工作目录。</summary>
    string WorkspaceRoot { get; }

    /// <summary>向用户展示的 ClassNEX 版本。</summary>
    string Version { get; }
}
