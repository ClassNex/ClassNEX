using System.Text.Json;
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
        nameof(AppSettings.UseLocalReminderSound)
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

    /// <summary>Serializes classroom preferences without the local account, credentials, paths or device identity.</summary>
    public static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, Wire);

    /// <summary>Preserves device-owned values when applying a remote revision, including existing Alpha configurations.</summary>
    public static void PreserveLocal(AppSettings remote, AppSettings local)
    {
        remote.NexApiKey = local.NexApiKey;
        remote.NexApiBaseUrl = local.NexApiBaseUrl;
        remote.NexModel = local.NexModel;
        remote.NexPermission = local.NexPermission;
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
