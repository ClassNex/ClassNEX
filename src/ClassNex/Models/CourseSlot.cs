namespace ClassNex.Models;


public sealed record CourseSlot(
    string Subject,
    string DisplayName,
    string? Teacher,
    string? Room,
    TimeSpan Start,
    TimeSpan End)
{
    public string StartText => Start.ToString(@"hh\:mm");

    public string EndText => End.ToString(@"hh\:mm");

    public string TimeRange => $"{StartText}–{EndText}";

    
    public string Detail
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Teacher))
                parts.Add(Teacher!);
            if (!string.IsNullOrWhiteSpace(Room))
                parts.Add(Room!);
            return string.Join(" · ", parts);
        }
    }

    
    public bool Contains(TimeSpan time) => Start <= time && time < End;
}
