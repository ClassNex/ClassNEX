using System.Net;
using Avalonia.Threading;
using ClassNex.Models;

namespace ClassNex.Services;

public sealed partial class CentralManagementService
{
    private const string LegacyPending = "集控服务尚不支持分项同步，本机更改已保存；未编辑的项目仍会接收，服务升级后会自动上传";

    private static string DraftNotice(CentralManagementSettings management) => management.LastConflictDraft is { Length: > 0 } file
        ? $"；本机更改未覆盖班级版本，草稿：data/CentralDrafts/{file}" : "";

    private void SchedulePublish(TimeSpan delay)
    {
        if (!_polling || _lifetime.IsCancellationRequested) return;
        try { _publishTimer?.Change(delay, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) when (_lifetime.IsCancellationRequested) { }
    }

    private bool BindDrafts(CentralManagementSettings management)
    {
        try { _journal.Bind(management); Interlocked.And(ref _draftErrors, ~4); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            Interlocked.Or(ref _draftErrors, 4);
            SetStatus("无法读取本机集控草稿，已暂停同步以保留原件：" + ex.Message);
            return false;
        }
    }

    private void OnLocalSettingsChanged() => QueueLocalEdit(false);
    private void OnLocalTimetableChanged() => QueueLocalEdit(true);

    private void QueueLocalEdit(bool timetable)
    {
        UpdatePolling();
        var settings = AppServices.Settings;
        var management = settings.CentralManagement;
        if (!management.IsEnabled || string.IsNullOrEmpty(management.SelectedClassId) || !BindDrafts(management)) return;
        var flag = timetable ? 2 : 1;
        try
        {
            var value = timetable ? File.ReadAllText(AppServices.TimetablePath) : CentralSettingsPayload.Serialize(settings);
            if (!timetable && _lastClassroomSettings == value && (Volatile.Read(ref _draftErrors) & flag) == 0) return;
            Interlocked.Increment(ref _localVersion);
            _journal.Save(timetable, management.AppliedRevision, value);
            if (!timetable) _lastClassroomSettings = value;
            Interlocked.And(ref _draftErrors, ~flag);
            SchedulePublish(TimeSpan.FromMilliseconds(600));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Interlocked.Or(ref _draftErrors, flag);
            SetStatus("无法暂存本机更改，已暂停同步。请检查数据目录后重新保存：" + ex.Message);
        }
    }

    private void KeepConflict(CentralManagementSettings management, CentralEditJournal.Entry edit)
    {
        if (_journal.Archive(edit) is not { } filename) return;
        management.LastConflictDraft = filename;
        Interlocked.Exchange(ref _forcePull, 1);
        SetStatus("检测到班级配置变更" + DraftNotice(management));
    }

    public async Task RestoreLastConflictAsync()
    {
        var generation = Volatile.Read(ref _connectionGeneration);
        var management = AppServices.Settings.CentralManagement;
        if (!management.IsEnabled || !BindDrafts(management) || management.LastConflictDraft is not { Length: > 0 } name)
            throw new InvalidOperationException("当前绑定没有可恢复的冲突草稿。");
        var original = (management.ServerUrl, management.ClientId, management.SelectedClassId);
        var source = await Task.Run(() => _journal.ReadConflict(name), _lifetime.Token);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_lifetime.IsCancellationRequested || !management.IsEnabled || generation != Volatile.Read(ref _connectionGeneration) ||
                (management.ServerUrl, management.ClientId, management.SelectedClassId) != original || management.LastConflictDraft != name ||
                IsDirty || Volatile.Read(ref _forcePull) != 0)
                throw new InvalidOperationException("请等待当前班级同步完成后再恢复草稿。");
            var current = CentralSettingsPayload.Serialize(AppServices.Settings);
            var next = CentralSettingsPayload.Merge(current, source.Timetable ? "{}" : source.Value);
            if (source.Timetable) _ = CsesCodec.Parse(source.Value);
            Interlocked.Increment(ref _localVersion);
            AppServices.ApplyManagedSettings(next, source.Timetable ? source.Value : null);
            if (!source.Timetable) _lastClassroomSettings = null;
            QueueLocalEdit(source.Timetable);
            if (Volatile.Read(ref _draftErrors) != 0) throw new IOException("数据目录暂不可写，原草稿已保留。");
            management.LastConflictDraft = null;
            SetStatus("草稿已恢复到本机，正在同步；原文件仍保留在 data/CentralDrafts");
        });
    }

    private async Task ReceiveUneditedAsync(CentralConfigEnvelope config, CentralManagementSettings management, int generation, long localVersion, CancellationToken cancellation)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (cancellation.IsCancellationRequested || !management.IsEnabled || generation != Volatile.Read(ref _connectionGeneration) || localVersion != Volatile.Read(ref _localVersion)) return;
            var settings = !string.IsNullOrWhiteSpace(config.SettingsJson) && _journal.CanReceive(false, config.Revision, management.AppliedRevision);
            var timetable = !string.IsNullOrWhiteSpace(config.TimetableYaml) && _journal.CanReceive(true, config.Revision, management.AppliedRevision);
            if (settings || timetable)
            {
                var current = CentralSettingsPayload.Serialize(AppServices.Settings);
                AppServices.ApplyManagedSettings(CentralSettingsPayload.Merge(current, settings ? config.SettingsJson : "{}"), timetable ? config.TimetableYaml : null);
                _lastClassroomSettings = CentralSettingsPayload.Serialize(AppServices.Settings);
                if (settings) _journal.Received(false, config.Revision);
                if (timetable) _journal.Received(true, config.Revision);
                management.LastSyncTime = DateTime.Now;
            }
            if (!config.SupportsResourceEdits) SetStatus(LegacyPending + DraftNotice(management));
        });
        if (config.SupportsResourceEdits && _journal.Pending) SchedulePublish(TimeSpan.FromMilliseconds(350));
    }

    private async Task PublishLocalAsync()
    {
        if (!_journal.Pending || Volatile.Read(ref _draftErrors) != 0 || _lifetime.IsCancellationRequested) return;
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) { SchedulePublish(TimeSpan.FromMilliseconds(350)); return; }
        var generation = Volatile.Read(ref _connectionGeneration);
        var management = AppServices.Settings.CentralManagement;
        bool IsCurrent() => management.IsEnabled && !_lifetime.IsCancellationRequested && generation == Volatile.Read(ref _connectionGeneration);
        var retry = TimeSpan.FromSeconds(3);
        try
        {
            if (!IsCurrent() || !BindDrafts(management)) return;
            var cancellation = _lifetime.Token;
            if (Paired(management) && !await CheckPairingAsync(management, generation, cancellation)) return;
            NormalizeServerUrl(management);
            var credential = RequireAccessToken();
            var assignment = await _client.PullAsync(management.ServerUrl, credential, management.ClientId, cancellation);
            if (!IsCurrent()) return;
            if (assignment == null || assignment.ClassId != management.SelectedClassId)
            {
                foreach (var edit in _journal.Snapshot()) KeepConflict(management, edit);
                return;
            }
            if (!assignment.SupportsResourceEdits)
            {
                retry = TimeSpan.FromSeconds(30);
                SetStatus(LegacyPending + DraftNotice(management));
                return;
            }
            foreach (var edit in _journal.Snapshot())
            {
                if (!IsCurrent()) return;
                try
                {
                    var revision = await _client.PublishEditAsync(management.ServerUrl, credential,
                        new(management.ClientId, assignment.ClassId, edit.BaseRevision, edit.Timetable ? null : edit.Value, edit.Timetable ? edit.Value : null), cancellation);
                    if (!IsCurrent()) return;
                    _journal.Accepted(edit, revision.Revision);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
                {
                    if (!IsCurrent()) return;
                    KeepConflict(management, edit);
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (UnauthorizedAccessException) when (Paired(management)) { ExpireBinding(generation); }
        catch (Exception ex)
        {
            if (IsCurrent()) SetStatus("本机更改已保留，将自动重试：" + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            if (_journal.Pending) SchedulePublish(retry);
        }
    }
}
