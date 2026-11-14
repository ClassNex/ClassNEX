using System.Reflection;
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
    private Timer? _timer;
    private int _busy;

    public event Action<string>? StatusChanged;

    public event Action<IReadOnlyList<CentralClassSummary>>? ClassesChanged;

    public IReadOnlyList<CentralClassSummary> AvailableClasses { get; private set; } = [];

    public void Start()
    {
        _timer ??= new Timer(
            _ => _ = TickAsync(),
            null,
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(30));
    }

    public async Task ConnectAndSyncAsync(CancellationToken cancellationToken = default)
    {
        await RegisterDeviceAsync(cancellationToken);
        await RefreshClassesAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(AppServices.Settings.CentralManagement.SelectedClassId))
            await SyncNowAsync(cancellationToken);
    }

    public async Task RefreshClassesAsync(CancellationToken cancellationToken = default)
    {
        var management = AppServices.Settings.CentralManagement;
        NormalizeServerUrl(management);
        var accessToken = RequireAccessToken();
        AvailableClasses = await _client.GetClassesAsync(
            management.ServerUrl,
            accessToken,
            cancellationToken);

        var selected = AvailableClasses.FirstOrDefault(item =>
            string.Equals(item.ClassId, management.SelectedClassId, StringComparison.Ordinal));
        if (selected is null)
        {
            management.SelectedClassId = "";
            management.SelectedClassName = "";
            management.AppliedRevision = 0;
        }
        else
        {
            management.SelectedClassName = selected.Name;
        }

        SaveManagement();
        ClassesChanged?.Invoke(AvailableClasses);
        SetStatus(AvailableClasses.Count == 0
            ? "账户下还没有班级，请先在集控中心创建"
            : $"已加载 {AvailableClasses.Count} 个班级");
    }

    public async Task SelectClassAsync(string classId, CancellationToken cancellationToken = default)
    {
        var management = AppServices.Settings.CentralManagement;
        var selected = AvailableClasses.FirstOrDefault(item =>
            string.Equals(item.ClassId, classId, StringComparison.Ordinal));
        if (selected is null)
            throw new InvalidOperationException("选择的班级不存在");

        management.SelectedClassId = selected.ClassId;
        management.SelectedClassName = selected.Name;
        management.AppliedRevision = 0;
        management.IsEnabled = true;
        SaveManagement();
        await SyncNowAsync(cancellationToken);
    }

    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
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
            if (string.IsNullOrWhiteSpace(management.SelectedClassId))
            {
                SetStatus("请先选择班级");
                return;
            }

            NormalizeServerUrl(management);
            var accessToken = RequireAccessToken();
            var config = await _client.PullAsync(
                management.ServerUrl,
                accessToken,
                management.SelectedClassId,
                cancellationToken);

            if (config.Revision > management.AppliedRevision &&
                !string.IsNullOrWhiteSpace(config.SettingsJson))
            {
                var remote = JsonSerializer.Deserialize<AppSettings>(
                                 config.SettingsJson,
                                 JsonOptions)
                             ?? throw new InvalidOperationException("集控设置格式无效");
                await Dispatcher.UIThread.InvokeAsync(() =>
                    AppServices.ApplyManagedSettings(remote, config.TimetableYaml));
                management = AppServices.Settings.CentralManagement;
                management.AppliedRevision = config.Revision;
            }

            management.LastSyncTime = DateTime.Now;
            SetStatus($"已同步 · {management.SelectedClassName} · 修订 {management.AppliedRevision}");
            await ReportAsync(accessToken, null, cancellationToken);
        }
        catch (Exception ex)
        {
            SetStatus("同步失败：" + ex.Message);
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
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public void Disconnect()
    {
        var management = AppServices.Settings.CentralManagement;
        management.IsEnabled = false;
        management.SelectedClassId = "";
        management.SelectedClassName = "";
        management.AppliedRevision = 0;
        management.LastSyncTime = null;
        AvailableClasses = [];
        SaveManagement();
        SetStatus("已断开集控");
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
                AppVersion),
            cancellationToken);
        SaveManagement();
    }

    private async Task ReportAsync(
        string accessToken,
        string? error,
        CancellationToken cancellationToken)
    {
        var management = AppServices.Settings.CentralManagement;
        if (string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(management.SelectedClassId))
            return;

        var timetable = File.Exists(AppServices.TimetablePath)
            ? await File.ReadAllTextAsync(AppServices.TimetablePath, cancellationToken)
            : "";
        await _client.ReportAsync(
            management.ServerUrl,
            accessToken,
            management.SelectedClassId,
            new CentralReportRequest(
                management.ClientId,
                AppVersion,
                Environment.MachineName,
                JsonSerializer.Serialize(AppServices.Settings, JsonOptions),
                timetable,
                error),
            cancellationToken);
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

    private static string AppVersion =>
        typeof(AppServices).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "26w45b";

    private void SetStatus(string status)
    {
        AppServices.Settings.CentralManagement.LastStatus = status;
        SaveManagement();
        StatusChanged?.Invoke(status);
    }

    private static void SaveManagement() =>
        SettingsService.Save(AppServices.Settings);

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _client.Dispose();
    }
}
