namespace ClassNex.Models;

public sealed class CentralManagementSettings
{
    public bool IsEnabled { get; set; }

    public string ServerUrl { get; set; } = AppSettings.DefaultCentralEndpoint;

    public string ClientId { get; set; } = "";

    public string SelectedClassId { get; set; } = "";

    public string SelectedClassName { get; set; } = "";

    public long AppliedRevision { get; set; }

    public DateTime? LastSyncTime { get; set; }

    public string LastStatus { get; set; } = "未配置";
}
