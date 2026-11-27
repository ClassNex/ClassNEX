using ClassNex.Models;

namespace ClassNex.Services;

public sealed partial class CentralManagementService
{
    private readonly SemaphoreSlim _pairGate = new(1, 1);
    private volatile CentralPairingTicket? _ticket;
    private volatile bool _approved;
    private int _connectionGeneration;
    private static bool Paired(CentralManagementSettings s) => s.ClientId.StartsWith("pair-", StringComparison.OrdinalIgnoreCase);

    public async Task RequestPairingAsync()
    {
        var generation = Volatile.Read(ref _connectionGeneration);
        await _pairGate.WaitAsync(_lifetime.Token);
        try
        {
            var s = AppServices.Settings.CentralManagement;
            if (generation != Volatile.Read(ref _connectionGeneration)) return;
            if (s.IsEnabled && !Paired(s) && (_registeredForSession || AccountService.Current?.IsLoggedIn == true || !string.IsNullOrWhiteSpace(s.SelectedClassId)))
            {
                SetStatus("此设备已通过账户连接；更换绑定前请先断开");
                return;
            }
            if (s.IsEnabled && Paired(s))
            {
                _ticket ??= await CentralPairingCredential.LoadAsync();
                if (_ticket != null) { UpdatePolling(); return; }
            }
            var machine = Environment.MachineName;
            var ticket = await _client.StartPairingAsync(AppSettings.DefaultCentralEndpoint, new(machine, machine, AppServices.ProductVersion), _lifetime.Token);
            if (generation != Volatile.Read(ref _connectionGeneration)) { await DiscardTicketAsync(ticket); return; }
            try { await CentralPairingCredential.SaveAsync(ticket); }
            catch { await DiscardTicketAsync(ticket); throw; }
            if (generation != Volatile.Read(ref _connectionGeneration))
            {
                CentralPairingCredential.Forget();
                await DiscardTicketAsync(ticket);
                return;
            }
            _ticket = ticket;
            _approved = false;
            s.ServerUrl = AppSettings.DefaultCentralEndpoint;
            s.ClientId = ticket.ClientId;
            s.LastConflictDraft = null;
            s.IsEnabled = true;
            s.PairingCode = ticket.Code;
            s.SelectedClassId = s.SelectedClassName = "";
            s.AppliedRevision = 0;
            SetStatus($"在集控网页输入配对码，{ticket.ExpiresAt.LocalDateTime:t} 前有效");
            SaveManagement();
            UpdatePolling();
        }
        catch (HttpRequestException ex) when (generation == Volatile.Read(ref _connectionGeneration))
        {
            SetStatus(ex.StatusCode switch
            {
                System.Net.HttpStatusCode.TooManyRequests => "请求过于频繁，请五分钟后再获取配对码",
                System.Net.HttpStatusCode.NotFound => "集控服务暂未提供设备配对，请稍后再试",
                _ => "未能连接集控服务，请检查网络后重试"
            });
        }
        catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested && generation == Volatile.Read(ref _connectionGeneration))
        {
            SetStatus("连接集控服务超时，请重试");
        }
        finally { _pairGate.Release(); }
    }

    private async Task DiscardTicketAsync(CentralPairingTicket ticket)
    {
        try { await _client.RevokePairingAsync(AppSettings.DefaultCentralEndpoint, ticket, _lifetime.Token); }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { }
    }

    private async Task<bool> CheckPairingAsync(CentralManagementSettings s, int generation, CancellationToken cancellation)
    {
        await _pairGate.WaitAsync(cancellation);
        try
        {
            if (!s.IsEnabled || generation != Volatile.Read(ref _connectionGeneration)) return false;
            _ticket ??= await CentralPairingCredential.LoadAsync();
            if (_ticket == null || _ticket.ClientId != s.ClientId) throw new UnauthorizedAccessException();
            if (_approved) return true;
            var status = await _client.PairingStatusAsync(AppSettings.DefaultCentralEndpoint, _ticket, cancellation);
            if (!s.IsEnabled || generation != Volatile.Read(ref _connectionGeneration)) return false;
            if (status.State == "linked")
            {
                _approved = true;
                s.PairingCode = "";
                _registeredForSession = true;
                SetStatus("已绑定，正在同步课堂");
                return true;
            }
            if (status.State != "pending") throw new InvalidDataException("无法识别配对状态");
            s.PairingCode = _ticket.Code;
            SetStatus($"在集控网页输入配对码，{_ticket.ExpiresAt.LocalDateTime:t} 前有效");
            return false;
        }
        finally { _pairGate.Release(); }
    }

    private async Task ExpireBindingAsync(int generation)
    {
        await _pairGate.WaitAsync();
        try
        {
            if (Interlocked.CompareExchange(ref _connectionGeneration, generation + 1, generation) != generation) return;
            ResetConnection(_approved ? "集控端已解除绑定" : "配对码已失效，请重新获取");
        }
        finally { _pairGate.Release(); }
    }

    public async Task DisconnectAsync()
    {
        var generation = Interlocked.Increment(ref _connectionGeneration);
        await _pairGate.WaitAsync();
        try
        {
            if (generation != Volatile.Read(ref _connectionGeneration)) return;
            var old = Paired(AppServices.Settings.CentralManagement) ? _ticket ?? await CentralPairingCredential.LoadAsync() : null;
            ResetConnection("已断开集控");
            if (old != null) await _client.RevokePairingAsync(AppSettings.DefaultCentralEndpoint, old, _lifetime.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            if (generation == Volatile.Read(ref _connectionGeneration))
                SetStatus("本机已断开；服务暂不可用，也可在集控网页解除绑定" + DraftNotice(AppServices.Settings.CentralManagement));
        }
        finally { _pairGate.Release(); }
    }
}
