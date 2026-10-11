using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClassNex.Services.NexIntelligence;

/// <summary>
/// NEX Agent：一轮对话 = 发消息 → 流式收正文/思考 → 若有工具调用则执行并回填 → 继续，直到模型不再要工具。
/// </summary>
public sealed class NexAgent
{
    private readonly NexAiClient _client = new();

    /// <summary>对话历史（含工具调用与工具结果）。</summary>
    public List<NexMessage> History { get; } = new();

    /// <summary>本次会话的权限档位（由对话框顶部的选择器决定）。</summary>
    public NexPermission Permission { get; set; } = NexPermission.ReadOnly;

    /// <summary>单轮里最多执行几轮工具调用，防止打转。</summary>
    public int MaxToolRounds { get; set; } = 8;

    public bool IsRunning { get; private set; }

    /// <summary>流式增量（正文 / 思考）。</summary>
    public event Action<NexDelta>? DeltaReceived;

    /// <summary>开始执行某个工具。</summary>
    public event Action<NexToolCall>? ToolStarted;

    /// <summary>工具执行结束（调用、结果文本、是否成功）。</summary>
    public event Action<NexToolCall, string, bool>? ToolFinished;

    /// <summary>一条助手消息接收完毕（含纯文本与工具调用两种）。</summary>
    public event Action<NexMessage>? AssistantMessageCompleted;

    /// <summary>出错（网络、鉴权、模型不存在等）。</summary>
    public event Action<string>? ErrorOccurred;

    public void Reset()
    {
        History.Clear();
    }

    /// <summary>发一条用户消息并跑完一整轮（含工具往返）。</summary>
    public async Task RunAsync(string userInput, CancellationToken ct)
    {
        if (IsRunning)
            return;

        var settings = AppServices.Settings;
        var apiKey = NexConfig.ResolveApiKey(settings);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ErrorOccurred?.Invoke("还没有可用的接口密钥，请到「应用设置 → 调试 → NEX Intelligence 接口」里填写。");
            return;
        }

        History.Add(new NexMessage { Role = NexRole.User, Content = userInput });

        IsRunning = true;
        try
        {
            for (var round = 0; round < MaxToolRounds; round++)
            {
                ct.ThrowIfCancellationRequested();

                var request = new List<NexMessage> { BuildSystemMessage() };
                request.AddRange(History);

                var assistant = await _client.CompleteAsync(
                    NexConfig.ResolveBaseUrl(settings),
                    apiKey,
                    NexConfig.ResolveModel(settings),
                    request,
                    NexAgentTools.All,
                    delta => DeltaReceived?.Invoke(delta),
                    ct);

                History.Add(assistant);
                AssistantMessageCompleted?.Invoke(assistant);

                if (assistant.ToolCalls.Count == 0)
                    break;

                foreach (var call in assistant.ToolCalls)
                {
                    ct.ThrowIfCancellationRequested();

                    ToolStarted?.Invoke(call);
                    var (result, ok) = await NexAgentTools.ExecuteAsync(Permission, call.Name, call.Arguments, ct);
                    ToolFinished?.Invoke(call, result, ok);

                    History.Add(new NexMessage
                    {
                        Role = NexRole.Tool,
                        ToolCallId = call.Id,
                        ToolName = call.Name,
                        Content = result,
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 用户点了停止：什么都不做，历史保留
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>系统提示词：带上应用现状、工作区与当前权限，让模型知道自己能做什么。</summary>
    private NexMessage BuildSystemMessage()
    {
        var settings = AppServices.Settings;
        var now = DateTime.Now;
        var profile = AppServices.Schedule.Profile;
        var hasTimetable = profile.Schedules.Count > 0 || profile.Subjects.Count > 0;

        var permissionText = Permission switch
        {
            NexPermission.WorkspaceWrite =>
                $"「工作区内修改」——你可以读写工作区（{SettingsService.DataDirectory}）里的文件，但不能动工作区之外的东西，也不能执行命令。",
            NexPermission.FullAccess =>
                "「完全权限」——你可以读写任意路径的文件，也可以执行系统命令。执行破坏性操作前先跟用户确认。",
            _ =>
                "「仅可查看」——你只能读取信息（读取课表、列目录、读文件、看应用状态），不能修改任何文件、也不能执行命令。",
        };

        var custom = settings.NexSystemPrompt;
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(custom))
        {
            sb.AppendLine(custom.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("你是 NEX，ClassNEX（一个面向教室大屏的课表显示应用，运行在 Windows 桌面）内置的 AI 助手。");
        sb.AppendLine();
        sb.AppendLine("【当前环境】");
        sb.AppendLine($"- 时间：{now:yyyy-MM-dd HH:mm:ss}（{now:dddd}）");
        sb.AppendLine($"- 是否已导入课表：{(hasTimetable ? "是" : "否")}（若为否，可以提示用户去档案编辑器导入 CSES 课表）");
        sb.AppendLine($"- 工作区（应用数据目录）：{SettingsService.DataDirectory}");
        sb.AppendLine($"- 当前权限档位：{permissionText}");
        sb.AppendLine();
        sb.AppendLine("【工作方式】");
        sb.AppendLine("- 一律用简体中文回答，直接、自然，不要客套话堆砌。");
        sb.AppendLine("- 需要应用里的真实数据（课表、状态、文件）时，先调用工具拿数据，不要凭空猜。");
        sb.AppendLine("- 要改文件或执行命令之前，先用一句话说明你打算做什么，然后调用工具。");
        sb.AppendLine("- 如果因为权限不足被拒绝，明确告诉用户需要切到哪一档权限，不要反复重试同一个工具。");
        sb.AppendLine("- 回答尽量短，能用列表就用列表。");
        sb.AppendLine("- 直接输出纯文本：不要用 Markdown 标记（不要 **加粗**、不要 ### 标题、不要表格），需要强调就用中文标点或分行。");

        return new NexMessage { Role = NexRole.System, Content = sb.ToString() };
    }
}
