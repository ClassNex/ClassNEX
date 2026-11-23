using YamlDotNet.Serialization;

namespace ClassNex.Models;

/// <summary>课表中可复用的科目资料，由课程通过完整名称引用。</summary>
public sealed class Subject
{
    /// <summary>在 CSES 文件中引用本学科的完整名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>可选简称，空间有限时用于显示。</summary>
    public string? SimplifiedName { get; set; }

    /// <summary>可选授课教师。</summary>
    public string? Teacher { get; set; }

    /// <summary>可选教室或上课地点。</summary>
    public string? Room { get; set; }

    /// <summary>户外课使用独立准备提前量。工作档案和集控保留该扩展；标准 CSES 导出省略。</summary>
    [YamlMember(Alias = "x_classnex_outdoor", ApplyNamingConventions = false, DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool IsOutDoor { get; set; }

    /// <summary>可选的本地科目颜色，不属于 CSES 字段。</summary>
    [YamlIgnore]
    public string? Color { get; set; }

    /// <summary>优先使用非空简称，否则返回完整名称。</summary>
    [YamlIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(SimplifiedName) ? Name : SimplifiedName!;
}
