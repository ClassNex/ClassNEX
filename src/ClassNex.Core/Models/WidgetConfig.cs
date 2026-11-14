namespace ClassNex.Models;







public sealed class WidgetConfig
{
    
    public string Type { get; set; } = "";

    
    public bool IsEnabled { get; set; } = true;

    
    public int Order { get; set; }

    
    public double FontScale { get; set; } = 1.0;

    

    
    public string? Text { get; set; }

    
    public bool UseCustomFontColor { get; set; }

    
    public string? FontColor { get; set; }

    

    
    public bool ShowSeconds { get; set; }

    
    public bool ShowRealTime { get; set; } = true;

    
    public bool FlashTimeSeparator { get; set; }

    

    
    public bool FadeCompletedClasses { get; set; } = true;

    
    public double ScheduleSpacing { get; set; } = 1.0;

    

    
    public bool ShowNextClass { get; set; } = true;

    

    
    public string? CountdownName { get; set; }

    
    public DateTimeOffset? CountdownTarget { get; set; }

    
    public bool UseCountdownAccent { get; set; } = true;
}
