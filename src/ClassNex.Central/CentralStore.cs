using System.Text.Json;
using ClassNex.Models;
using ClassNex.Services;

namespace ClassNex.Central;

public sealed partial class CentralStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _statePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CentralState _state = new();
    private DateTime _lastSavedUtc;

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
        foreach (var record in _state.Classes.Values) record.AwaitingFirstTimetable ??= !HasTimetable(record.TimetableYaml);
    }

    public async Task RegisterDeviceAsync(CentralUser user, CentralDeviceRegistrationRequest request)
    {
        await _gate.WaitAsync();
        try
        {
            var id = string.IsNullOrWhiteSpace(request.ClientId)
                ? Guid.NewGuid().ToString("N")
                : request.ClientId.Trim();
            if (IsPairingId(id)) throw new UnauthorizedAccessException("配对设备只能使用自己的设备凭据");
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

    public async Task<IReadOnlyList<CentralDeviceSummary>> ListDevicesAsync(string ownerSubject)
    {
        await _gate.WaitAsync();
        try
        {
            var now = DateTime.UtcNow;
            return _state.Devices.Values
                .Where(item => string.Equals(item.OwnerSubject, ownerSubject, StringComparison.Ordinal))
                .OrderByDescending(item => item.LastSeen)
                .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => ToDeviceSummary(item, now))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CentralDeviceSummary?> AssignDeviceClassAsync(
        string clientId,
        string ownerSubject,
        string classId)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_state.Devices.TryGetValue(clientId, out var device) ||
                !string.Equals(device.OwnerSubject, ownerSubject, StringComparison.Ordinal))
                return null;

            if (!string.IsNullOrWhiteSpace(classId) &&
                (!_state.Classes.TryGetValue(classId, out var record) ||
                 !string.Equals(record.OwnerSubject, ownerSubject, StringComparison.Ordinal)))
                throw new ArgumentException("选择的班级不存在");

            device.SelectedClassId = classId;
            await SaveAsync();
            return ToDeviceSummary(device, DateTime.UtcNow);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CentralAssignedConfig?> GetClientAssignmentAsync(
        string clientId,
        string ownerSubject)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_state.Devices.TryGetValue(clientId, out var device) ||
                !string.Equals(device.OwnerSubject, ownerSubject, StringComparison.Ordinal))
                return null;

            device.LastSeen = DateTime.UtcNow;
            if (device.LastSeen - _lastSavedUtc >= TimeSpan.FromMinutes(1)) await SaveAsync();
            if (string.IsNullOrWhiteSpace(device.SelectedClassId) ||
                !_state.Classes.TryGetValue(device.SelectedClassId, out var record) ||
                !string.Equals(record.OwnerSubject, ownerSubject, StringComparison.Ordinal))
            {
                return null;
            }

            var config = record.Clone();
            if (IsPairingId(clientId) && config.AwaitingFirstTimetable == true) config.TimetableYaml = "";
            return new CentralAssignedConfig(config);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ReportDeviceAsync(
        string ownerSubject,
        CentralReportRequest report)
    {
        var settings = string.IsNullOrWhiteSpace(report.SettingsJson) ? "" : CentralSettingsPayload.FilterIncoming(report.SettingsJson);
        var hasTimetable = HasTimetable(report.TimetableYaml);

        await _gate.WaitAsync();
        try
        {
            if (!_state.Devices.TryGetValue(report.ClientId, out var device) ||
                !string.Equals(device.OwnerSubject, ownerSubject, StringComparison.Ordinal))
                return false;

            device.LastSeen = DateTime.UtcNow;
            device.AppVersion = report.AppVersion;
            device.MachineName = report.MachineName;
            device.LastError = report.Error ?? "";

            if (!string.IsNullOrWhiteSpace(device.SelectedClassId) &&
                _state.Classes.TryGetValue(device.SelectedClassId, out var record) &&
                string.Equals(record.OwnerSubject, ownerSubject, StringComparison.Ordinal))
            {
                var before = (record.SettingsJson, record.TimetableYaml);
                if (string.IsNullOrWhiteSpace(record.SettingsJson) &&
                    settings.Length > 0)
                    record.SettingsJson = settings;
                if ((record.AwaitingFirstTimetable == true || string.IsNullOrWhiteSpace(record.TimetableYaml)) &&
                    hasTimetable)
                {
                    record.TimetableYaml = report.TimetableYaml;
                    record.AwaitingFirstTimetable = false;
                }
                if (before != (record.SettingsJson, record.TimetableYaml)) { record.Revision++; record.UpdatedAt = DateTime.UtcNow; }
            }

            await SaveAsync();
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CentralClassRevision?> PublishDeviceConfigAsync(
        string ownerSubject,
        CentralClientPublishRequest publish)
    {
        var settings = CentralSettingsPayload.FilterIncoming(publish.SettingsJson);
        var hasTimetable = HasTimetable(publish.TimetableYaml);

        await _gate.WaitAsync();
        try
        {
            if (!_state.Devices.TryGetValue(publish.ClientId, out var device) ||
                !string.Equals(device.OwnerSubject, ownerSubject, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(device.SelectedClassId) ||
                !_state.Classes.TryGetValue(device.SelectedClassId, out var record) ||
                !string.Equals(record.OwnerSubject, ownerSubject, StringComparison.Ordinal))
                return null;

            var timetable = record.AwaitingFirstTimetable == true && !hasTimetable && IsPairingId(publish.ClientId) ? record.TimetableYaml : publish.TimetableYaml;
            if (hasTimetable) record.AwaitingFirstTimetable = false;
            var changed =
                !string.Equals(record.SettingsJson, settings, StringComparison.Ordinal) ||
                !string.Equals(record.TimetableYaml, timetable, StringComparison.Ordinal);
            if (changed)
            {
                record.SettingsJson = settings;
                record.TimetableYaml = timetable;
                record.Message = "由客户端自动同步";
                record.Revision++;
                record.UpdatedAt = DateTime.UtcNow;
            }

            device.LastSeen = DateTime.UtcNow;
            await SaveAsync();
            return new CentralClassRevision(
                record.ClassId,
                record.Name,
                record.Revision,
                record.UpdatedAt);
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
                AwaitingFirstTimetable = true,
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
        string? settingsJson,
        string? timetableYaml,
        string message)
    {
        if (!string.IsNullOrWhiteSpace(settingsJson)) settingsJson = CentralSettingsPayload.FilterIncoming(settingsJson);
        var hasTimetable = HasTimetable(timetableYaml);

        await _gate.WaitAsync();
        try
        {
            if (!_state.Classes.TryGetValue(classId, out var record) ||
                !string.Equals(record.OwnerSubject, ownerSubject, StringComparison.Ordinal))
                return null;

            var settings = settingsJson ?? record.SettingsJson;
            var timetable = timetableYaml ?? record.TimetableYaml;
            var changed = !string.Equals(record.SettingsJson, settings, StringComparison.Ordinal) ||
                          !string.Equals(record.TimetableYaml, timetable, StringComparison.Ordinal);
            record.SettingsJson = settings;
            record.TimetableYaml = timetable;
            if (hasTimetable) record.AwaitingFirstTimetable = false;
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

    private static bool HasTimetable(string? yaml)
    {
        if (yaml is { Length: > 4194304 }) throw new ArgumentException("课表超过大小限制");
        if (string.IsNullOrWhiteSpace(yaml)) return false;
        var profile = CsesCodec.Parse(yaml);
        return profile.Subjects?.Count > 0 || profile.Schedules?.Any(s => s?.Classes?.Count > 0) == true || profile.ImportedTimeLayout?.Times?.Count > 0;
    }

    private async Task SaveAsync()
    {
        var pending = _statePath + ".tmp";
        await using (var stream = File.Create(pending)) await JsonSerializer.SerializeAsync(stream, _state, JsonOptions);
        File.Move(pending, _statePath, true);
        _lastSavedUtc = DateTime.UtcNow;
    }

    private CentralDeviceSummary ToDeviceSummary(
        CentralDeviceRecord device,
        DateTime now)
    {
        _state.Classes.TryGetValue(device.SelectedClassId, out var assignedClass);
        var className = assignedClass is not null &&
                        string.Equals(
                            assignedClass.OwnerSubject,
                            device.OwnerSubject,
                            StringComparison.Ordinal)
            ? assignedClass.Name
            : "";
        return new CentralDeviceSummary(
            device.ClientId,
            device.DisplayName,
            device.MachineName,
            device.AppVersion,
            className.Length == 0 ? "" : device.SelectedClassId,
            className,
            device.LastSeen,
            device.LastSeen is { } seen && now - seen <= TimeSpan.FromMinutes(2),
            device.LastError);
    }

    private sealed class CentralState
    {
        public Dictionary<string, CentralClassRecord> Classes { get; set; } = new();

        public Dictionary<string, CentralDeviceRecord> Devices { get; set; } = new();
    }
}

public sealed class CentralClassRecord
{
    private string _settings = "";
    private string? _sharedSettings;

    public bool? AwaitingFirstTimetable { get; set; }

    public string ClassId { get; set; } = "";

    public string OwnerSubject { get; set; } = "";

    public string OwnerName { get; set; } = "";

    public string Name { get; set; } = "";

    public long Revision { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string Message { get; set; } = "";

    public string SettingsJson { get => _settings; set { _settings = value; _sharedSettings = null; } }

    public string TimetableYaml { get; set; } = "";

    public CentralClassSummary ToSummary(int deviceCount) => new(
        ClassId,
        Name,
        Revision,
        deviceCount,
        UpdatedAt,
        Message);

    public CentralClassRecord Clone()
    {
        if (_sharedSettings == null)
        {
            try { _sharedSettings = string.IsNullOrWhiteSpace(_settings) ? "" : CentralSettingsPayload.FilterIncoming(_settings); }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException) { _sharedSettings = ""; }
        }
        var result = (CentralClassRecord)MemberwiseClone();
        result._settings = _sharedSettings;
        return result;
    }
}

public sealed class CentralDeviceRecord
{
    public byte[]? DeviceKeyHash { get; set; }

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

public sealed record CentralAssignedConfig(CentralClassRecord ClassRecord);
