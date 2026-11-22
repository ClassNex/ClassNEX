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
    internal NativeOverlayWindow Overlay { get; }

    private sealed record Applied(string Theme, bool Startup, bool Protocol, bool Voice, bool Edge, string Side)
    {
        public static Applied Read()
        {
            var s = AppServices.Settings;
            return new(s.ThemeMode, s.RunAtStartup, s.RegisterUrlProtocol, s.IsNexVoiceWakeEnabled, s.IsNexEdgeBarEnabled, s.NexEdgeBarSide);
        }
    }

    internal NativeDesktop()
    {
        _bridge = new()
        {
            ReadSettings = () => AppServices.Settings, SaveSettings = AppServices.SaveSettings, ReadOverlay = ReadOverlay,
            Command = RunCommand, Status = ReadStatus, ExecuteAsync = ExecuteAsync
        };
        Overlay = new(_bridge);
    }

    internal void Connect()
    {
        AppServices.SettingsChanged += SettingsChanged;
        AppServices.ManagedSettingsApplied += RemoteChanged;
        AppServices.Schedule.ProfileChanged += ScheduleChanged;
        AppServices.Schedule.Saved += ScheduleChanged;
        AppServices.TimeLayout.Changed += ScheduleChanged;
        AppServices.Widgets.Changed += RemoteChanged;
        AccountService.Changed += RemoteChanged;
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
        var owner = (Microsoft.UI.Xaml.Window?)_settings ?? Overlay;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
        if (await picker.PickSingleFileAsync() is { } file) AppServices.LoadTimetable(file.Path);
    }

    private OverlaySnapshot ReadOverlay()
    {
        var now = DateTime.Now;
        var today = AppServices.Day.Read(now);
        return new(now, today, AppServices.Time.GetCountdownText(today.Slots, now.TimeOfDay), AppServices.Schedule.Profile.Schedules.Count > 0);
    }
}
