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

    public async Task InitializeAsync()
    {
        if (!File.Exists(_statePath))
            return;

        await using var stream = File.OpenRead(_statePath);
        _state = await JsonSerializer.DeserializeAsync<CentralState>(stream, JsonOptions)
                 ?? new CentralState();
    }

    public async Task RegisterDeviceAsync(CentralUser user, CentralDeviceRegistrationRequest request)
    {
        await _gate.WaitAsync();
        try
        {
            var id = string.IsNullOrWhiteSpace(request.ClientId)
                ? Guid.NewGuid().ToString("N")
                : request.ClientId.Trim();
            if (!_state.Devices.TryGetValue(id, out var device))
            {
                device = new CentralDeviceRecord { ClientId = id };
                _state.Devices[id] = device;
            }
            else if (!string.Equals(device.OwnerSubject, user.Subject, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("客户端标识已被其他账户占用");
            }

            device.OwnerSubject = user.Subject;
            device.OwnerName = user.Name;
            device.DisplayName = request.DisplayName.Trim();
            device.MachineName = request.MachineName.Trim();
            device.AppVersion = request.AppVersion.Trim();
            device.LastSeen = DateTime.UtcNow;
            await SaveAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<CentralClassSummary>> ListClassesAsync(string ownerSubject)
    {
        await _gate.WaitAsync();
        try
        {
            return _state.Classes.Values
                .Where(item => string.Equals(item.OwnerSubject, ownerSubject, StringComparison.Ordinal))
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => item.ToSummary(
                    _state.Devices.Values.Count(device =>
                        string.Equals(device.SelectedClassId, item.ClassId, StringComparison.Ordinal))))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CentralClassRecord> CreateClassAsync(
        CentralUser user,
        CentralCreateClassRequest request)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("班级名称不能为空");

        await _gate.WaitAsync();
        try
        {
            var record = new CentralClassRecord
            {
                ClassId = Guid.NewGuid().ToString("N"),
                OwnerSubject = user.Subject,
                OwnerName = user.Name,
                Name = name,
                UpdatedAt = DateTime.UtcNow,
            };
            _state.Classes[record.ClassId] = record;
            await SaveAsync();
            return record.Clone();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CentralClassRecord?> GetClassAsync(string classId, string ownerSubject)
    {
        await _gate.WaitAsync();
        try
        {
            return _state.Classes.TryGetValue(classId, out var record) &&
                   string.Equals(record.OwnerSubject, ownerSubject, StringComparison.Ordinal)
                ? record.Clone()
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CentralClassRecord?> UpdateClassAsync(
        string classId,
        string ownerSubject,
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
            if (!_state.Classes.TryGetValue(classId, out var record) ||
                !string.Equals(record.OwnerSubject, ownerSubject, StringComparison.Ordinal))
                return null;

            var changed = !string.Equals(record.SettingsJson, settingsJson, StringComparison.Ordinal) ||
                          !string.Equals(record.TimetableYaml, timetableYaml, StringComparison.Ordinal);
            record.SettingsJson = settingsJson;
            record.TimetableYaml = timetableYaml;
            record.Message = message.Trim();
            if (changed)
            {
                record.Revision++;
                record.UpdatedAt = DateTime.UtcNow;
            }
            await SaveAsync();
            return record.Clone();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteClassAsync(string classId, string ownerSubject)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_state.Classes.TryGetValue(classId, out var record) ||
                !string.Equals(record.OwnerSubject, ownerSubject, StringComparison.Ordinal))
                return false;

            _state.Classes.Remove(classId);
            foreach (var device in _state.Devices.Values.Where(item =>
                         string.Equals(item.SelectedClassId, classId, StringComparison.Ordinal)))
            {
                device.SelectedClassId = "";
            }
            await SaveAsync();
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ApplyReportAsync(
        string classId,
        string ownerSubject,
        string clientId,
        CentralReportRequest report)
    {
        if (!string.IsNullOrWhiteSpace(report.SettingsJson))
            using (JsonDocument.Parse(report.SettingsJson)) { }
        if (!string.IsNullOrWhiteSpace(report.TimetableYaml))
            _ = CsesCodec.Parse(report.TimetableYaml);

        await _gate.WaitAsync();
        try
        {
            if (!_state.Classes.TryGetValue(classId, out var record) ||
                !string.Equals(record.OwnerSubject, ownerSubject, StringComparison.Ordinal))
                return false;

            if (_state.Devices.TryGetValue(clientId, out var device) &&
                string.Equals(device.OwnerSubject, ownerSubject, StringComparison.Ordinal))
            {
                device.SelectedClassId = classId;
                device.LastSeen = DateTime.UtcNow;
                device.AppVersion = report.AppVersion;
                device.MachineName = report.MachineName;
                device.LastError = report.Error ?? "";
            }

            if (string.IsNullOrWhiteSpace(record.SettingsJson) &&
                !string.IsNullOrWhiteSpace(report.SettingsJson))
                record.SettingsJson = report.SettingsJson;
            if (string.IsNullOrWhiteSpace(record.TimetableYaml) &&
                !string.IsNullOrWhiteSpace(report.TimetableYaml))
                record.TimetableYaml = report.TimetableYaml;
            await SaveAsync();
            return true;
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

    private sealed class CentralState
    {
        public Dictionary<string, CentralClassRecord> Classes { get; set; } = new();

        public Dictionary<string, CentralDeviceRecord> Devices { get; set; } = new();
    }
}

public sealed class CentralClassRecord
{
    public string ClassId { get; set; } = "";

    public string OwnerSubject { get; set; } = "";

    public string OwnerName { get; set; } = "";

    public string Name { get; set; } = "";

    public long Revision { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string Message { get; set; } = "";

    public string SettingsJson { get; set; } = "";

    public string TimetableYaml { get; set; } = "";

    public CentralClassSummary ToSummary(int deviceCount) => new(
        ClassId,
        Name,
        Revision,
        deviceCount,
        UpdatedAt,
        Message);

    public CentralClassRecord Clone() => (CentralClassRecord)MemberwiseClone();
}

public sealed class CentralDeviceRecord
{
    public string ClientId { get; set; } = "";

    public string OwnerSubject { get; set; } = "";

    public string OwnerName { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string MachineName { get; set; } = "";

    public string AppVersion { get; set; } = "";

    public string SelectedClassId { get; set; } = "";

    public DateTime? LastSeen { get; set; }

    public string LastError { get; set; } = "";
}
