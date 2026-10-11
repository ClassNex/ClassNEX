using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClassNex.Services.NexIntelligence;

/// <summary>
/// NEX Intelligence 能调用的工具。三个权限档位决定放行范围：
/// 仅可查看 = 只读工具；工作区内修改 = 可改应用数据目录里的文件；完全权限 = 任意路径 + 执行命令。
/// </summary>
public static class NexAgentTools
{
    /// <summary>工作区 = 应用数据目录（课表、设置、日志都在这里）。</summary>
    public static string WorkspaceRoot => SettingsService.DataDirectory;

    /// <summary>命令执行的超时（毫秒）。</summary>
    private const int CommandTimeoutMs = 30_000;

    private const int MaxReadBytes = 200_000;

    public static IReadOnlyList<NexToolDefinition> All { get; } = new List<NexToolDefinition>
    {
        new()
        {
            Name = "get_classnex_info",
            Description = "读取 ClassNEX 当前状态：版本号、是否已导入课表、今天有几节课、当前是否在上课、已启用的桌面组件。",
            ParametersJson = """{"type":"object","properties":{}}""",
        },
        new()
        {
            Name = "get_schedule",
            Description = "读取课表。day 传 today / tomorrow 或 YYYY-MM-DD，默认 today。",
            ParametersJson = """{"type":"object","properties":{"day":{"type":"string","description":"today / tomorrow / YYYY-MM-DD"}}}""",
        },
        new()
        {
            Name = "list_files",
            Description = "列出目录里的文件与子目录。path 留空表示应用数据目录（工作区）。",
            ParametersJson = """{"type":"object","properties":{"path":{"type":"string","description":"目录绝对路径，留空=工作区"}}}""",
        },
        new()
        {
            Name = "read_file",
            Description = "读取文本文件内容（最多 20 万字符）。",
            ParametersJson = """{"type":"object","properties":{"path":{"type":"string","description":"文件绝对路径"}},"required":["path"]}""",
        },
        new()
        {
            Name = "write_file",
            Description = "写入或覆盖文本文件。需要「工作区内修改」或「完全权限」：前者只能写应用数据目录（工作区）里的文件。",
            ParametersJson = """{"type":"object","properties":{"path":{"type":"string","description":"文件绝对路径"},"content":{"type":"string","description":"要写入的完整内容"}},"required":["path","content"]}""",
        },
        new()
        {
            Name = "run_command",
            Description = "执行系统命令并返回输出。只有「完全权限」可用。",
            ParametersJson = """{"type":"object","properties":{"command":{"type":"string","description":"命令行"},"working_directory":{"type":"string","description":"工作目录，可留空"}},"required":["command"]}""",
        },
    };

    /// <summary>执行一个工具调用，返回给模型看的文本结果。</summary>
    public static async Task<(string Result, bool Ok)> ExecuteAsync(
        NexPermission permission, string name, string argumentsJson, CancellationToken ct)
    {
        try
        {
            var args = ParseArgs(argumentsJson);

            switch (name)
            {
                case "get_classnex_info":
                    return (GetInfo(), true);

                case "get_schedule":
                    return (GetSchedule(GetString(args, "day", "today")), true);

                case "list_files":
                    return (ListFiles(GetString(args, "path", "")), true);

                case "read_file":
                    return (ReadFile(GetString(args, "path", "")), true);

                case "write_file":
                {
                    var path = GetString(args, "path", "");
                    var content = GetString(args, "content", "");
                    if (permission == NexPermission.ReadOnly)
                        return ("权限不足：当前是「仅可查看」，不能写入文件。请让用户在权限下拉框里切换到「工作区内修改」或「完全权限」。", false);

                    var full = Path.GetFullPath(path);
                    if (permission == NexPermission.WorkspaceWrite && !IsInWorkspace(full))
                        return ($"权限不足：当前是「工作区内修改」，只能写工作区（{WorkspaceRoot}）里的文件，{full} 在工作区之外。", false);

                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    await File.WriteAllTextAsync(full, content, new UTF8Encoding(false), ct);
                    return ($"已写入 {full}（{content.Length} 字符）", true);
                }

                case "run_command":
                {
                    if (permission != NexPermission.FullAccess)
                        return ("权限不足：执行命令需要「完全权限」，当前档位不允许。请让用户切换权限后再试。", false);

                    var command = GetString(args, "command", "");
                    var cwd = GetString(args, "working_directory", "");
                    return (await RunCommandAsync(command, cwd, ct), true);
                }

                default:
                    return ($"未知工具：{name}", false);
            }
        }
        catch (Exception ex)
        {
            return ($"工具执行失败：{ex.Message}", false);
        }
    }

    // ---------- 具体工具 ----------

