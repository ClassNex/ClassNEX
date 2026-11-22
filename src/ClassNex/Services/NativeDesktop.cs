using ClassNex.WinUI;
using Microsoft.UI.Dispatching;
using Windows.Storage.Pickers;

namespace ClassNex.Services;

internal sealed partial class NativeDesktop : IDisposable
{
    private readonly DesktopBridge _bridge;
    private readonly DispatcherQueue _queue = DispatcherQueue.GetForCurrentThread();
    private NativeSettingsWindow? _settings;
    private Applied _applied = Applied.Read();
    private bool _disposed, _refreshQueued, _refreshSettings;
    private readonly Views.MainWindow _overlay;
    internal NativeSettingsWindow? Settings => _settings;

    private sealed record Applied(string Theme, bool Startup, bool Protocol, bool Voice, bool Edge, string Side)
    {
        public static Applied Read()
        {
            var s = AppServices.Settings;
            return new(s.ThemeMode, s.RunAtStartup, s.RegisterUrlProtocol, s.IsNexVoiceWakeEnabled, s.IsNexEdgeBarEnabled, s.NexEdgeBarSide);
        }
    }

    internal NativeDesktop(Views.MainWindow overlay)
    {
        _bridge = new()
        {
            ReadSettings = () => AppServices.Settings, SaveSettings = AppServices.SaveSettings, ReadOverlay = ReadOverlay,
            Command = RunCommand, Status = ReadStatus, ExecuteAsync = ExecuteAsync,
            ReadPlugins = () => AppServices.Plugins as Core.Abstractions.IPluginManager
        };
        _overlay = overlay;
    }

    internal void Connect()
    {
        if (AppServices.Settings.IsOobeCompleted) StartupRegistry.RepairPortableRegistrations(_applied.Startup, _applied.Protocol);
        AppServices.SettingsChanged += SettingsChanged;
        AppServices.ManagedSettingsApplied += RemoteChanged;
        AppServices.Schedule.ProfileChanged += ScheduleChanged;
        AppServices.Schedule.Saved += ScheduleChanged;
        AppServices.TimeLayout.Changed += ScheduleChanged;
        AppServices.Widgets.Changed += RemoteChanged;
        AccountService.Changed += RemoteChanged;
        if (AppServices.Plugins is { } plugins) plugins.Changed += RemoteChanged;
        if (AppServices.Management is { } management) management.StatusChanged += StatusChanged;
        if (AppServices.NexVoice is { } voice) voice.StatusChanged += StatusChanged;
    }

    internal void OpenSettings(string page = "home")
    {
        if (_disposed) return;
        if (_settings is null)
        {
            var window = new NativeSettingsWindow(_bridge);
            _settings = window;
            window.Closed += (_, _) => { if (_settings == window) _settings = null; };
        }
        _settings.NavigateTo(page);
        _settings.Activate();
    }

    internal async Task PickTimetableAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".yaml");
        picker.FileTypeFilter.Add(".yml");
        if (_settings is null && !_overlay.IsVisible) OpenSettings("schedule");
        var owner = _settings is { } settings ? WinRT.Interop.WindowNative.GetWindowHandle(settings) : _overlay.TryGetPlatformHandle()?.Handle ?? 0;
        if (owner == 0) return;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, owner);
        if (await picker.PickSingleFileAsync() is { } file) AppServices.LoadTimetable(file.Path);
    }

    private OverlaySnapshot ReadOverlay()
    {
        var now = DateTime.Now;
        var today = AppServices.Day.Read(now);
        return new(now, today, AppServices.Time.GetCountdownText(today.Slots, now.TimeOfDay), AppServices.Schedule.Profile.Schedules.Count > 0);
    }
}
