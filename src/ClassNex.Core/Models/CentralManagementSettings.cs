namespace ClassNex.Models;

public sealed class CentralManagementSettings
{
    public bool IsEnabled { get; set; }

    public string ServerUrl { get; set; } = "";

    public string EnrollmentToken { get; set; } = "";

    public string ClientId { get; set; } = "";

    public string ClientToken { get; set; } = "";

    public long AppliedRevision { get; set; }

    public DateTime? LastSyncTime { get; set; }

    public string LastStatus { get; set; } = "未配置";
}
