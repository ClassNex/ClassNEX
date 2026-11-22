namespace ClassNex.Models;

/// <summary>CSES 中针对一个星期几和周次条件的一组授课记录。</summary>
public sealed class Schedule
{
    /// <summary>档案中为这组课程保存的名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>适用的星期几，从周一 1 到周日 7。</summary>
    public int EnableDay { get; set; } = 1;

    /// <summary>适用周次：all、odd 或 even。</summary>
    public string Weeks { get; set; } = "all";

    /// <summary>属于此计划的授课记录。</summary>
    public List<Course> Classes { get; set; } = new();
}
