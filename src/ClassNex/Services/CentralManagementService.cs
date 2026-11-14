using System.Reflection;
using System.Text;
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

    public string CurrentStatus => AppServices.Settings.CentralManagement.LastStatus;

    public void Start()
    {
        _timer ??= new Timer(
            _ => _ = TickAsync(),
            null,
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(30));
    }

    public async Task RegisterAndSyncAsync(CancellationToken cancellationToken = default)
    {
        var management = AppServices.Settings.CentralManagement;
        NormalizeServerUrl(management);
        await RegisterAsync(cancellationToken);
        await SyncNowAsync(cancellationToken);
    }

    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return;

        try
        {
            var management = AppServices.Settings.CentralManagement;
            if (!management.IsEnabled || string.IsNullOrWhiteSpace(management.ServerUrl))
            {
                SetStatus("集控未启用");
                return;
            }

            NormalizeServerUrl(management);
            if (string.IsNullOrWhiteSpace(management.ClientToken))
                await RegisterAsync(cancellationToken);

            CentralConfigEnvelope config;
            try
            {
                config = await _client.PullAsync(
                    management.ServerUrl,
                    management.ClientToken,
                    cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                management.ClientToken = "";
                SaveManagement();
                await RegisterAsync(cancellationToken);
                config = await _client.PullAsync(
                    management.ServerUrl,
                    management.ClientToken,
                    cancellationToken);
            }

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
            SetStatus(config.Revision > management.AppliedRevision
                ? "发现新配置但内容为空"
                : $"已同步 · 修订 {management.AppliedRevision}");
            await ReportAsync(null, cancellationToken);
        }
        catch (Exception ex)
        {
            SetStatus("同步失败：" + ex.Message);
            try
            {
                await ReportAsync(ex.Message, cancellationToken);
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
        management.ClientToken = "";
        management.EnrollmentToken = "";
        management.AppliedRevision = 0;
        management.LastSyncTime = null;
        SetStatus("已断开集控");
    }

    private async Task TickAsync()
    {
        if (!AppServices.Settings.CentralManagement.IsEnabled)
            return;
        await SyncNowAsync();
    }

    private async Task RegisterAsync(CancellationToken cancellationToken)
    {
        var management = AppServices.Settings.CentralManagement;
        if (string.IsNullOrWhiteSpace(management.ServerUrl))
            throw new InvalidOperationException("请填写集控服务地址");
        if (string.IsNullOrWhiteSpace(management.EnrollmentToken))
            throw new InvalidOperationException("请填写客户端注册令牌");

        if (string.IsNullOrWhiteSpace(management.ClientId))
            management.ClientId = Guid.NewGuid().ToString("N");

        SetStatus("正在注册客户端...");
        var result = await _client.RegisterAsync(
            management.ServerUrl,
            new CentralRegistrationRequest(
                management.EnrollmentToken,
                management.ClientId,
                Environment.MachineName,
                Environment.MachineName,
                AppVersion),
            cancellationToken);
        management.ClientId = result.ClientId;
        management.ClientToken = result.ClientToken;
        SetStatus("客户端注册成功");
        await ReportAsync(null, cancellationToken);
    }

    private async Task ReportAsync(string? error, CancellationToken cancellationToken)
    {
        var management = AppServices.Settings.CentralManagement;
        if (string.IsNullOrWhiteSpace(management.ClientToken))
            return;

        var timetable = File.Exists(SettingsService.TimetableStoragePath)
            ? await File.ReadAllTextAsync(SettingsService.TimetableStoragePath, cancellationToken)
            : "";
        await _client.ReportAsync(
            management.ServerUrl,
            management.ClientToken,
            new CentralReportRequest(
                AppVersion,
                Environment.MachineName,
                JsonSerializer.Serialize(AppServices.Settings, JsonOptions),
                timetable,
                error),
            cancellationToken);
    }

    private static void NormalizeServerUrl(CentralManagementSettings management)
    {
        var value = management.ServerUrl.Trim().TrimEnd('/');
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

    private static void SaveManagement()
    {
        SettingsService.Save(AppServices.Settings);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _client.Dispose();
    }
}
