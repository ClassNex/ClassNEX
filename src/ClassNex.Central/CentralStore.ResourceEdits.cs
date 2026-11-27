using ClassNex.Models;

namespace ClassNex.Central;

internal sealed class CentralEditConflict(string message) : Exception(message);

public sealed partial class CentralStore
{
    internal async Task<CentralClassRevision?> PublishEditAsync(string owner, CentralResourceEdit edit)
    {
        if (edit.BaseRevision < 0 || string.IsNullOrWhiteSpace(edit.ClassId) || (edit.SettingsJson == null) == (edit.TimetableYaml == null))
            throw new ArgumentException("请选择要提交的单项资源和有效的班级修订。");
        var settings = edit.SettingsJson is { } raw ? CentralSettingsPayload.FilterIncoming(raw) : null;
        var hasTimetable = HasTimetable(edit.TimetableYaml);
        await _gate.WaitAsync();
        try
        {
            if (!_state.Devices.TryGetValue(edit.ClientId, out var device) || device.OwnerSubject != owner) return null;
            if (device.SelectedClassId != edit.ClassId || !_state.Classes.TryGetValue(edit.ClassId, out var record) || record.OwnerSubject != owner)
                throw new CentralEditConflict("设备已更换班级或未分配，未提交原班级的更改。");
            ApplyEdit(record, settings, edit.TimetableYaml, hasTimetable, "由客户端自动同步", edit.BaseRevision);
            device.LastSeen = DateTime.UtcNow;
            await SaveAsync();
            return new(record.ClassId, record.Name, record.Revision, record.UpdatedAt);
        }
        finally { _gate.Release(); }
    }

    private static void ApplyEdit(CentralClassRecord record, string? settings, string? timetable, bool hasTimetable, string message, long? baseline)
    {
        if (baseline is { } revision && (revision < 0 || revision > record.Revision ||
            settings != null && settings != record.SettingsJson && revision < record.SettingsRevision ||
            timetable != null && timetable != record.TimetableYaml && revision < record.TimetableRevision))
            throw new CentralEditConflict("同一项内容已在其他设备更新，本次更改未覆盖班级版本。");
        var before = (record.SettingsJson, record.TimetableYaml);
        if (settings != null) record.SettingsJson = settings;
        if (timetable != null) record.TimetableYaml = timetable;
        if (hasTimetable) record.AwaitingFirstTimetable = false;
        var priorRevision = record.Revision;
        record.RecordChanges(before);
        if (record.Revision != priorRevision) record.Message = message.Trim();
    }
}
