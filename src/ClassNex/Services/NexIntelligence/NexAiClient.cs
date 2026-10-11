using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClassNex.Services.NexIntelligence;

/// <summary>
/// NEX Intelligence 的模型客户端：OpenAI 兼容的 /v1/chat/completions，走流式（SSE）。
/// 已实测该接口返回的增量里有 content（正文）与 reasoning_content（思考过程），
/// 工具调用则分片出现在 delta.tool_calls 里（按 index 拼装）。
/// </summary>
public sealed class NexAiClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    /// <summary>发一轮请求；增量通过回调实时抛出，返回拼好的助手消息（含工具调用）。</summary>
    public async Task<NexMessage> CompleteAsync(
        string baseUrl,
        string apiKey,
        string model,
        IReadOnlyList<NexMessage> messages,
        IReadOnlyList<NexToolDefinition> tools,
        Action<NexDelta>? onDelta,
        CancellationToken ct)
    {
        var result = new NexMessage { Role = NexRole.Assistant };

        var url = baseUrl.TrimEnd('/') + "/v1/chat/completions";
        var payload = BuildPayload(model, messages, tools);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"接口返回 {(int)response.StatusCode}：{Trim(body)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // 工具调用的分片拼装：index -> (id, name, arguments)
        var toolAcc = new Dictionary<int, NexToolCall>();

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // 异步流里不能用 EndOfStream（CA2024）：读到 null 就是流结束
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
                break;
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = line[5..].Trim();
            if (data == "[DONE]")
                break;

            try
            {
                using var doc = JsonDocument.Parse(data);
                if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    continue;

                var choice = choices[0];
                if (!choice.TryGetProperty("delta", out var delta))
                    continue;

                if (delta.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
                {
                    var text = contentEl.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        result.Content += text;
                        onDelta?.Invoke(new NexDelta { Content = text });
                    }
                }

                if (delta.TryGetProperty("reasoning_content", out var reasonEl) && reasonEl.ValueKind == JsonValueKind.String)
                {
                    var text = reasonEl.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        result.Reasoning += text;
                        onDelta?.Invoke(new NexDelta { Reasoning = text });
                    }
                }

                if (delta.TryGetProperty("tool_calls", out var callsEl) && callsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var call in callsEl.EnumerateArray())
                    {
                        var index = call.TryGetProperty("index", out var idxEl) ? idxEl.GetInt32() : 0;
                        if (!toolAcc.TryGetValue(index, out var acc))
                        {
                            acc = new NexToolCall();
                            toolAcc[index] = acc;
                        }

                        if (call.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                            acc.Id = idEl.GetString() ?? acc.Id;

                        if (call.TryGetProperty("function", out var fn))
                        {
                            if (fn.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                                acc.Name = nameEl.GetString() ?? acc.Name;

                            if (fn.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.String)
                                acc.Arguments += argsEl.GetString();
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // 单个分片坏了就跳过，不中断整轮
            }
        }

        foreach (var pair in toolAcc)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value.Name))
                result.ToolCalls.Add(pair.Value);
        }

        onDelta?.Invoke(new NexDelta { IsDone = true });
        return result;
    }

    private static string BuildPayload(string model, IReadOnlyList<NexMessage> messages, IReadOnlyList<NexToolDefinition> tools)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model);
            writer.WriteBoolean("stream", true);

            writer.WriteStartArray("messages");
            foreach (var m in messages)
            {
                writer.WriteStartObject();
                writer.WriteString("role", m.Role);

                if (m.Role == NexRole.Tool)
                {
                    writer.WriteString("tool_call_id", m.ToolCallId);
                    writer.WriteString("content", m.Content);
                }
                else
                {
                    writer.WriteString("content", m.Content);
                }

                if (m.Role == NexRole.Assistant && m.ToolCalls.Count > 0)
                {
                    writer.WriteStartArray("tool_calls");
                    foreach (var call in m.ToolCalls)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("id", call.Id);
                        writer.WriteString("type", "function");
                        writer.WriteStartObject("function");
                        writer.WriteString("name", call.Name);
                        writer.WriteString("arguments", string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments);
                        writer.WriteEndObject();
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            if (tools.Count > 0)
            {
                writer.WriteStartArray("tools");
                foreach (var t in tools)
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", "function");
                    writer.WriteStartObject("function");
                    writer.WriteString("name", t.Name);
                    writer.WriteString("description", t.Description);
                    writer.WritePropertyName("parameters");
                    using (var schema = JsonDocument.Parse(t.ParametersJson))
                        schema.RootElement.WriteTo(writer);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string Trim(string s) => s.Length <= 400 ? s : s[..400] + "…";
}
