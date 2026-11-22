namespace ClassNex.Models;

/// <summary>单设备集控状态；应用远端设置时须保留该设备原有的绑定信息。</summary>
public sealed class CentralManagementSettings
{
    /// <summary>是否参与当前设备的集控同步。</summary>
    public bool IsEnabled { get; set; }

    /// <summary>集控 API 所在地址，保留服务子路径。</summary>
    public string ServerUrl { get; set; } = AppSettings.DefaultCentralEndpoint;

    /// <summary>区别于账户 ID 的设备实例标识。</summary>
    public string ClientId { get; set; } = "";

    /// <summary>该设备上次获分配的班级标识。</summary>
    public string SelectedClassId { get; set; } = "";

    /// <summary>最近一次同步得到的班级显示名。</summary>
    public string SelectedClassName { get; set; } = "";

    /// <summary>本机已应用的班级配置修订号。</summary>
    public long AppliedRevision { get; set; }

    /// <summary>最近完成同步的时间，未同步时为空。</summary>
    public DateTime? LastSyncTime { get; set; }

    /// <summary>向设备用户显示的最近同步结果。</summary>
    public string LastStatus { get; set; } = "未配置";
}
