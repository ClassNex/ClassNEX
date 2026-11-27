using Avalonia.Threading;
using ClassNex.Models;

namespace ClassNex.Services;

public sealed partial class CentralManagementService : IDisposable
{
    private readonly CentralManagementClient _client;
    private readonly CancellationTokenSource _lifetime = new();
    private Timer? _timer;
    private Timer? _publishTimer;
    private int _busy;
    private long _localVersion;
    private readonly CentralEditJournal _journal = new();
    private string? _lastClassroomSettings;
    private int _draftErrors;
    private int _forcePull;
    private bool _polling;
    private bool IsDirty => _journal.Pending || Volatile.Read(ref _draftErrors) != 0;
    private bool _registeredForSession;
    private AccountProfile? _lastAccount;
    private bool _localEventsSubscribed;
    private DateTime _lastReportAt = DateTime.MinValue;

    public event Action<string>? StatusChanged;

    public CentralManagementService() : this(new()) { }

    public CentralManagementService(CentralManagementClient client) => _client = client;

    public void Start()
    {
        _publishTimer ??= new Timer(
            _ => _ = PublishLocalAsync(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        if (!_localEventsSubscribed)
        {
            _lastAccount = AccountService.Current;
            _lastClassroomSettings = CentralSettingsPayload.Serialize(AppServices.Settings);
            BindDrafts(AppServices.Settings.CentralManagement);
            AppServices.LocalSettingsChanged += OnLocalSettingsChanged;
            AppServices.LocalTimetableSwitched += OnLocalTimetableChanged;
            AppServices.Schedule.Saved += OnLocalTimetableChanged;
            AccountService.Changed += OnAccountChanged;
            _localEventsSubscribed = true;
        }

        _timer ??= new Timer(
            _ => _ = TickAsync(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        UpdatePolling();
        if (_journal.Pending) SchedulePublish(TimeSpan.FromMilliseconds(600));
    }

    private void OnAccountChanged()
    {
        if (ReferenceEquals(_lastAccount, AccountService.Current)) return;
        _lastAccount = AccountService.Current;
        if (Paired(AppServices.Settings.CentralManagement)) return;
        Interlocked.Increment(ref _connectionGeneration);
        _registeredForSession = false;
        UpdatePolling();
        if (_journal.Pending) SchedulePublish(TimeSpan.FromMilliseconds(600));
    }

    private void UpdatePolling()
    {
        var management = AppServices.Settings.CentralManagement;
        var enabled = management.IsEnabled && (Paired(management) || AccountService.Current?.IsLoggedIn == true);
        if (_polling == enabled) return;
        _polling = enabled;
        _timer?.Change(enabled ? TimeSpan.FromSeconds(1) : Timeout.InfiniteTimeSpan,
            enabled ? TimeSpan.FromSeconds(2) : Timeout.InfiniteTimeSpan);
    }

    public async Task ConnectAndSyncAsync(CancellationToken cancellationToken = default)
    {
        UpdatePolling();
        await SyncNowAsync(cancellationToken);
    }

    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cancellationToken = linked.Token;
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return;
        var generation = Volatile.Read(ref _connectionGeneration);

        try
        {
            var management = AppServices.Settings.CentralManagement;
            if (!management.IsEnabled)
            {
                SetStatus("集控未启用");
                return;
            }

            if (Paired(management) && !await CheckPairingAsync(management, generation, cancellationToken)) return;
            NormalizeServerUrl(management);
            var accessToken = RequireAccessToken();
            var localVersion = Volatile.Read(ref _localVersion);
            if (!BindDrafts(management) || Volatile.Read(ref _draftErrors) != 0) return;
            if (!_registeredForSession && !Paired(management))
                await RegisterDeviceAsync(cancellationToken);
            var config = await _client.PullAsync(
                management.ServerUrl,
                accessToken,
                management.ClientId,
                cancellationToken);
            if (!management.IsEnabled || generation != Volatile.Read(ref _connectionGeneration)) return;
            if (Volatile.Read(ref _localVersion) != localVersion || Volatile.Read(ref _draftErrors) != 0) return;
            if (_journal.Pending)
            {
                if (config != null && config.ClassId == management.SelectedClassId)
                {
                    await ReceiveUneditedAsync(config, management, generation, localVersion, cancellationToken);
                    if (ShouldReport() && generation == Volatile.Read(ref _connectionGeneration))
                        await ReportAsync(accessToken, null, cancellationToken, presenceOnly: true);
                    return;
                }
                foreach (var edit in _journal.Snapshot()) KeepConflict(management, edit);
            }

            if (config is null)
            {
                var classWasAssigned = !string.IsNullOrWhiteSpace(management.SelectedClassId);
                management.SelectedClassId = "";
                management.SelectedClassName = "";
                management.AppliedRevision = 0;
                BindDrafts(management);
                management.LastSyncTime = DateTime.Now;
                SetStatus("已连接，等待集控端分配班级" + DraftNotice(management));
                if (classWasAssigned || ShouldReport())
                    await ReportAsync(accessToken, null, cancellationToken);
                return;
            }

            var classChanged = !string.Equals(
                management.SelectedClassId,
                config.ClassId,
                StringComparison.Ordinal);
            var applied = false;
            var hasSettings = !string.IsNullOrWhiteSpace(config.SettingsJson);
            if ((hasSettings || !string.IsNullOrWhiteSpace(config.TimetableYaml)) &&
                (classChanged || config.Revision > management.AppliedRevision || Volatile.Read(ref _forcePull) != 0))
            {
                var current = await Dispatcher.UIThread.InvokeAsync(() => CentralSettingsPayload.Serialize(AppServices.Settings));
                var remote = CentralSettingsPayload.Merge(current, hasSettings ? config.SettingsJson : "{}");
                var accepted = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested || generation != Volatile.Read(ref _connectionGeneration) || !management.IsEnabled || Volatile.Read(ref _localVersion) != localVersion || IsDirty) return false;
                    AppServices.ApplyManagedSettings(remote, config.TimetableYaml);
                    _lastClassroomSettings = CentralSettingsPayload.Serialize(AppServices.Settings);
                    management.AppliedRevision = config.Revision;
                    management.SelectedClassId = config.ClassId;
                    Interlocked.Exchange(ref _forcePull, 0);
                    return true;
                });
                if (!accepted) return;
                management = AppServices.Settings.CentralManagement;
                applied = true;
            }

            if (classChanged && !applied) management.AppliedRevision = 0;
            management.SelectedClassId = config.ClassId;
            management.SelectedClassName = config.ClassName;
            if (!BindDrafts(management)) return;
            management.LastSyncTime = DateTime.Now;
            SetStatus($"已同步 · {management.SelectedClassName} · 修订 {management.AppliedRevision}" + DraftNotice(management));
            if (applied || ShouldReport())
                await ReportAsync(accessToken, null, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (UnauthorizedAccessException) when (Paired(AppServices.Settings.CentralManagement)) { ExpireBinding(generation); }
        catch (Exception ex)
        {
            if (generation != Volatile.Read(ref _connectionGeneration)) return;
            SetStatus("同步失败：" + ex.Message);
            if (ShouldReport())
            {
                try
                {
                    await ReportAsync(
                        RequireAccessToken(),
                        ex.Message,
                        cancellationToken, presenceOnly: IsDirty);
                }
                catch
                {
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public void Disconnect() => _ = DisconnectAsync();

    private void ResetConnection()
    {
        var management = AppServices.Settings.CentralManagement;
        foreach (var pending in _journal.Snapshot())
        {
            try { KeepConflict(management, pending); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* The original pending file stays in CentralDrafts. */ }
        }
        management.IsEnabled = false;
        UpdatePolling();
        management.PairingCode = "";
        management.SelectedClassId = "";
        management.SelectedClassName = "";
        management.AppliedRevision = 0;
        management.LastSyncTime = null;
        _registeredForSession = false;
        _lastReportAt = DateTime.MinValue;
        BindDrafts(management);
        Interlocked.Exchange(ref _draftErrors, 0);
        Interlocked.Exchange(ref _forcePull, 0);
        SaveManagement();
        SetStatus("已断开集控" + DraftNotice(management));
    }

    private async Task TickAsync()
    {
        if (!AppServices.Settings.CentralManagement.IsEnabled)
            return;
        await SyncNowAsync();
    }

    private async Task RegisterDeviceAsync(CancellationToken cancellationToken)
    {
        var management = AppServices.Settings.CentralManagement;
        NormalizeServerUrl(management);
        var accessToken = RequireAccessToken();
        if (string.IsNullOrWhiteSpace(management.ClientId))
            management.ClientId = Guid.NewGuid().ToString("N");

        await _client.RegisterDeviceAsync(
            management.ServerUrl,
            accessToken,
            new CentralDeviceRegistrationRequest(
                management.ClientId,
                Environment.MachineName,
                Environment.MachineName,
                AppServices.ProductVersion),
            cancellationToken);
        _registeredForSession = true;
        SaveManagement();
    }

    private async Task ReportAsync(
        string accessToken,
        string? error,
        CancellationToken cancellationToken,
        bool presenceOnly = false)
    {
        var management = AppServices.Settings.CentralManagement;
        if (string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(management.ClientId))
            return;

        var timetable = !presenceOnly && File.Exists(AppServices.TimetablePath)
            ? await File.ReadAllTextAsync(AppServices.TimetablePath, cancellationToken)
            : "";
        await _client.ReportAsync(
            management.ServerUrl,
            accessToken,
            new CentralReportRequest(
                management.ClientId,
                AppServices.ProductVersion,
                Environment.MachineName,
                presenceOnly ? "" : await Dispatcher.UIThread.InvokeAsync(() => CentralSettingsPayload.Serialize(AppServices.Settings)),
                timetable,
                error),
            cancellationToken);
        _lastReportAt = DateTime.UtcNow;
    }

    private string RequireAccessToken()
    {
        if (Paired(AppServices.Settings.CentralManagement)) return _ticket is { } ticket && _approved ? CentralManagementClient.AsDevice(ticket) : throw new UnauthorizedAccessException();
        var token = AccountService.Current?.AccessToken;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("请先登录 ClassNEX ID");
        return token;
    }

    private static void NormalizeServerUrl(CentralManagementSettings management)
    {
        if (Paired(management)) { management.ServerUrl = AppSettings.DefaultCentralEndpoint; return; }
        var value = management.ServerUrl.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(value))
        {
            management.ServerUrl = AppSettings.DefaultCentralEndpoint;
            return;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("集控服务地址必须是有效的 HTTP/HTTPS 地址");
        management.ServerUrl = value;
    }

    private void SetStatus(string status)
    {
        if (string.Equals(
                AppServices.Settings.CentralManagement.LastStatus,
                status,
                StringComparison.Ordinal))
            return;

        AppServices.Settings.CentralManagement.LastStatus = status;
        SaveManagement();
        StatusChanged?.Invoke(status);
    }

    private bool ShouldReport() =>
        DateTime.UtcNow - _lastReportAt >= TimeSpan.FromSeconds(30);

    private static void SaveManagement() =>
        SettingsService.Save(AppServices.Settings);

    public void Dispose()
    {
        _lifetime.Cancel();
        if (_localEventsSubscribed)
        {
            AppServices.LocalSettingsChanged -= OnLocalSettingsChanged;
            AppServices.LocalTimetableSwitched -= OnLocalTimetableChanged;
            AppServices.Schedule.Saved -= OnLocalTimetableChanged;
            AccountService.Changed -= OnAccountChanged;
            _localEventsSubscribed = false;
        }

        _timer?.Dispose();
        _timer = null;
        _publishTimer?.Dispose();
        _publishTimer = null;
        _registeredForSession = false;
        _client.Dispose();
    }
}
