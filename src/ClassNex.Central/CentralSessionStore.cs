using System.Text.Json;

namespace ClassNex.Central;

public sealed class CentralSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, CentralWebSession> _sessions = new();

    public CentralSessionStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "central-sessions.json");
    }

    public async Task InitializeAsync()
    {
        if (!File.Exists(_path))
            return;

        await using var stream = File.OpenRead(_path);
        _sessions = await JsonSerializer.DeserializeAsync<
                       Dictionary<string, CentralWebSession>>(stream, JsonOptions)
                   ?? new Dictionary<string, CentralWebSession>();
        await RemoveExpiredAsync();
    }

    public async Task SetAsync(
        string token,
        CentralUser user,
        DateTimeOffset expiresAt)
    {
        await _gate.WaitAsync();
        try
        {
            _sessions[token] = new CentralWebSession(user, expiresAt);
            await SaveAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CentralUser?> GetAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        await _gate.WaitAsync();
        try
        {
            if (!_sessions.TryGetValue(token, out var session))
                return null;
            if (session.ExpiresAt > DateTimeOffset.UtcNow)
                return session.User;

            _sessions.Remove(token);
            await SaveAsync();
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;

        await _gate.WaitAsync();
        try
        {
            if (_sessions.Remove(token))
                await SaveAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RemoveExpiredAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = _sessions
            .Where(item => item.Value.ExpiresAt <= now)
            .Select(item => item.Key)
            .ToList();
        if (expired.Count == 0)
            return;

        foreach (var token in expired)
            _sessions.Remove(token);
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, _sessions, JsonOptions);
    }
}

public sealed record CentralWebSession(CentralUser User, DateTimeOffset ExpiresAt);
