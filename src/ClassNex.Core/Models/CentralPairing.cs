namespace ClassNex.Models;

/// <summary>Information shown to the owner when they pair a classroom device; identities are assigned by the server.</summary>
public sealed record CentralPairingStart(string DisplayName, string MachineName, string AppVersion);

/// <summary>A time-limited human code and separate device-only credential. Never display or send the credential to the management website.</summary>
public sealed record CentralPairingTicket(string ClientId, string DeviceToken, string Code, DateTimeOffset ExpiresAt);

/// <summary>Claim a displayed code, optionally joining an existing class owned by the signed-in user.</summary>
public sealed record CentralPairingClaim(string Code, string? ClassId = null);

/// <summary>The device's own enrollment state, and owner display name after approval.</summary>
public sealed record CentralPairingStatus(string State, string? OwnerName = null);