    private static string GetInfo()
    {
        var s = AppServices.Settings;
        var now = DateTime.Now;
        var profile = AppServices.Schedule.Profile;
        var slots = AppServices.Time.GetTodaySlots(profile, now);
        var current = AppServices.Time.GetCurrentCourse(slots, now.TimeOfDay);
        var next = AppServices.Time.GetNextCourse(slots, now.TimeOfDay);
        var widgets = string.Join("、", s.Widgets.Where(w => w.IsEnabled).Select(w => w.Type));

        var sb = new StringBuilder();
        sb.AppendLine($"ClassNEX 版本：{s.GetType().Assembly.GetName().Version}");
        sb.AppendLine($"当前时间：{now:yyyy-MM-dd HH:mm:ss}（{now:dddd}）");
        sb.AppendLine($"是否已导入课表：{(profile.Schedules.Count > 0 || profile.Subjects.Count > 0 ? "是" : "否")}");
        sb.AppendLine($"今天课程数：{slots.Count}");
        sb.AppendLine($"当前课程：{(current is null ? "无" : $"{current.DisplayName} {current.TimeRange}")}");
        sb.AppendLine($"下一节课：{(next is null ? "无" : $"{next.DisplayName} {next.TimeRange}")}");
        sb.AppendLine($"已启用组件：{(string.IsNullOrWhiteSpace(widgets) ? "无" : widgets)}");
        sb.AppendLine($"数据目录（工作区）：{SettingsService.DataDirectory}");
        return sb.ToString();
    }

    private static string GetSchedule(string day)
    {
        var now = DateTime.Now;
        var target = day.Trim().ToLowerInvariant() switch
        {
            "" or "today" => now,
            "tomorrow" => now.AddDays(1),
            _ => DateTime.TryParse(day, out var parsed) ? parsed : now,
        };

        var slots = AppServices.Time.GetTodaySlots(AppServices.Schedule.Profile, target);
        if (slots.Count == 0)
            return $"{target:yyyy-MM-dd}（{target:dddd}）没有课程，或者还没有导入课表。";

        var sb = new StringBuilder();
        sb.AppendLine($"{target:yyyy-MM-dd}（{target:dddd}）共 {slots.Count} 节课：");
        foreach (var slot in slots)
        {
            sb.AppendLine($"- {slot.TimeRange} {slot.DisplayName}"
                + (string.IsNullOrWhiteSpace(slot.Detail) ? "" : $"（{slot.Detail}）"));
        }
        return sb.ToString();
    }

    private static string ListFiles(string path)
    {
        var target = string.IsNullOrWhiteSpace(path) ? WorkspaceRoot : Path.GetFullPath(path);
        if (!Directory.Exists(target))
            return $"目录不存在：{target}";

        var sb = new StringBuilder();
        sb.AppendLine($"目录：{target}");
        foreach (var dir in Directory.GetDirectories(target).Take(50))
            sb.AppendLine($"  [目录] {Path.GetFileName(dir)}");
        foreach (var file in Directory.GetFiles(target).Take(100))
            sb.AppendLine($"  {Path.GetFileName(file)}  {new FileInfo(file).Length} 字节");
        return sb.ToString();
    }

    private static string ReadFile(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
            return $"文件不存在：{full}";

        var info = new FileInfo(full);
        if (info.Length > 2_000_000)
            return $"文件太大（{info.Length} 字节），拒绝读取。";

        var text = File.ReadAllText(full);
        if (text.Length > MaxReadBytes)
            text = text[..MaxReadBytes] + "\n…（已截断）";
        return text;
    }

    private static async Task<string> RunCommandAsync(string command, string cwd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command))
            return "命令为空。";

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var workDir = string.IsNullOrWhiteSpace(cwd) ? WorkspaceRoot : cwd;
        if (Directory.Exists(workDir))
            psi.WorkingDirectory = workDir;

        using var process = Process.Start(psi);
        if (process is null)
            return "命令启动失败。";

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CommandTimeoutMs);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { /* 忽略 */ }
            return $"命令超过 {CommandTimeoutMs / 1000} 秒未结束，已终止。";
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var sb = new StringBuilder();
        sb.AppendLine($"退出码：{process.ExitCode}");
        if (!string.IsNullOrWhiteSpace(stdout))
            sb.AppendLine("标准输出：\n" + Clamp(stdout));
        if (!string.IsNullOrWhiteSpace(stderr))
            sb.AppendLine("标准错误：\n" + Clamp(stderr));
        return sb.ToString();

        static string Clamp(string s) => s.Length > 8000 ? s[..8000] + "\n…（已截断）" : s;
    }

    private static bool IsInWorkspace(string fullPath)
    {
        var root = Path.GetFullPath(WorkspaceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, JsonElement> ParseArgs(string json)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
            return map;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return map;

            foreach (var prop in doc.RootElement.EnumerateObject())
                map[prop.Name] = prop.Value.Clone();
        }
        catch (JsonException)
        {
            // 参数不是合法 JSON：当空参处理
        }

        return map;
    }

    private static string GetString(Dictionary<string, JsonElement> args, string key, string fallback)
    {
        if (!args.TryGetValue(key, out var el))
            return fallback;

        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? fallback,
            JsonValueKind.Null or JsonValueKind.Undefined => fallback,
            _ => el.ToString(),
        };
    }
}
