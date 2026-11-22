namespace ClassNex.Models;

/// <summary>设备向集控服务登记身份时提交的资料；访问令牌单独放在请求头。</summary>
/// <param name="ClientId">本地安装生成的设备标识，不是 OAuth 客户端 ID。</param>
/// <param name="DisplayName">管理端向用户显示的设备名称。</param>
/// <param name="MachineName">客户端上报的主机名。</param>
/// <param name="AppVersion">该设备报告的 ClassNEX 版本。</param>
public sealed record CentralDeviceRegistrationRequest(
    string ClientId,
    string DisplayName,
    string MachineName,
    string AppVersion);

/// <summary>下发给设备的一次完整班级修订，设置和课表属于同一版本。</summary>
/// <param name="ClassId">班级的稳定标识。</param>
/// <param name="ClassName">供用户识别班级的显示名。</param>
/// <param name="Revision">班级配置修订号，用于识别同一版本及后续变更。</param>
/// <param name="SettingsJson">可同步的课堂设置 JSON，应排除令牌、本机密钥和路径。</param>
/// <param name="TimetableYaml">CSES 格式的课表文本。</param>
/// <param name="Message">本次班级配置的附带消息。</param>
/// <param name="UpdatedAt">服务端记录的该修订更新时间。</param>
public sealed record CentralConfigEnvelope(
    string ClassId,
    string ClassName,
    long Revision,
    string SettingsJson,
    string TimetableYaml,
    string Message,
    DateTime UpdatedAt);

/// <summary>设备上报最近同步状态时提交的本机版本和课堂配置。</summary>
/// <param name="ClientId">本地安装生成的设备标识，不是 OAuth 客户端 ID。</param>
/// <param name="AppVersion">该设备报告的 ClassNEX 版本。</param>
/// <param name="MachineName">客户端上报的主机名。</param>
/// <param name="SettingsJson">可同步的课堂设置 JSON，应排除令牌、本机密钥和路径。</param>
/// <param name="TimetableYaml">CSES 格式的课表文本。</param>
/// <param name="Error">应用配置失败时的简短原因；成功时可为 null。</param>
public sealed record CentralReportRequest(
    string ClientId,
    string AppVersion,
    string MachineName,
    string SettingsJson,
    string TimetableYaml,
    string? Error);

/// <summary>班级选择列表所需的摘要，无需同时获取整个课表。</summary>
/// <param name="ClassId">班级的稳定标识。</param>
/// <param name="Name">管理端显示的班级名称。</param>
/// <param name="Revision">班级配置修订号，用于识别同一版本及后续变更。</param>
/// <param name="DeviceCount">已关联的设备数量。</param>
/// <param name="UpdatedAt">服务端记录的该修订更新时间。</param>
/// <param name="Message">本次班级配置的附带消息。</param>
public sealed record CentralClassSummary(
    string ClassId,
    string Name,
    long Revision,
    int DeviceCount,
    DateTime UpdatedAt,
    string Message);

/// <summary>管理端创建班级时传递的显示名；标识由服务端分配。</summary>
/// <param name="Name">新班级使用的可读名称。</param>
public sealed record CentralCreateClassRequest(string Name);

/// <summary>供集控设备选择器显示的归属和最近通信情况。</summary>
/// <param name="ClientId">本地安装生成的设备标识，不是 OAuth 客户端 ID。</param>
/// <param name="DisplayName">管理端向用户显示的设备名称。</param>
/// <param name="MachineName">客户端上报的主机名。</param>
/// <param name="AppVersion">该设备报告的 ClassNEX 版本。</param>
/// <param name="ClassId">班级的稳定标识。</param>
/// <param name="ClassName">供用户识别班级的显示名。</param>
/// <param name="LastSeen">最近一次联系时间；未联系过时为空。</param>
/// <param name="IsOnline">服务端按最近联系情况推断的在线标记。</param>
/// <param name="LastError">设备最近上报的同步错误，无错误时可为空字符串。</param>
public sealed record CentralDeviceSummary(
    string ClientId,
    string DisplayName,
    string MachineName,
    string AppVersion,
    string ClassId,
    string ClassName,
    DateTime? LastSeen,
    bool IsOnline,
    string LastError);

/// <summary>在集控端调整设备归属时指定的目标班级。</summary>
/// <param name="ClassId">班级的稳定标识。</param>
public sealed record CentralAssignDeviceRequest(string ClassId);

/// <summary>将本机课堂设置与课表发布给所属班级的请求。</summary>
/// <param name="ClientId">本地安装生成的设备标识，不是 OAuth 客户端 ID。</param>
/// <param name="SettingsJson">可同步的课堂设置 JSON，应排除令牌、本机密钥和路径。</param>
/// <param name="TimetableYaml">CSES 格式的课表文本。</param>
public sealed record CentralClientPublishRequest(
    string ClientId,
    string SettingsJson,
    string TimetableYaml);

/// <summary>发布后返回的班级修订标识，不重复携带完整配置。</summary>
/// <param name="ClassId">班级的稳定标识。</param>
/// <param name="ClassName">供用户识别班级的显示名。</param>
/// <param name="Revision">班级配置修订号，用于识别同一版本及后续变更。</param>
/// <param name="UpdatedAt">服务端记录的该修订更新时间。</param>
public sealed record CentralClassRevision(
    string ClassId,
    string ClassName,
    long Revision,
    DateTime UpdatedAt);
