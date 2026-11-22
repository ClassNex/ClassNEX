namespace ClassNex.Models;

/// <summary>排列桌面组件的主轴，数值与 Alpha 设置文件兼容。</summary>
public enum LayoutOrientation
{
    /// <summary>沿桌面横向排列。</summary>
    Horizontal = 0,
    /// <summary>沿桌面纵向排列。</summary>
    Vertical = 1,
}

/// <summary>便携安装的设置文件模型；需要跨设备传输时请使用 <see cref="CentralSettingsPayload"/> 过滤本机数据。</summary>
public sealed class AppSettings
{
    /// <summary>新安装使用的 ClassNEX ID 服务根地址。</summary>
    public const string DefaultAccountEndpoint = "https://classnex.tech";

    /// <summary>新安装使用的集控服务地址，包含应用子路径。</summary>
    public const string DefaultCentralEndpoint = "https://classnex.tech/Control";

    /// <summary>当前档案文件的路径；未选择档案时可以为空。</summary>
    public string? TimetablePath { get; set; }

    /// <summary>计算单双周的本地单周起始日期。</summary>
    public DateTime SingleWeekStartTime { get; set; } = new(DateTime.Today.Year, 1, 1);

    /// <summary>叠加在起始日期计算结果上的周数偏移。</summary>
    public int WeekRotationOffset { get; set; }

    /// <summary>桌面课表浮窗的上次可见状态。</summary>
    public bool IsMainWindowVisible { get; set; } = true;

    /// <summary>旧设置文件保存的浮窗横坐标，当前停靠模式可忽略它。</summary>
    public double MainWindowLeft { get; set; } = 80;

    /// <summary>旧设置文件保存的浮窗纵坐标，当前停靠模式可忽略它。</summary>
    public double MainWindowTop { get; set; } = 80;

    /// <summary>桌面浮窗背景的不透明度，0 为全透明，1 为不透明。</summary>
    public double BackgroundOpacity { get; set; } = 0.5;

    /// <summary>是否令课表浮窗保持置顶。</summary>
    public bool Topmost { get; set; } = true;

    /// <summary>与窗口缩放相乘的全局文字比例。</summary>
    public double FontScale { get; set; } = 1.0;

    /// <summary>浮窗在桌面上的基础缩放倍率，1 表示原始尺寸。</summary>
    public double MainWindowScale { get; set; } = 1.9;

    /// <summary>浮窗基础倍率与全局字号倍率之积，无需另行设置。</summary>
    public double EffectiveScale => MainWindowScale * FontScale;

    /// <summary>是否将浮窗上的鼠标点击交给下方窗口。</summary>
    public bool IsClickThrough { get; set; } = true;

    /// <summary>鼠标移入浮窗后淡化到的不透明度。</summary>
    public double HoverOpacity { get; set; } = 0.05;

    /// <summary>桌面组件的排列方向。</summary>
    public LayoutOrientation Orientation { get; set; } = LayoutOrientation.Horizontal;

    /// <summary>在时间表中新建课程时的默认分钟数。</summary>
    public int DefaultOnClassTimePointMinutes { get; set; } = 40;

    /// <summary>在时间表中新建课间时的默认分钟数。</summary>
    public int DefaultBreakingTimePointMinutes { get; set; } = 10;

    /// <summary>桌面组件实例及其排列、启用状态和独立参数。</summary>
    public List<WidgetConfig> Widgets { get; set; } = new();

    /// <summary>当前时间表，包含课程、课间、分隔点与行动。</summary>
    public TimeLayout TimeLayout { get; set; } = new();

    /// <summary>保存的主题选择：system、light 或 dark。</summary>
    public string ThemeMode { get; set; } = "system";

    /// <summary>托盘点击动作的持久化选项索引。</summary>
    public int TrayClickBehavior { get; set; }

    /// <summary>本机的集控绑定与最近同步状态，不随班级配置下发。</summary>
    public CentralManagementSettings CentralManagement { get; set; } = new();

    /// <summary>保留供旧配置读取的本机账户名称，不代表已登录。</summary>
    public string UserName { get; set; } = "";

    /// <summary>保留供旧配置读取的本机邮箱，不代表已验证。</summary>
    public string Email { get; set; } = "";

    /// <summary>浏览器登录和用户资料请求使用的账户服务地址。</summary>
    public string AccountEndpoint { get; set; } = DefaultAccountEndpoint;

    /// <summary>账户服务登记的桌面 OAuth 客户端标识。</summary>
    public string AccountClientId { get; set; } = "fa7664d772fac4b5097ce15c";

    /// <summary>课堂通知的总开关。</summary>
    public bool AllowNotification { get; set; } = true;

    /// <summary>是否允许重要通知播放视觉效果。</summary>
    public bool AllowNotificationEffect { get; set; } = true;

    /// <summary>是否显示次要课堂通知。</summary>
    public bool AllowMinorNotification { get; set; } = true;

    /// <summary>课前、上课和下课通知的规则与文案。</summary>
    public NotificationSettings Notification { get; set; } = new();

