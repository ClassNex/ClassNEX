# ClassNEX 架构

## 项目分层

| 项目 | 职责 | 依赖 |
| --- | --- | --- |
| `ClassNex.Core` | 模型、核心服务契约、课程/设置/时间/组件服务、插件 SDK 契约、AI/通知抽象 | 无 |
| `ClassNex.Notification` | 课前、上课、下课提醒调度与提醒流程 | `ClassNex.Core` |
| `ClassNex.AI` | NEX HTTP 客户端、Agent、工具调用、模型目录与 AI 服务实现 | `ClassNex.Core` |
| `ClassNex.Plugins` | 插件清单发现、可卸载加载上下文、生命周期管理 | `ClassNex.Core` |
| `ClassNex` | Avalonia UI、窗口、语音识别集成、静态资源与组合根 | 以上全部 |
| `ClassNex.Launcher` | 独立启动器 | 无 |

## 依赖方向

```text
ClassNex.Core
  ^       ^       ^
  |       |       |
Notification  AI  Plugins
  ^       ^       ^
  \       |       /
       ClassNex
```

模块之间不引用主程序，主程序只通过 Core 中的接口组合服务。通知模块通过
`INotificationSurface` 调用 UI；AI 模块通过 `IAiApplicationContext` 读取应用状态；
插件通过 `PluginContext` 访问 Core、通知和 AI 接口。

## 组合根

`AppServices` 创建核心服务与 AI 服务。`App` 创建通知 Surface、通知服务、插件服务和
语音协调器，并把实例注册到插件上下文。UI 只负责窗口和交互。
