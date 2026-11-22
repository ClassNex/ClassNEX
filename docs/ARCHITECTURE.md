# ClassNEX 桌面架构

Windows 主线使用 .NET 10。WinUI 3 接管 STA 消息循环和设置窗口；Avalonia/AvaloniaFluentUI 负责透明课表浮窗、托盘、档案、登录与 OOBE。两个界面在同一进程共享课表和设置，没有第二个桌面浮窗。

| 项目 | 职责和依赖 |
| --- | --- |
| `ClassNex.Core` | 无 UI 依赖。CSES 模型与编解码、单双周、按日课表缓存、便携设置存储、组件配置、插件和提醒公共契约。唯一第三方运行依赖为 YamlDotNet。 |
| `ClassNex.Notification.Service` | 依赖 Core。处理课堂事件，按时间、有效期、去重键和优先级投递到显示渠道；不知道窗口类型。 |
| `ClassNex.Plugin` | 依赖 Core。发现本地清单、检查 API 与依赖、装配可信插件、分离配置、停止与卸载。旧插件的契约类型仍位于 Core 原有命名空间。 |
| `ClassNex.AI` | 依赖 Core。使用注入的课堂上下文访问课程与设置，通过可取消的流式请求提供对话。 |
| `ClassNex.WinUI` | Windows App SDK 1.8 设置窗口、导航与页面；`DesktopBridge` 从桌面组合根获取状态和真实命令。 |
| `ClassNex` | 桌面入口、窗口与视图模型、托盘、账户和平台适配。显式传递实际服务实例给通知、助手与插件宿主。 |
| `ClassNex.Central` | ASP.NET Core 集控站点；只引用 Core，通过 HTTP 协议与客户端通信。 |
| `ClassNex.Launcher` | 选择并启动版本载荷的独立启动器，不加载 UI 或插件。 |

`DesktopRuntime` 持有通知、集控和插件的生命周期。`MainWindowNotificationSurface` 是通知渠道的桌面实现；界面不接管调度。`AppServices` 仍是 Alpha 辅助窗口使用的兼容入口，不是新业务模块的容器。插件的 `IServiceProvider` 用于保持已发布契约；宿主按清单给新插件提供能力，这不能限制进程或操作系统权限。

课表加载或编辑后由档案变更使缓存失效。秒钟、倒计时仍读取当前时间，不靠降低时钟精度节省刷新。设置文件与课表采用同目录临时文件再替换；单节课结束时间不算该课程的上课时间。提醒将课程/课间转换为有限期的请求，窗口隐藏与通知服务解绑。

`CentralSettingsPayload` 定义能离开设备的设置。应用下发配置时保留账号、路径、NEX 密钥和权限；改变外观不会退出登录。集控从配置的 `CENTRAL_PUBLIC_URL` 构造 OAuth 回调和返回地址，不从浏览器传入的 Host 推断。测试时可用 `CENTRAL_DATA_DIRECTORY` 指向空目录，不读取生产数据库。

设计参考：CI 的 Core 服务契约、课堂事件/组件和设置导航，以及 FlClash 桌面端侧栏、信息层次与紧凑分组。源项目只作参考，不分发其界面、品牌素材或字体。
