using System.Text.Json;
using Avalonia.Threading;
using ClassNex.Models;

namespace ClassNex.Services;

public sealed class CentralManagementService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly CentralManagementClient _client = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Timer? _timer;
    private Timer? _publishTimer;
    private int _busy;
    private int _publishing;
    private long _localVersion;
    private long _publishedVersion;
    private bool _polling;
    private bool IsDirty => Volatile.Read(ref _publishedVersion) != Volatile.Read(ref _localVersion);
    private bool _registeredForSession;
    private AccountProfile? _lastAccount;
    private bool _localEventsSubscribed;
    private DateTime _lastReportAt = DateTime.MinValue;

    public event Action<string>? StatusChanged;

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
            AppServices.LocalSettingsChanged += OnLocalSettingsChanged;
            AppServices.Schedule.Saved += OnLocalSettingsChanged;
            AccountService.Changed += OnAccountChanged;
            _localEventsSubscribed = true;
        }

        _timer ??= new Timer(
            _ => _ = TickAsync(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        UpdatePolling();
    }

    private void OnAccountChanged()
    {
        if (ReferenceEquals(_lastAccount, AccountService.Current)) return;
        _lastAccount = AccountService.Current;
        _registeredForSession = false;
        Interlocked.Exchange(ref _publishedVersion, Volatile.Read(ref _localVersion));
        UpdatePolling();
    }

    private void UpdatePolling()
    {
        var enabled = AppServices.Settings.CentralManagement.IsEnabled && AccountService.Current?.IsLoggedIn == true;
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

        try
        {
            var management = AppServices.Settings.CentralManagement;
            if (!management.IsEnabled)
            {
                SetStatus("集控未启用");
                return;
            }

            NormalizeServerUrl(management);
            var accessToken = RequireAccessToken();
            var localVersion = Volatile.Read(ref _localVersion);
            if (IsDirty)
            {
                SetStatus("本机更改等待自动上传");
                return;
            }
            if (!_registeredForSession)
                await RegisterDeviceAsync(cancellationToken);
            var config = await _client.PullAsync(
                management.ServerUrl,
                accessToken,
                management.ClientId,
                cancellationToken);

            if (config is null)
            {
                var classWasAssigned = !string.IsNullOrWhiteSpace(management.SelectedClassId);
                management.SelectedClassId = "";
                management.SelectedClassName = "";
                management.AppliedRevision = 0;
                management.LastSyncTime = DateTime.Now;
                SetStatus("已连接，等待集控端分配班级");
                if (classWasAssigned || ShouldReport())
                    await ReportAsync(accessToken, null, cancellationToken);
                return;
            }

            var classChanged = !string.Equals(
                management.SelectedClassId,
                config.ClassId,
                StringComparison.Ordinal);
            var applied = false;
            if (!string.IsNullOrWhiteSpace(config.SettingsJson) &&
                (classChanged || config.Revision > management.AppliedRevision))
            {
                var remote = JsonSerializer.Deserialize<AppSettings>(
                                 config.SettingsJson,
                                 JsonOptions)
                             ?? throw new InvalidOperationException("集控设置格式无效");
                var accepted = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _localVersion) != localVersion || IsDirty) return false;
                    AppServices.ApplyManagedSettings(remote, config.TimetableYaml);
                    return true;
                });
                if (!accepted) return;
                management = AppServices.Settings.CentralManagement;
                management.AppliedRevision = config.Revision;
                applied = true;
            }

            management.SelectedClassId = config.ClassId;
            management.SelectedClassName = config.ClassName;
            management.LastSyncTime = DateTime.Now;
            SetStatus($"已同步 · {management.SelectedClassName} · 修订 {management.AppliedRevision}");
            if (applied || ShouldReport())
                await ReportAsync(accessToken, null, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            SetStatus("同步失败：" + ex.Message);
            if (ShouldReport())
            {
                try
                {
                    await ReportAsync(
                        AccountService.Current?.AccessToken ?? "",
                        ex.Message,
                        cancellationToken);
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

    public void Disconnect()
    {
        var management = AppServices.Settings.CentralManagement;
        management.IsEnabled = false;
        UpdatePolling();
        management.SelectedClassId = "";
        management.SelectedClassName = "";
        management.AppliedRevision = 0;
        management.LastSyncTime = null;
        _registeredForSession = false;
        _lastReportAt = DateTime.MinValue;
        SaveManagement();
        SetStatus("已断开集控");
    }

    private async Task TickAsync()
    {
        if (!AppServices.Settings.CentralManagement.IsEnabled)
            return;
        await SyncNowAsync();
    }

    private void OnLocalSettingsChanged()
    {
        UpdatePolling();
        if (!AppServices.Settings.CentralManagement.IsEnabled)
        {
            Interlocked.Exchange(ref _publishedVersion, Volatile.Read(ref _localVersion));
            return;
        }
        Interlocked.Increment(ref _localVersion);
        _publishTimer?.Change(TimeSpan.FromMilliseconds(600), Timeout.InfiniteTimeSpan);
    }

    private async Task PublishLocalAsync()
    {
        if (Interlocked.CompareExchange(ref _publishing, 1, 0) != 0)
            return;

        try
        {
            var management = AppServices.Settings.CentralManagement;
            var version = Volatile.Read(ref _localVersion);
            var cancellation = _lifetime.Token;
            if (!management.IsEnabled)
                return;
            if (string.IsNullOrWhiteSpace(management.SelectedClassId))
            {
                Interlocked.Exchange(ref _publishedVersion, version);
                return;
            }

            NormalizeServerUrl(management);
            var accessToken = RequireAccessToken();
            var timetable = File.Exists(AppServices.TimetablePath)
                ? await File.ReadAllTextAsync(AppServices.TimetablePath, cancellation)
                : "";
            var revision = await _client.PublishAsync(
                management.ServerUrl,
                accessToken,
                new CentralClientPublishRequest(
                    management.ClientId,
                    await Dispatcher.UIThread.InvokeAsync(() => CentralSettingsPayload.Serialize(AppServices.Settings)),
                    timetable), cancellation);

            management = AppServices.Settings.CentralManagement;
            management.SelectedClassId = revision.ClassId;
            management.SelectedClassName = revision.ClassName;
            management.AppliedRevision = revision.Revision;
            management.LastSyncTime = DateTime.Now;
            Interlocked.Exchange(ref _publishedVersion, version);
            SetStatus($"已同步本机更改 · {revision.ClassName} · 修订 {revision.Revision}");
        }
        catch (Exception ex)
        {
            SetStatus("本机更改上传失败：" + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _publishing, 0);
            if (IsDirty && _polling && !_lifetime.IsCancellationRequested)
                _publishTimer?.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        }
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
        CancellationToken cancellationToken)
    {
        var management = AppServices.Settings.CentralManagement;
        if (string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(management.ClientId))
            return;

        var timetable = File.Exists(AppServices.TimetablePath)
            ? await File.ReadAllTextAsync(AppServices.TimetablePath, cancellationToken)
            : "";
        await _client.ReportAsync(
            management.ServerUrl,
            accessToken,
            new CentralReportRequest(
                management.ClientId,
                AppServices.ProductVersion,
                Environment.MachineName,
                await Dispatcher.UIThread.InvokeAsync(() => CentralSettingsPayload.Serialize(AppServices.Settings)),
                timetable,
                error),
            cancellationToken);
        _lastReportAt = DateTime.UtcNow;
    }

    private static string RequireAccessToken()
    {
        var token = AccountService.Current?.AccessToken;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("请先登录 ClassNEX ID");
        return token;
    }

    private static void NormalizeServerUrl(CentralManagementSettings management)
    {
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
            AppServices.Schedule.Saved -= OnLocalSettingsChanged;
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
