using ClassNex.Models;
using ClassNex.Core.Abstractions;
using System.Reflection;
using System.Text;

namespace ClassNex.Services;





public static class AppServices
{
    internal static string ProductVersion { get; } = typeof(AppServices).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
    public static ISettingsService SettingsStore => SettingsService.Store;
    internal static Views.MainWindow? MainWindow { get; set; }
    private static bool _applyingRevision;
    public static AppSettings Settings { get; private set; } = new();

    public static IScheduleService Schedule { get; private set; } = null!;

    public static ITimeService Time { get; private set; } = null!;
    internal static DayScheduleCache Day { get; private set; } = null!;

    public static ITimeLayoutService TimeLayout { get; private set; } = null!;

    public static IWidgetService Widgets { get; private set; } = null!;

    public static IAiService Ai { get; private set; } = null!;

    public static IPluginService? Plugins { get; set; }

    
    public static INotificationService? NotificationService { get; set; }

    
    public static NexVoiceCoordinator? NexVoice { get; set; }

    public static CentralManagementService? Management { get; set; }

    
    public static event Action? SettingsChanged;

    public static event Action? LocalSettingsChanged;

    public static event Action? ManagedSettingsApplied;

    
    private const int SampleSeedVersion = 3;

    public static void Initialize()
    {
        SettingsService.EnsureDataDirectory();
        Settings = SettingsService.Load();
        AccountService.Load();

        if (Settings.SampleSeedVersion > 0 && Settings.SampleSeedVersion < SampleSeedVersion)
        {
            Settings.SampleSeedVersion = SampleSeedVersion;
            SettingsService.Save(Settings);
        }

        Time = new TimeService(() => Settings);
        Schedule = new ScheduleService(() => Settings);
        Day = new(Time, Schedule);
        TimeLayout = new TimeLayoutService(() => Settings, Schedule, SaveSettings);
        Widgets = new WidgetService(() => Settings, SaveSettings);
        Ai = new NexAiService(new AppServicesAiContext(() => Settings, Schedule, Time, SettingsService.DataDirectory));

        
        Schedule.ProfileChanged += () => TimeLayout.EnsureFromProfile(Schedule.Profile);

        Schedule.Load(ResolveTimetablePath());
    }

    
    public static void LoadTimetable(string path)
    {
        Settings.TimetablePath = path;
        Schedule.Load(path);
        SaveSettings();
    }

    
    public static void ReloadTimetable()
    {
        Schedule.Reload();
    }

    public static void SaveSettings()
    {
        if (_applyingRevision) return;
        SettingsStore.Save(Settings);
        SettingsChanged?.Invoke();
        LocalSettingsChanged?.Invoke();
    }

    public static void ApplyManagedSettings(AppSettings remote, string? timetableYaml)
    {
        var local = Settings;
        remote.Widgets ??= AppSettings.CreateDefaultWidgets();
        remote.Notification ??= new NotificationSettings();
        remote.TimeLayout ??= new TimeLayout();
        CentralSettingsPayload.PreserveLocal(remote, local);
        remote.BackgroundOpacity = Math.Clamp(remote.BackgroundOpacity, 0.1, 1);
        remote.FontScale = Math.Clamp(remote.FontScale, 0.8, 2.4);
        remote.MainWindowScale = Math.Clamp(remote.MainWindowScale, 0.5, 4);

        var reload = false;
        if (!string.IsNullOrWhiteSpace(timetableYaml))
        {
            var target = SettingsService.TimetableStoragePath;
            reload = !File.Exists(target) || !string.Equals(File.ReadAllText(target), timetableYaml, StringComparison.Ordinal);
            if (reload)
            {
                _ = CsesCodec.Parse(timetableYaml);
                var temporary = Path.Combine(SettingsService.DataDirectory, $".timetable-{Guid.NewGuid():N}.tmp");
                try { File.WriteAllText(temporary, timetableYaml, new UTF8Encoding(false)); File.Move(temporary, target, true); }
                finally { File.Delete(temporary); }
            }
            reload |= !string.Equals(Schedule.FilePath, target, StringComparison.OrdinalIgnoreCase);
            remote.TimetablePath = target;
        }

        _applyingRevision = true;
        try
        {
            Settings = remote;
            if (reload) Schedule.Load(ResolveTimetablePath());
            else TimeLayout.EnsureFromProfile(Schedule.Profile);
            SettingsStore.Save(Settings);
        }
        finally { _applyingRevision = false; }
        SettingsChanged?.Invoke();
        ManagedSettingsApplied?.Invoke();
    }

    public static string TimetableFileName => Schedule.FileName;

    public static string TimetablePath => Schedule.FilePath;

    
    private static string ResolveTimetablePath()
    {
        var storage = SettingsService.TimetableStoragePath;
        if (!File.Exists(storage))
        {
            var sample = CsesCodec.SamplePath;
            if (File.Exists(sample))
            {
                try
                {
                    File.Copy(sample, storage, true);
                }
                catch
                {
                    
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(Settings.TimetablePath) &&
            File.Exists(Settings.TimetablePath))
        {
            return Settings.TimetablePath!;
        }

        return storage;
    }
}