    /// <summary>是否要求 Windows 在登录时启动本程序。</summary>
    public bool RunAtStartup { get; set; }

    /// <summary>是否注册应用的自定义 URL 协议。</summary>
    public bool RegisterUrlProtocol { get; set; }

    /// <summary>课堂运行时是否应用已选择的异常处置策略。</summary>
    public bool TeachingSafeMode { get; set; }

    /// <summary>教学安全模式下使用的异常处置选项索引。</summary>
    public int CrashHandlingMode { get; set; }

    /// <summary>启动时是否显示欢迎画面。</summary>
    public bool ShowStartupSplash { get; set; } = true;

    /// <summary>是否已在本机解锁开发者设置。</summary>
    public bool IsDebugOptionsEnabled { get; set; }

    /// <summary>旧安装示例档案迁移标记；不用于向新用户分发示例课表。</summary>
    public int SampleSeedVersion { get; set; }

    /// <summary>为新的布局返回独立的日期和课表组件实例。</summary>
    public static List<WidgetConfig> CreateDefaultWidgets() => new()
    {
        new WidgetConfig { Type = "date", IsEnabled = true, Order = 0 },
        new WidgetConfig { Type = "schedule", IsEnabled = true, Order = 1 },
    };

    /// <summary>标记本机是否已完成首次启动向导。</summary>
    public bool IsOobeCompleted { get; set; }

    /// <summary>设置中保存的界面语言标识（如 zh-CN）。</summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>可选的学期起始日期，与单双周基准分别保存。</summary>
    public DateTime? SemesterStartDate { get; set; }

    /// <summary>是否启用 NEX Intelligence 功能入口。</summary>
    public bool IsNexIntelligenceEnabled { get; set; } = true;

    /// <summary>该设备向模型服务发起请求的基地址。</summary>
    public string NexApiBaseUrl { get; set; } = "https://cyberspace.furruka.asia";

    /// <summary>该设备的模型服务凭据，不应写入日志或集控配置。</summary>
    public string NexApiKey { get; set; } = "";

    /// <summary>发送给模型服务的模型标识。</summary>
    public string NexModel { get; set; } = "z-ai/glm-5.3";

    /// <summary>本机保存的助手工具权限键；默认 readonly。</summary>
    public string NexPermission { get; set; } = "readonly";

    /// <summary>可选的助手系统提示词。</summary>
    public string NexSystemPrompt { get; set; } = "";

    /// <summary>是否允许语音唤醒助手。</summary>
    public bool IsNexVoiceWakeEnabled { get; set; } = true;

    /// <summary>是否在桌面边缘显示助手唤醒条。</summary>
    public bool IsNexEdgeBarEnabled { get; set; } = true;

    /// <summary>唤醒条所在的屏幕边缘，left 或 right。</summary>
    public string NexEdgeBarSide { get; set; } = "right";
}

/// <summary>课堂事件的启用、提前量和呈现文本；保留 Alpha 设置名以供已有安装升级。</summary>
public sealed class NotificationSettings
{
    /// <summary>是否在课程开始时提醒。</summary>
    public bool IsClassOnNotificationEnabled { get; set; } = true;

    /// <summary>是否在下节课开始前提醒。</summary>
    public bool IsClassOnPreparingNotificationEnabled { get; set; } = true;

    /// <summary>是否在课程结束时提醒。</summary>
    public bool IsClassOffNotificationEnabled { get; set; } = true;

    /// <summary>旧格式使用的室内课提前量（秒）；新设置以课间剩余分钟数为准。</summary>
    public int InDoorClassPreparingDeltaTime { get; set; } = 60;

    /// <summary>户外课提前准备的秒数。</summary>
    public int OutDoorClassPreparingDeltaTime { get; set; } = 600;

    /// <summary>室内课在课间剩余多少分钟时提醒。</summary>
    public int ClassOnBreakReminderMinutes { get; set; } = 3;

    /// <summary>室内课准备页的提示文案。</summary>
    public string ClassOnPreparingText { get; set; } = "准备上课，请回到座位并保持安静，做好上课准备。";

    /// <summary>室内课课前蒙版的短标题。</summary>
    public string ClassOnPreparingMaskText { get; set; } = "即将上课";

    /// <summary>户外课课前蒙版的短标题。</summary>
    public string OutdoorClassOnPreparingMaskText { get; set; } = "即将上课";

    /// <summary>户外课准备页的提示文案。</summary>
    public string OutdoorClassOnPreparingText { get; set; } = "下节课程为户外课程，请合理规划时间，做好上课准备。";

    /// <summary>正式上课时蒙版显示的标题。</summary>
    public string ClassOnMaskText { get; set; } = "上课";

    /// <summary>下课时蒙版显示的标题。</summary>
    public string ClassOffMaskText { get; set; } = "课间休息";

    /// <summary>课间详情页的可选自定义文案。</summary>
    public string ClassOffOverlayText { get; set; } = "";

    /// <summary>通知详情是否包含授课教师。</summary>
    public bool ShowTeacherName { get; set; }
}
