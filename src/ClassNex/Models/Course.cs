namespace ClassNex.Models;


public sealed class Course
{
    
    public string Subject { get; set; } = "";

    
    public string StartTime { get; set; } = "08:00:00";

    
    public string EndTime { get; set; } = "08:45:00";

    public Course Clone() => new() { Subject = Subject, StartTime = StartTime, EndTime = EndTime };
}
