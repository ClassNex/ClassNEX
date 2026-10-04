namespace ClassNex.Models;

/// <summary>一天的课表（白皮书 Models/Schedule.cs），对应 CSES 的 schedule 节点。</summary>
public sealed class Schedule
{
    public string Name { get; set; } = "";

    /// <summary>启用日：1=周一 ... 7=周日。</summary>
    public int EnableDay { get; set; } = 1;

    /// <summary>周数：all / odd / even。</summary>
    public string Weeks { get; set; } = "all";

    public List<Course> Classes { get; set; } = new();
}
