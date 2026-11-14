using System.Security.Cryptography;
using System.Text.Json;
using ClassNex.Models;
using ClassNex.Services;

namespace ClassNex.Central;

public sealed class CentralStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _statePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CentralState _state = new();

    public CentralStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _statePath = Path.Combine(dataDirectory, "central-state.json");
    }

    public string AdminToken => _state.AdminToken;

    public string EnrollmentToken => _state.EnrollmentToken;

    public async Task InitializeAsync()
    {
        if (File.Exists(_statePath))
        {
            await using var stream = File.OpenRead(_statePath);
            _state = await JsonSerializer.DeserializeAsync<CentralState>(stream, JsonOptions)
                     ?? new CentralState();
        }

        if (string.IsNullOrWhiteSpace(_state.AdminToken))
            _state.AdminToken = CreateToken();
        if (string.IsNullOrWhiteSpace(_state.EnrollmentToken))
            _state.EnrollmentToken = CreateToken();

        var adminOverride = Environment.GetEnvironmentVariable("CLASSNEX_CENTRAL_ADMIN_TOKEN");
        var enrollmentOverride = Environment.GetEnvironmentVariable("CLASSNEX_CENTRAL_ENROLL_TOKEN");
        if (!string.IsNullOrWhiteSpace(adminOverride))
            _state.AdminToken = adminOverride.Trim();
        if (!string.IsNullOrWhiteSpace(enrollmentOverride))
            _state.EnrollmentToken = enrollmentOverride.Trim();

        await SaveAsync();
    }

    public async Task<CentralClientRecord> RegisterAsync(CentralRegistrationRequest request)
    {
        await _gate.WaitAsync();
        try
        {
            var id = string.IsNullOrWhiteSpace(request.ClientId)
                ? Guid.NewGuid().ToString("N")
                : request.ClientId.Trim();
            if (!_state.Clients.TryGetValue(id, out var client))
            {
                client = new CentralClientRecord
                {
                    ClientId = id,
                    ClientToken = CreateToken(),
                };
                _state.Clients[id] = client;
            }
            else if (!string.IsNullOrWhiteSpace(client.MachineName) &&
                     !string.Equals(
                         client.MachineName,
                         request.MachineName.Trim(),
                         StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("客户端标识已被其他设备占用");
            }

            client.ClientToken = CreateToken();

            client.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? request.MachineName
                : request.DisplayName.Trim();
            client.MachineName = request.MachineName.Trim();
            client.AppVersion = request.AppVersion.Trim();
            client.LastSeen = DateTime.UtcNow;
            await SaveAsync();
            return client.Clone();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CentralClientRecord?> FindByClientTokenAsync(string token)
    {
        await _gate.WaitAsync();
        try
        {
            var client = _state.Clients.Values.FirstOrDefault(item =>
                string.Equals(item.ClientToken, token, StringComparison.Ordinal));
            if (client is null)
                return null;

            client.LastSeen = DateTime.UtcNow;
            await SaveAsync();
            return client.Clone();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplyReportAsync(string clientId, CentralReportRequest report)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_state.Clients.TryGetValue(clientId, out var client))
                return;

            client.LastSeen = DateTime.UtcNow;
            client.AppVersion = report.AppVersion;
            client.MachineName = report.MachineName;
            client.LastError = report.Error ?? "";
            if (string.IsNullOrWhiteSpace(client.SettingsJson) &&
                !string.IsNullOrWhiteSpace(report.SettingsJson))
                client.SettingsJson = report.SettingsJson;
            if (string.IsNullOrWhiteSpace(client.TimetableYaml) &&
                !string.IsNullOrWhiteSpace(report.TimetableYaml))
                client.TimetableYaml = report.TimetableYaml;
            await SaveAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<CentralClientSummary>> ListClientsAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return _state.Clients.Values
                .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => item.ToSummary())
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CentralClientRecord?> GetClientAsync(string clientId)
    {
        await _gate.WaitAsync();
        try
        {
            return _state.Clients.TryGetValue(clientId, out var client)
                ? client.Clone()
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CentralClientRecord?> UpdateClientAsync(
        string clientId,
        string settingsJson,
        string timetableYaml,
        string message)
    {
        if (!string.IsNullOrWhiteSpace(settingsJson))
            using (JsonDocument.Parse(settingsJson)) { }
        if (!string.IsNullOrWhiteSpace(timetableYaml))
            _ = CsesCodec.Parse(timetableYaml);

        await _gate.WaitAsync();
        try
        {
            if (!_state.Clients.TryGetValue(clientId, out var client))
                return null;

            var changed = !string.Equals(client.SettingsJson, settingsJson, StringComparison.Ordinal) ||
                          !string.Equals(client.TimetableYaml, timetableYaml, StringComparison.Ordinal);
            client.SettingsJson = settingsJson;
            client.TimetableYaml = timetableYaml;
            client.Message = message.Trim();
            if (changed)
            {
                client.Revision++;
                client.UpdatedAt = DateTime.UtcNow;
            }

            await SaveAsync();
            return client.Clone();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteClientAsync(string clientId)
    {
        await _gate.WaitAsync();
        try
        {
            var removed = _state.Clients.Remove(clientId);
            if (removed)
                await SaveAsync();
            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveAsync()
    {
        await using var stream = File.Create(_statePath);
        await JsonSerializer.SerializeAsync(stream, _state, JsonOptions);
    }

    private static string CreateToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private sealed class CentralState
    {
        public string AdminToken { get; set; } = "";

        public string EnrollmentToken { get; set; } = "";

        public Dictionary<string, CentralClientRecord> Clients { get; set; } = new();
    }
}

public sealed class CentralClientRecord
{
    public string ClientId { get; set; } = "";

    public string ClientToken { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string MachineName { get; set; } = "";

    public string AppVersion { get; set; } = "";

    public long Revision { get; set; }

    public DateTime? LastSeen { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public string Message { get; set; } = "";

    public string LastError { get; set; } = "";

    public string SettingsJson { get; set; } = "";

    public string TimetableYaml { get; set; } = "";

    public CentralClientSummary ToSummary() => new(
        ClientId,
        DisplayName,
        MachineName,
        AppVersion,
        Revision,
        LastSeen,
        Message);

    public CentralClientRecord Clone() => (CentralClientRecord)MemberwiseClone();
}
