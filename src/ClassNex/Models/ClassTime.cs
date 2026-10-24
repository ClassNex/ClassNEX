namespace ClassNex.Models;


public enum ClassTimeKind
{
    Class = 0,
    Break = 1,
    Separator = 2,
    Action = 3,
}


public sealed class ClassTime
{
    
    public string Name { get; set; } = "";

    
    public string Start { get; set; } = "08:00";

    
    public string End { get; set; } = "08:45";

    public ClassTimeKind Kind { get; set; } = ClassTimeKind.Class;

    public string Display => $"{Name}  {Start}–{End}";

    
    public static string ToCsesTime(string shortTime) =>
        shortTime.Length >= 8 ? shortTime : (shortTime.Length == 5 ? shortTime + ":00" : shortTime);

    
    public static string ToShortTime(string csesTime) =>
        csesTime.Length >= 5 ? csesTime[..5] : csesTime;

    
    public static bool SameTime(string a, string b) =>
        TimeSpan.TryParse(a, out var ta) && TimeSpan.TryParse(b, out var tb)
            ? ta.Hours == tb.Hours && ta.Minutes == tb.Minutes
            : string.Equals(a, b, StringComparison.Ordinal);
}
