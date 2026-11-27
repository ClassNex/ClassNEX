using System.Security.Cryptography;
using System.Text;
using ClassNex.Models;

namespace ClassNex.Central;

public sealed partial class CentralStore
{
    private sealed record PendingDevice(string Code, byte[] KeyHash, CentralPairingStart Info, DateTimeOffset ExpiresAt);
    private readonly Dictionary<string, PendingDevice> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Attempts, DateTimeOffset Until)> _attempts = new(StringComparer.Ordinal);
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private static readonly TimeSpan PairingLifetime = TimeSpan.FromMinutes(5);
    internal static bool IsPairingId(string id) => id.StartsWith("pair-", StringComparison.OrdinalIgnoreCase);
    private static byte[]? TokenHash(string token) => token.Length == 64 && token.All(char.IsAsciiHexDigit) ? SHA256.HashData(Encoding.ASCII.GetBytes(token)) : null;
    private static bool Matches(byte[]? stored, byte[]? given) => stored is { Length: 32 } && given is { Length: 32 } && CryptographicOperations.FixedTimeEquals(stored, given);

    private void Prune(DateTimeOffset now)
    {
        foreach (var id in _pending.Where(p => p.Value.ExpiresAt <= now).Select(p => p.Key).ToArray()) _pending.Remove(id);
        foreach (var key in _attempts.Where(p => p.Value.Until <= now).Select(p => p.Key).ToArray()) _attempts.Remove(key);
    }

    private bool Allow(string key, int limit, DateTimeOffset now)
    {
        var bucket = _attempts.GetValueOrDefault(key);
        if (bucket.Until <= now) bucket = (0, now.AddMinutes(5));
        if (bucket.Attempts >= limit || (!_attempts.ContainsKey(key) && _attempts.Count >= 8192)) return false;
        _attempts[key] = (bucket.Attempts + 1, bucket.Until);
        return true;
    }

    internal async Task<CentralPairingTicket?> StartPairingAsync(CentralPairingStart info, string address, DateTimeOffset now)
    {
        await _gate.WaitAsync();
        try
        {
            Prune(now);
            if (!Allow("start/" + address, 60, now) || _pending.Count >= 1024) return null;
            var id = "pair-" + Guid.NewGuid().ToString("N");
            var secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            string code;
            do { code = RandomNumberGenerator.GetString(Alphabet, 8); } while (_pending.Values.Any(p => p.Code == code));
            var expires = now + PairingLifetime;
            _pending.Add(id, new(code, TokenHash(secret)!, info, expires));
            return new(id, secret, code.Insert(4, "-"), expires);
        }
        finally { _gate.Release(); }
    }

    internal async Task<(int Status, CentralDeviceSummary? Device)> ClaimAsync(CentralUser owner, CentralPairingClaim claim, string address, DateTimeOffset now)
    {
        await _gate.WaitAsync();
        try
        {
            Prune(now);
            if (!Allow("claim/owner/" + owner.Subject, 10, now) || !Allow("claim/ip/" + address, 150, now)) return (429, null);
            var code = claim.Code is { Length: <= 20 } ? claim.Code.Trim().Replace("-", "").ToUpperInvariant() : "";
            var found = _pending.FirstOrDefault(p => p.Value.Code == code);
            if (found.Value == null) return (404, null);
            var classId = claim.ClassId?.Trim();
            CentralClassRecord? created = null;
            if (string.IsNullOrEmpty(classId))
            {
                created = new() { ClassId = Guid.NewGuid().ToString("N"), Name = found.Value.Info.DisplayName, OwnerSubject = owner.Subject, OwnerName = owner.Name, UpdatedAt = now.UtcDateTime, AwaitingFirstTimetable = true };
                _state.Classes.Add(created.ClassId, created);
                classId = created.ClassId;
            }
            else if (!_state.Classes.TryGetValue(classId, out var target) || target.OwnerSubject != owner.Subject) return (404, null);
            var d = new CentralDeviceRecord { ClientId = found.Key, DisplayName = found.Value.Info.DisplayName, MachineName = found.Value.Info.MachineName,
                AppVersion = found.Value.Info.AppVersion, OwnerSubject = owner.Subject, OwnerName = owner.Name, DeviceKeyHash = found.Value.KeyHash, SelectedClassId = classId, LastSeen = now.UtcDateTime };
            _state.Devices.Add(d.ClientId, d);
            try { await SaveAsync(); }
            catch { _state.Devices.Remove(d.ClientId); if (created != null) _state.Classes.Remove(created.ClassId); throw; }
            _pending.Remove(found.Key);
            return (200, ToDeviceSummary(d, now.UtcDateTime));
        }
        finally { _gate.Release(); }
    }

    internal async Task<(CentralPairingStatus? Status, string? Owner)> DeviceIdentityAsync(string id, string key, DateTimeOffset now)
    {
        var hash = TokenHash(key);
        if (hash == null || !IsPairingId(id)) return default;
        await _gate.WaitAsync();
        try
        {
            if (_state.Devices.TryGetValue(id, out var linked) && Matches(linked.DeviceKeyHash, hash)) return (new("linked", linked.OwnerName), linked.OwnerSubject);
            if (_pending.TryGetValue(id, out var pending) && pending.ExpiresAt > now && Matches(pending.KeyHash, hash)) return (new("pending"), null);
            return default;
        }
        finally { _gate.Release(); }
    }

    internal async Task<bool> RevokeAsync(string id, string? owner, string key = "")
    {
        await _gate.WaitAsync();
        try
        {
            var hash = TokenHash(key);
            if (owner == null && _pending.TryGetValue(id, out var pending) && Matches(pending.KeyHash, hash)) return _pending.Remove(id);
            if (!_state.Devices.TryGetValue(id, out var d) || (owner != null ? d.OwnerSubject != owner : !Matches(d.DeviceKeyHash, hash))) return false;
            _state.Devices.Remove(id);
            try { await SaveAsync(); } catch { _state.Devices.Add(id, d); throw; }
            return true;
        }
        finally { _gate.Release(); }
    }
}
