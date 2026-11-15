namespace ClassNex.Models;

public sealed record CentralDeviceRegistrationRequest(
    string ClientId,
    string DisplayName,
    string MachineName,
    string AppVersion);

public sealed record CentralConfigEnvelope(
    string ClassId,
    string ClassName,
    long Revision,
    string SettingsJson,
    string TimetableYaml,
    string Message,
    DateTime UpdatedAt);

public sealed record CentralReportRequest(
    string ClientId,
    string AppVersion,
    string MachineName,
    string SettingsJson,
    string TimetableYaml,
    string? Error);

public sealed record CentralClassSummary(
    string ClassId,
    string Name,
    long Revision,
    int DeviceCount,
    DateTime UpdatedAt,
    string Message);

public sealed record CentralCreateClassRequest(string Name);

public sealed record CentralDeviceSummary(
    string ClientId,
    string DisplayName,
    string MachineName,
    string AppVersion,
    string ClassId,
    string ClassName,
    DateTime? LastSeen,
    bool IsOnline,
    string LastError);

public sealed record CentralAssignDeviceRequest(string ClassId);
