namespace ClassNex.Models;

public sealed record CentralRegistrationRequest(
    string EnrollmentToken,
    string ClientId,
    string DisplayName,
    string MachineName,
    string AppVersion);

public sealed record CentralRegistrationResponse(
    string ClientId,
    string ClientToken);

public sealed record CentralConfigEnvelope(
    long Revision,
    string SettingsJson,
    string TimetableYaml,
    string Message,
    DateTime UpdatedAt);

public sealed record CentralReportRequest(
    string AppVersion,
    string MachineName,
    string SettingsJson,
    string TimetableYaml,
    string? Error);

public sealed record CentralClientSummary(
    string ClientId,
    string DisplayName,
    string MachineName,
    string AppVersion,
    long Revision,
    DateTime? LastSeen,
    string Message);
