using YamlDotNet.Serialization;

namespace ClassNex.Models;

/// <summary>科目 / 课程（白皮书 Models/Subject.cs）。</summary>
public sealed class Subject
{
    /// <summary>课程名（CSES: subject.name）。</summary>
    public string Name { get; set; } = "";

    /// <summary>简称（CSES: subject.simplified_name）。</summary>
    public string? SimplifiedName { get; set; }

    /// <summary>任课教师（CSES: subject.teacher）。</summary>
    public string? Teacher { get; set; }

    /// <summary>上课教室（CSES: subject.room）。</summary>
    public string? Room { get; set; }

    /// <summary>显示颜色（界面用，不写入 CSES）。</summary>
    [YamlIgnore]
    public string? Color { get; set; }

    /// <summary>列表中显示的名称（优先简称）。</summary>
    [YamlIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(SimplifiedName) ? Name : SimplifiedName!;
}
