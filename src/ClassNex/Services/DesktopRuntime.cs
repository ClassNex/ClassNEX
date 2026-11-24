using ClassNex.Core.Abstractions;
using ClassNex.Models;
using ClassNex.Views;

namespace ClassNex.Services;

internal sealed class DesktopRuntime : IDisposable
{
    private readonly PluginService _plugins;
    private readonly CentralManagementService _management = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TopmostEffectWindow _effects = new();
    private readonly MainWindow _overlay;
    private readonly ReminderSound _reminderSound;
    private bool _started;
    private bool _disposed;
    public NotificationService Notifications { get; }

    public DesktopRuntime(MainWindow overlay, ISettingsService store, IScheduleService schedule, ITimeService time,
        ITimeLayoutService layout, IWidgetService widgets, IAiService ai, Func<AppSettings> settings)
    {
        _overlay = overlay;
        if (overlay.Screens.Primary is { } screen) _effects.UpdateWindowPos(screen, 1 / screen.Scaling, false);
        _reminderSound = new ReminderSound(settings);
        Notifications = new NotificationService(new MainWindowNotificationSurface(overlay, _effects, _reminderSound), settings, time, schedule, AppServices.ClassroomClock);
        var api = new PluginServiceProvider();
        api.Add(schedule); api.Add(time); api.Add(layout); api.Add(widgets); api.Add(ai); api.Add(store);
        api.Add<INotificationService>(Notifications);
        api.Add<INotificationPublisher>(Notifications);
        _plugins = new PluginService(Path.Combine(AppContext.BaseDirectory, "plugins"), api, Notifications, ai,
            Path.Combine(SettingsService.DataDirectory, "PluginSettings"));
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;
        _started = true;
        AppServices.MainWindow = _overlay;
        AppServices.NotificationService = Notifications;
        AppServices.Management = _management;
        AppServices.Plugins = _plugins;
        AppServices.SettingsChanged += Notifications.RefreshSchedule;
        _management.Start();
        Notifications.Start();
        _ = LoadPluginsAsync(_lifetime.Token);
    }

    private async Task LoadPluginsAsync(CancellationToken token)
    {
        try { await _plugins.LoadAllAsync(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError("Plugin discovery: {0}", ex.Message); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AppServices.SettingsChanged -= Notifications.RefreshSchedule;
        _lifetime.Cancel();
        _plugins.StopAll();
        _management.Dispose();
        Notifications.Dispose();
        _reminderSound.Dispose();
        _effects.Close();
        _lifetime.Dispose();
        AppServices.Management = null; AppServices.Plugins = null; AppServices.NotificationService = null; AppServices.MainWindow = null;
    }
}
