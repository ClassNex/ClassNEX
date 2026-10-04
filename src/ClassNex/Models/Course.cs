namespace ClassNex.Models;

/// <summary>一节课（白皮书 Models/Course.cs），对应 CSES 的 class 节点。</summary>
public sealed class Course
{
    /// <summary>科目名，对应 subjects 中某个 Subject.Name。</summary>
    public string Subject { get; set; } = "";

    /// <summary>开始时间 HH:MM:SS。</summary>
    public string StartTime { get; set; } = "08:00:00";

    /// <summary>结束时间 HH:MM:SS。</summary>
    public string EndTime { get; set; } = "08:45:00";

    public Course Clone() => new() { Subject = Subject, StartTime = StartTime, EndTime = EndTime };
}
