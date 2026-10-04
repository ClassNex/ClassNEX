namespace ClassNex.Models;

/// <summary>CSES 课程（科目）对象。</summary>
public sealed class CsesSubject
{
    /// <summary>课程名（必填）。</summary>
    public string Name { get; set; } = "";

    /// <summary>课程简称（可选，适合紧凑展示）。</summary>
    public string? SimplifiedName { get; set; }

    /// <summary>任课教师（可选）。</summary>
    public string? Teacher { get; set; }

    /// <summary>上课教室（可选）。</summary>
    public string? Room { get; set; }
}

/// <summary>CSES 单节课（由课程名 + 起止时间组成）。</summary>
public sealed class CsesClass
{
    public string Subject { get; set; } = "";
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
}

/// <summary>CSES 某一天的课程表。</summary>
public sealed class CsesSchedule
{
    public string Name { get; set; } = "";

    /// <summary>启用日，1-7 表示周一到周日。</summary>
    public int EnableDay { get; set; }

    /// <summary>周数：all / odd / even。</summary>
    public string Weeks { get; set; } = "all";

    public List<CsesClass> Classes { get; set; } = new();
}

/// <summary>CSES 文档（整个课表文件）。</summary>
public sealed class CsesDocument
{
    public int Version { get; set; } = 1;
    public List<CsesSubject> Subjects { get; set; } = new();
    public List<CsesSchedule> Schedules { get; set; } = new();

    public CsesSubject? FindSubject(string name) =>
        Subjects.FirstOrDefault(s => s.Name == name);
}
