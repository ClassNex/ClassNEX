namespace ClassNex.Services;

internal sealed partial class NativeDesktop
{
    private void ScheduleChanged() => Enqueue(() => QueueRefresh(true));
    private void SettingsChanged() => Enqueue(() => QueueRefresh(false));
    private void RemoteChanged() => Enqueue(() => QueueRefresh(true));
    private void StatusChanged(string _) => Enqueue(() =>
    {
        if (_settings?.CurrentPageKey is "basic" or "nex" or "home") _settings.RefreshSettings();
    });

    internal void Enqueue(Action action)
    {
        if (_disposed) return;
        if (_queue.HasThreadAccess) action();
        else _queue.TryEnqueue(() => { if (!_disposed) action(); });
    }

    private void QueueRefresh(bool controls)
    {
        _refreshSettings |= controls;
        if (_refreshQueued) return;
        _refreshQueued = true;
        _queue.TryEnqueue(() =>
        {
            _refreshQueued = false;
            if (_disposed) return;
            var refresh = _refreshSettings;
            _refreshSettings = false;
            var previous = _applied;
            _applied = Applied.Read();
            if (previous.Theme != _applied.Theme) App.ApplyTheme(_applied.Theme);
            if (previous.Startup != _applied.Startup) StartupRegistry.SetRunAtStartup(_applied.Startup);
            if (previous.Protocol != _applied.Protocol) StartupRegistry.SetUrlProtocol(_applied.Protocol);
            if (previous.Voice != _applied.Voice)
            {
                if (_applied.Voice) AppServices.NexVoice?.Start(); else AppServices.NexVoice?.Stop();
            }
            if (previous.Edge != _applied.Edge || previous.Side != _applied.Side) AppServices.NexVoice?.RefreshEdgeBar();
            var show = AppServices.Settings.IsOobeCompleted && AppServices.Settings.IsMainWindowVisible;
            if (show != Overlay.IsAvailable)
            {
                if (show) Overlay.ShowOverlay(); else Overlay.HideOverlay();
            }
            if (show) Overlay.RefreshOverlay();
            if (refresh || previous.Theme != _applied.Theme) _settings?.RefreshSettings();
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AppServices.SettingsChanged -= SettingsChanged;
        AppServices.ManagedSettingsApplied -= RemoteChanged;
        AppServices.Schedule.ProfileChanged -= ScheduleChanged;
        AppServices.Schedule.Saved -= ScheduleChanged;
        AppServices.TimeLayout.Changed -= ScheduleChanged;
        AppServices.Widgets.Changed -= RemoteChanged;
        AccountService.Changed -= RemoteChanged;
        if (AppServices.Management is { } management) management.StatusChanged -= StatusChanged;
        if (AppServices.NexVoice is { } voice) voice.StatusChanged -= StatusChanged;
        _settings?.Close();
        Overlay.Close();
    }
}
