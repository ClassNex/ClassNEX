using System.Collections.Generic;

namespace ClassNex.Services.NexIntelligence;


public static class NexRole
{
    public const string System = "system";
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string Tool = "tool";
}


public sealed class NexToolCall
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    
    public string Arguments { get; set; } = "";
}


public sealed class NexMessage
{
    public string Role { get; set; } = NexRole.User;

    public string Content { get; set; } = "";

    
    public string Reasoning { get; set; } = "";

    
    public List<NexToolCall> ToolCalls { get; set; } = new();

    
    public string ToolCallId { get; set; } = "";

    
    public string ToolName { get; set; } = "";
}


public sealed class NexDelta
{
    public string? Content { get; set; }

    public string? Reasoning { get; set; }

    public bool IsDone { get; set; }
}


public sealed class NexToolDefinition
{
    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    
    public string ParametersJson { get; set; } = """{"type":"object","properties":{}}""";
}


public enum NexPermission
{
    
    ReadOnly,

    
    WorkspaceWrite,

    
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
