using System.Collections.Generic;

namespace ClassNex.Services.NexIntelligence;

/// <summary>NEX 消息协议的角色字符串，供历史记录和模型接口共用。</summary>
public static class NexRole
{
    /// <summary>提供给模型的系统级会话说明。</summary>
    public const string System = "system";
    /// <summary>用户输入的消息。</summary>
    public const string User = "user";
    /// <summary>模型回复或请求执行工具的消息。</summary>
    public const string Assistant = "assistant";
    /// <summary>响应某个工具调用的结果消息。</summary>
    public const string Tool = "tool";
}

/// <summary>模型请求的一次工具调用，可用 ID 与结果消息配对。</summary>
public sealed class NexToolCall
{
    /// <summary>此调用在模型回复中的标识。</summary>
    public string Id { get; set; } = "";

    /// <summary>应从宿主工具表解析的名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>模型返回的 JSON 参数文本，执行前必须由对应工具验证。</summary>
    public string Arguments { get; set; } = "";
}

/// <summary>对话历史中的一条完整消息，可包含助手请求或宿主返回的工具结果。</summary>
public sealed class NexMessage
{
    /// <summary>从 NexRole 取值的角色键。</summary>
    public string Role { get; set; } = NexRole.User;

    /// <summary>消息正文；仅请求工具时可以为空。</summary>
    public string Content { get; set; } = "";

    /// <summary>模型另外返回的可选推理片段。</summary>
    public string Reasoning { get; set; } = "";

    /// <summary>该助手消息要求执行的工具调用，保持原始顺序。</summary>
    public List<NexToolCall> ToolCalls { get; set; } = new();

    /// <summary>结果消息所响应的调用 ID。</summary>
    public string ToolCallId { get; set; } = "";

    /// <summary>生成该结果消息的工具名。</summary>
    public string ToolName { get; set; } = "";
}

/// <summary>模型流中的增量事件；文本是新增片段而不是完整消息。</summary>
public sealed class NexDelta
{
    /// <summary>新收到的正文片段，可能为空。</summary>
    public string? Content { get; set; }

    /// <summary>新收到的推理片段，可能为空。</summary>
    public string? Reasoning { get; set; }

    /// <summary>是否已到达当前回复的流末尾。</summary>
    public bool IsDone { get; set; }
}

/// <summary>发给模型的宿主工具描述；实际权限仍由宿主检查。</summary>
public sealed class NexToolDefinition
{
    /// <summary>工具在请求和执行端使用的稳定名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>帮助模型选择工具的简短用途说明。</summary>
    public string Description { get; set; } = "";

    /// <summary>描述可接受参数的 JSON Schema 文本。</summary>
    public string ParametersJson { get; set; } = """{"type":"object","properties":{}}""";
}

/// <summary>助手工具可以访问的能力范围，由本机用户选择。</summary>
public enum NexPermission
{
    /// <summary>不允许通过助手工具修改文件或执行命令。</summary>
    ReadOnly,

    /// <summary>仅允许在指定工作区内写入，不启用命令工具。</summary>
    WorkspaceWrite,

    /// <summary>启用宿主实现的完整能力，仅限明确授权的本机会话。</summary>
    FullAccess,
}

/// <summary>在持久化权限键、权限值与中文界面标签之间转换。</summary>
public static class NexPermissionNames
{
    /// <summary>生成保存到设置文件的稳定键。</summary>
    public static string ToKey(this NexPermission p) => p switch
    {
        NexPermission.WorkspaceWrite => "workspace",
        NexPermission.FullAccess => "full",
        _ => "readonly",
    };

    /// <summary>解析持久化键；为空或未知时使用只读权限。</summary>
    public static NexPermission FromKey(string? key) => key switch
    {
        "workspace" => NexPermission.WorkspaceWrite,
        "full" => NexPermission.FullAccess,
        _ => NexPermission.ReadOnly,
    };

    /// <summary>取得供设置页显示的中文权限名称。</summary>
    public static string ToDisplay(this NexPermission p) => p switch
    {
        NexPermission.WorkspaceWrite => "工作区内修改",
        NexPermission.FullAccess => "完全权限",
        _ => "仅可查看",
    };
}
