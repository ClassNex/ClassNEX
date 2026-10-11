using System.Collections.Generic;

namespace ClassNex.Services.NexIntelligence;

/// <summary>对话消息角色。</summary>
public static class NexRole
{
    public const string System = "system";
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string Tool = "tool";
}

/// <summary>工具调用（模型要求执行某个工具）。</summary>
public sealed class NexToolCall
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>参数字符串（JSON，流式到达时是分片拼起来的）。</summary>
    public string Arguments { get; set; } = "";
}

/// <summary>一轮对话里的消息（发给接口 / 从接口收到）。</summary>
public sealed class NexMessage
{
    public string Role { get; set; } = NexRole.User;

    public string Content { get; set; } = "";

    /// <summary>模型的思考过程（部分模型返回 reasoning_content）。</summary>
    public string Reasoning { get; set; } = "";

    /// <summary>本轮模型请求的工具调用。</summary>
    public List<NexToolCall> ToolCalls { get; set; } = new();

    /// <summary>工具结果消息对应的调用 id。</summary>
    public string ToolCallId { get; set; } = "";

    /// <summary>工具名（工具结果消息用，展示需要）。</summary>
    public string ToolName { get; set; } = "";
}

/// <summary>流式返回的一个增量。</summary>
public sealed class NexDelta
{
    public string? Content { get; set; }

    public string? Reasoning { get; set; }

    public bool IsDone { get; set; }
}

/// <summary>工具定义（发给接口的 function 描述）。</summary>
public sealed class NexToolDefinition
{
    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>JSON Schema 形式的参数定义。</summary>
    public string ParametersJson { get; set; } = """{"type":"object","properties":{}}""";
}

/// <summary>权限档位。</summary>
public enum NexPermission
{
    /// <summary>仅可查看：只能读，不能改任何东西。</summary>
    ReadOnly,

    /// <summary>工作区内修改：可以改应用数据目录里的文件。</summary>
    WorkspaceWrite,

    /// <summary>完全权限：可以写任意路径、执行命令。</summary>
    FullAccess,
}

public static class NexPermissionNames
{
    public static string ToKey(this NexPermission p) => p switch
    {
        NexPermission.WorkspaceWrite => "workspace",
        NexPermission.FullAccess => "full",
        _ => "readonly",
    };

    public static NexPermission FromKey(string? key) => key switch
    {
        "workspace" => NexPermission.WorkspaceWrite,
        "full" => NexPermission.FullAccess,
        _ => NexPermission.ReadOnly,
    };

    public static string ToDisplay(this NexPermission p) => p switch
    {
        NexPermission.WorkspaceWrite => "工作区内修改",
        NexPermission.FullAccess => "完全权限",
        _ => "仅可查看",
    };
}
