namespace ClassNex.Models;

/// <summary>CSES 档案中的一节课；科目详细资料由所属档案单独保存。</summary>
public sealed class Course
{
    /// <summary>引用 <see cref="Models.Subject.Name"/> 的科目名。</summary>
    public string Subject { get; set; } = "";

    /// <summary>CSES 课程开始时刻，通常为 HH:mm:ss。</summary>
    public string StartTime { get; set; } = "08:00:00";

    /// <summary>CSES 课程结束时刻，通常为 HH:mm:ss。</summary>
    public string EndTime { get; set; } = "08:45:00";

    /// <summary>复制这一条授课记录；修改副本不会改变原记录。</summary>
    public Course Clone() => new() { Subject = Subject, StartTime = StartTime, EndTime = EndTime };
}
