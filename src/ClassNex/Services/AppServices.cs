using ClassNex.Models;

namespace ClassNex.Services;





public static class AppServices
{
    public static AppSettings Settings { get; private set; } = new();

    public static IScheduleService Schedule { get; private set; } = null!;

    public static ITimeService Time { get; private set; } = null!;

    public static ITimeLayoutService TimeLayout { get; private set; } = null!;

    public static IWidgetService Widgets { get; private set; } = null!;

    
    public static Views.MainWindow? MainWindow { get; set; }

    
    public static NotificationService? NotificationService { get; set; }

    
    public static NexVoiceCoordinator? NexVoice { get; set; }

    
    public static event Action? SettingsChanged;

    
    private const int SampleSeedVersion = 3;

    public static void Initialize()
    {
        SettingsService.EnsureDataDirectory();
        Settings = SettingsService.Load();

        if (Settings.Widgets.Count == 0)
            Settings.Widgets = AppSettings.CreateDefaultWidgets();

        
        if (Math.Abs(Settings.BackgroundOpacity - 0.55) < 0.001)
            Settings.BackgroundOpacity = Styles.CiPalette.CardOpacity;

        
        
        
        if (Settings.SampleSeedVersion > 0 && Settings.SampleSeedVersion < SampleSeedVersion)
        {
            if (string.IsNullOrWhiteSpace(Settings.TimetablePath))
            {
                try
                {
                    var sample = CsesCodec.SamplePath;
                    if (File.Exists(sample))
                        File.Copy(sample, SettingsService.TimetableStoragePath, true);

                    Settings.TimeLayout = new TimeLayout();
                }
                catch
                {
                    
                }
            }

            Settings.SampleSeedVersion = SampleSeedVersion;
            SettingsService.Save(Settings);
        }

        Time = new TimeService(() => Settings);
        Schedule = new ScheduleService(() => Settings);
        TimeLayout = new TimeLayoutService(() => Settings, Schedule);
        Widgets = new WidgetService(() => Settings);

        
        Schedule.ProfileChanged += () => TimeLayout.EnsureFromProfile(Schedule.Profile);

        Schedule.Load(ResolveTimetablePath());
        TimeLayout.EnsureFromProfile(Schedule.Profile);
    }

    
    public static void LoadTimetable(string path)
    {
        Settings.TimetablePath = path;
        SettingsService.Save(Settings);
        Schedule.Load(path);
    }

    
    public static void ReloadTimetable()
    {
        Schedule.Reload();
        TimeLayout.EnsureFromProfile(Schedule.Profile);
    }

    public static void SaveSettings()
    {
        SettingsService.Save(Settings);
        SettingsChanged?.Invoke();
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
