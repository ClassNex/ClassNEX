# ClassNEX 架构

## 项目分层

| 项目 | 职责 | 依赖 |
| --- | --- | --- |
| `ClassNex.Core` | 模型、课程/设置/时间/组件服务、插件 SDK、AI/通知契约 | YamlDotNet，无 UI 或其他项目依赖 |
| `ClassNex.Notification.Service` | 课前、上课、下课提醒调度与提醒流程 | `ClassNex.Core` |
| `ClassNex.AI` | NEX HTTP 客户端、Agent、工具调用、模型目录与 AI 服务实现 | `ClassNex.Core` |
| `ClassNex.Plugin` | 插件清单发现、可卸载加载上下文、生命周期管理 | `ClassNex.Core` |
| `ClassNex.WinUI` | 原生 WinUI 3 桌面呈现层 | `ClassNex.Core`、Windows App SDK |
| `ClassNex` | 客户端入口、桌面窗口、语音识别集成、静态资源与组合根 | Core、WinUI、Notification.Service、Plugin、AI |
| `ClassNex.Central` | 独立部署的集控服务与管理界面 | `ClassNex.Core` |
| `ClassNex.Launcher` | 独立启动器 | 无 |

## 依赖方向

```text
ClassNex ───┬─ ClassNex.Notification.Service ──┐
            ├─ ClassNex.Plugin ───────────────┤
            ├─ ClassNex.AI ───────────────────┤
            ├─ ClassNex.WinUI ────────────────┤
            └─────────────────────────────────┼─ ClassNex.Core
ClassNex.Central ─────────────────────────────┘
```

服务实现彼此不引用，也不引用主程序。通知通过 `INotificationSurface` 呈现；语音协调器通过 `INexPresentation` 更改浮窗的 NEX 内容。AI 通过 `IAiApplicationContext` 读取应用状态；旧插件仍通过 `PluginContext` 访问 Core、通知和 AI 接口。`DayScheduleCache` 为课表浮窗和通知共用当天的课程列表，日期、单双周或档案变更时重算。

桌面包的版本载荷目录 `app-<版本>-<序号>/` 随主程序携带 `ClassNex.Core.dll`、
`ClassNex.Notification.Service.dll`、`ClassNex.Plugin.dll`、`ClassNex.AI.dll` 和 `ClassNex.WinUI.dll`。
GitHub Actions 在生成 SHA512 清单前检查这些模块均已发布。集控独立发布，不打入桌面包。

## 组合根

`AppServices` 创建核心服务与 AI 服务。`App` 组装通知、插件、语音和客户端窗口，`NativeDesktop` 用 `DesktopBridge` 将 WinUI 设置和浮窗连接到现有服务。同一个设置实例既接受本机修改也接受集控下发；两种操作都不改写本机的登录身份。设置写入使用同目录临时文件原子替换。

桌面进程由 WinUI 的 STA 消息循环驱动。设置和浮窗使用 `Microsoft.UI.Xaml`；档案编辑器、OOBE、登录和托盘等辅助窗口继续使用同一线程上的 Avalonia。自包含 Windows 发布须携带与载荷匹配的 `ClassNex.pri`，否则原生控件无法读取主题资源。
