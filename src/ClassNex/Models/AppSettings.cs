namespace ClassNex.Models;


public enum LayoutOrientation
{
    Horizontal = 0,
    Vertical = 1,
}


public sealed class AppSettings
{
    
    
    public string? TimetablePath { get; set; }

    
    public DateTime SingleWeekStartTime { get; set; } = new(DateTime.Today.Year, 1, 1);

    
    public int WeekRotationOffset { get; set; }

    
    public bool IsMainWindowVisible { get; set; } = true;

    public double MainWindowLeft { get; set; } = 80;

    public double MainWindowTop { get; set; } = 80;

    
    public double BackgroundOpacity { get; set; } = 0.5;

    
    public bool Topmost { get; set; } = true;

    
    public double FontScale { get; set; } = 1.0;

    
    
    
    
    public double MainWindowScale { get; set; } = 1.9;

    
    public double EffectiveScale => MainWindowScale * FontScale;

    
    public bool IsClickThrough { get; set; } = true;

    
    public double HoverOpacity { get; set; } = 0.05;

    
    public LayoutOrientation Orientation { get; set; } = LayoutOrientation.Horizontal;

    public int DefaultOnClassTimePointMinutes { get; set; } = 40;

    public int DefaultBreakingTimePointMinutes { get; set; } = 10;

    
    
    public List<WidgetConfig> Widgets { get; set; } = new();

    
    
    public TimeLayout TimeLayout { get; set; } = new();

    
    
    public string ThemeMode { get; set; } = "system";

    
    
    public int TrayClickBehavior { get; set; }

    
    
    public string UserName { get; set; } = "LingOfficial";

    
    public string Email { get; set; } = "Lingofficial0423@gamil";

    public string AccountEndpoint { get; set; } = "http://124.221.160.188:8000";

    public string AccountClientId { get; set; } = "fa7664d772fac4b5097ce15c";

    
    
    public bool AllowNotification { get; set; } = true;

    
    public bool AllowNotificationEffect { get; set; } = true;

    
    public bool AllowMinorNotification { get; set; } = true;

    
    public NotificationSettings Notification { get; set; } = new();

    

    
    public bool RunAtStartup { get; set; }

    
    public bool RegisterUrlProtocol { get; set; }

    
    public bool TeachingSafeMode { get; set; }

    
    public int CrashHandlingMode { get; set; }

    
    public bool ShowStartupSplash { get; set; } = true;

    
    public bool IsDebugOptionsEnabled { get; set; }

    
    public int SampleSeedVersion { get; set; }

    
    public static List<WidgetConfig> CreateDefaultWidgets() => new()
    {
        new WidgetConfig { Type = "date", IsEnabled = true, Order = 0 },
        new WidgetConfig { Type = "schedule", IsEnabled = true, Order = 1 },
    };

    

    
    public bool IsOobeCompleted { get; set; }

    
    public string Language { get; set; } = "zh-CN";

    
    public DateTime? SemesterStartDate { get; set; }

    

    
    public bool IsNexIntelligenceEnabled { get; set; } = true;

    
    public string NexApiBaseUrl { get; set; } = "https://cyberspace.furruka.asia";

    
    
    
    
    public string NexApiKey { get; set; } = "";

    
    public string NexModel { get; set; } = "z-ai/glm-5.3";

    
    public string NexPermission { get; set; } = "readonly";

    
    public string NexSystemPrompt { get; set; } = "";

    
    public bool IsNexVoiceWakeEnabled { get; set; } = true;

    
    public bool IsNexEdgeBarEnabled { get; set; } = true;

    
    public string NexEdgeBarSide { get; set; } = "right";
}





public sealed class NotificationSettings
{
    public bool IsClassOnNotificationEnabled { get; set; } = true;

    public bool IsClassOnPreparingNotificationEnabled { get; set; } = true;

    public bool IsClassOffNotificationEnabled { get; set; } = true;

    public int InDoorClassPreparingDeltaTime { get; set; } = 60;

    public int OutDoorClassPreparingDeltaTime { get; set; } = 600;

    public string ClassOnPreparingText { get; set; } = "准备上课，请回到座位并保持安静，做好上课准备。";

    public string ClassOnPreparingMaskText { get; set; } = "即将上课";

    public string OutdoorClassOnPreparingMaskText { get; set; } = "即将上课";

    public string OutdoorClassOnPreparingText { get; set; } = "下节课程为户外课程，请合理规划时间，做好上课准备。";

    public string ClassOnMaskText { get; set; } = "上课";

    public string ClassOffMaskText { get; set; } = "课间休息";

    public string ClassOffOverlayText { get; set; } = "";

    public bool IsSpeechEnabledOnClassPreparing { get; set; } = true;

    public bool IsSpeechEnabledOnClassOn { get; set; } = true;

    public bool IsSpeechEnabledOnClassOff { get; set; } = true;

    public bool ShowTeacherName { get; set; }
}
