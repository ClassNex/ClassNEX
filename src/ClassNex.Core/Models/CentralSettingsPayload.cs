using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ClassNex.Models;

/// <summary>The subset of portable desktop settings that may cross a device boundary.</summary>
public static class CentralSettingsPayload
{
    private static readonly HashSet<string> LocalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(AppSettings.NexApiKey), nameof(AppSettings.AccountClientId), nameof(AppSettings.AccountEndpoint),
        nameof(AppSettings.Email), nameof(AppSettings.UserName), nameof(AppSettings.IsOobeCompleted),
        nameof(AppSettings.TimetablePath), nameof(AppSettings.CentralManagement), nameof(AppSettings.IsDebugOptionsEnabled),
        nameof(AppSettings.NexPermission), nameof(AppSettings.NexApiBaseUrl), nameof(AppSettings.NexModel),
        nameof(AppSettings.UseLocalReminderSound), nameof(AppSettings.NexSystemPrompt)
    };
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { type =>
        {
            if (type.Type != typeof(AppSettings)) return;
            foreach (var property in type.Properties.Where(p => LocalNames.Contains(p.Name)))
                property.ShouldSerialize = (_, _) => false;
        } } }
    };
    private static readonly Dictionary<string, string> ClassroomFields = Wire.GetTypeInfo(typeof(AppSettings)).Properties
        .Where(p => p.Set is not null && !LocalNames.Contains(p.Name))
        .ToDictionary(p => p.Name, p => p.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Serializes classroom preferences without the local account, credentials, paths or device identity.</summary>
    public static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, Wire);

    /// <summary>Validates incoming classroom preferences and removes device-only fields without filling in omitted settings.</summary>
    public static string FilterIncoming(string json)
    {
        var values = ReadFields(json);
        _ = values.Deserialize<AppSettings>(Wire);
        return values.ToJsonString();
    }

    /// <summary>Applies only the preferences a class supplies; older or partial snapshots do not reset other desktop settings.</summary>
    public static AppSettings Merge(string currentClassroomJson, string updateJson)
    {
        var target = JsonNode.Parse(currentClassroomJson, new JsonNodeOptions { PropertyNameCaseInsensitive = true })!.AsObject();
        static void MergeObject(JsonObject into, JsonObject from)
        {
            foreach (var (key, value) in from)
                if (value is JsonObject group && into[key] is JsonObject existing) MergeObject(existing, group);
                else into[key] = value?.DeepClone();
        }
        MergeObject(target, ReadFields(updateJson));
        return target.Deserialize<AppSettings>(Wire)!;
    }

    private static JsonObject ReadFields(string json)
    {
        if (json.Length > 1024 * 1024) throw new JsonException("集控设置超过大小限制");
        if (JsonNode.Parse(json) is not JsonObject source) throw new JsonException("集控设置必须为 JSON 对象");
        var values = new JsonObject();
        foreach (var (key, value) in source)
            if (ClassroomFields.TryGetValue(key, out var name)) values[name] = value?.DeepClone();
        return values;
    }

    /// <summary>Preserves device-owned values when applying a remote revision, including existing Alpha configurations.</summary>
    public static void PreserveLocal(AppSettings remote, AppSettings local)
    {
        remote.NexApiKey = local.NexApiKey;
        remote.NexApiBaseUrl = local.NexApiBaseUrl;
        remote.NexModel = local.NexModel;
        remote.NexPermission = local.NexPermission;
        remote.NexSystemPrompt = local.NexSystemPrompt;
        remote.AccountEndpoint = local.AccountEndpoint;
        remote.AccountClientId = local.AccountClientId;
        remote.UserName = local.UserName;
        remote.Email = local.Email;
        remote.IsOobeCompleted = local.IsOobeCompleted;
        remote.IsDebugOptionsEnabled = local.IsDebugOptionsEnabled;
        remote.TimetablePath = local.TimetablePath;
        remote.UseLocalReminderSound = local.UseLocalReminderSound;
        remote.CentralManagement = local.CentralManagement;
    }
}
