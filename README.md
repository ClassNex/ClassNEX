<!-- markdownlint-disable MD001 MD033 MD041 MD051 -->

<div align="center">

# ClassNEX

<!-- 这里放 ClassNEX Logo -->

<!-- <img src="docs/images/logo.svg" height="72"/> -->

![Banner](docs/images/banner.png)

[![Stars](https://img.shields.io/github/stars/<OWNER>/ClassNEX?label=Stars)](https://github.com/<OWNER>/ClassNEX)
[![Release](https://img.shields.io/github/v/release/<OWNER>/ClassNEX?style=flat-square\&color=%233fb950\&label=正式版)](https://github.com/<OWNER>/ClassNEX/releases/latest)
[![Pre-release](https://img.shields.io/github/v/release/<OWNER>/ClassNEX?include_prereleases\&style=flat-square\&label=测试版)](https://github.com/<OWNER>/ClassNEX/releases)
[![Downloads](https://img.shields.io/github/downloads/<OWNER>/ClassNEX/total?style=social\&label=下载量\&logo=github)](https://github.com/<OWNER>/ClassNEX/releases/latest)<br/>
![.NET](https://img.shields.io/badge/.NET-8%2F9-512BD4?style=flat-square\&logo=.net)
![Avalonia](https://img.shields.io/badge/UI-Avalonia-8B5CF6?style=flat-square)
![GitHub Repo Size](https://img.shields.io/github/repo-size/<OWNER>/ClassNEX?style=flat-square)
[![Top Language](https://img.shields.io/github/languages/top/<OWNER>/ClassNEX?style=flat-square)](https://github.com/<OWNER>/ClassNEX)

ClassNEX 是一款面向智慧教室的跨平台课表与信息展示工具，融合 **AI 智能编排、自然语言交互、多设备集控与高度可定制的桌面组件**，可以在 Windows、Linux、Android 等设备上展示课表及各种课堂信息。<br/>
让课表不再只是“显示”，而是成为连接课堂、设备与 AI 的智能信息中心。

#### [🌐 官方网站](#) | [🚀 软件下载](#) | [📚 项目文档](#) | [💬 社区交流](#)

</div>

## 功能

> [!TIP]
> ClassNEX 正在持续开发中，部分功能可能尚未完成。实际支持情况请以当前版本为准。

### 课表信息显示

* [ ] 显示当天课表、当前课程及课程状态
* [ ] 显示上下课倒计时、时间信息
* [ ] 显示天气、日期、通知等课堂信息
* [ ] 支持提醒音效、通知及视觉强调效果
* [ ] 支持自定义信息显示方式及隐藏条件

### 课表编辑与管理

* [ ] 简洁直观的课表编辑工具
* [ ] 支持多周课表
* [ ] 支持单双周、A/B 周轮换
* [ ] 支持单日及跨天临时调课
* [ ] 支持节假日与校历
* [ ] 支持课程、教师、教室等信息管理
* [ ] 支持 Excel / CSV / JSON 等数据导入导出
* [ ] 支持课表版本管理与变更同步

### AI 智能助手

* [ ] 使用自然语言查询课程信息
* [ ] 使用自然语言修改课表
* [ ] 批量执行课表调整
* [ ] 使用自然语言修改应用设置
* [ ] AI 自动生成结构化操作指令
* [ ] 支持 OpenAI 兼容 API
* [ ] 支持自建 AI 网关及本地模型
* [ ] 支持语音输入
* [ ] AI 跑马灯交互

例如：

```text
“明天下午第三节换成数学，老师改成王老师。”
```

ClassNEX 会将自然语言解析为结构化操作，再由系统进行规则校验后执行。

> AI 负责理解与编排，系统负责验证与执行。

### 自定义

* [ ] 通过组件自由组合需要显示的信息
* [ ] 支持课表、时间、天气、倒计时、通知等组件
* [ ] 支持组件拖拽、缩放、透明度及圆角调整
* [ ] 支持组件轮播及多行显示
* [ ] 支持主题与主题色自定义
* [ ] 支持插件扩展
* [ ] 支持自定义组件布局

### 多设备集控

* [ ] 远程同步课表
* [ ] 远程修改设备设置
* [ ] 远程发送通知
* [ ] 远程切换设备模式
* [ ] 远程控制组件显示状态
* [ ] 设备在线状态监控
* [ ] 设备分组管理
* [ ] 远程截屏
* [ ] 指令日志与操作审计

### 其它功能

* [ ] Windows / Linux / Android 多平台支持
* [ ] Web 管理后台
* [ ] 开机自启动
* [ ] 自动检查更新
* [ ] 深色 / 浅色主题
* [ ] Fluent Design 风格界面
* [ ] 灵活的通知与提醒系统
* [ ] 插件系统
* [ ] ……

## 软件截图

> 截图将在客户端 UI 完成后持续补充。

### 主界面

![软件截图 - 主界面](docs/images/main.png)

### AI 助手

![软件截图 - AI 助手](docs/images/ai.png)

### 课表编辑器

![软件截图 - 课表编辑](docs/images/schedule-editor.png)

### 组件

![软件截图 - 桌面组件](docs/images/widgets.png)

### 集控中心

![软件截图 - 集控中心](docs/images/control-center.png)

<details>
<summary>查看更多软件截图……</summary>

### 设置界面

![软件截图 - 设置](docs/images/settings.png)

### OOBE 开箱引导

![软件截图 - OOBE](docs/images/oobe.png)

### Web 管理端

![软件截图 - Web 管理端](docs/images/web-admin.png)

</details>

## 开始使用

**首先，请确保您的设备满足当前版本的系统要求。**

### 桌面端

* Windows 10 或更高版本
* Linux
* 其它 Avalonia 支持的平台

运行桌面客户端需要安装项目对应版本的 .NET Runtime。

### Android

Android 客户端用于移动端控制与管理，包括：

* 查看课表
* 修改课表
* AI 操作
* 发送通知
* 管理大屏设备
* 查看设备状态

### Web

Web 管理端主要用于：

* 设备管理
* 课表管理
* 集控操作
* AI 管理
* 用户及权限管理

> [!IMPORTANT]
> 详细安装及配置说明请参阅项目文档。

## 获取帮助＆加入社区

您可以通过以下方式获取帮助：

* [项目文档](#)
* [GitHub Issues](https://github.com/<OWNER>/ClassNEX/issues)
* [GitHub Discussions](https://github.com/<OWNER>/ClassNEX/discussions)

如果您确定遇到的是 **Bug**，或者希望提出新的功能建议，请提交 Issue。

提交 Bug 时建议提供：

```text
ClassNEX 版本：
操作系统：
设备型号：
问题描述：
复现步骤：
错误日志：
相关截图：
```

## 开发

ClassNEX 使用 C# / .NET 构建。

### 核心技术

| 模块    | 技术                                      |
| ----- | --------------------------------------- |
| 桌面 UI | Avalonia UI                             |
| UI 风格 | FluentAvalonia                          |
| 开发模式  | MVVM                                    |
| 核心语言  | C#                                      |
| 服务端   | ASP.NET Core                            |
| 实时通信  | SignalR                                 |
| Web   | Blazor                                  |
| 数据库   | PostgreSQL                              |
| 缓存    | Redis                                   |
| AI    | OpenAI Compatible API / Semantic Kernel |
| 部署    | Docker                                  |

### 项目结构

```text
ClassNEX/
├── src/
│   ├── ClassNEX.Core/
│   ├── ClassNEX.Client.Avalonia/
│   ├── ClassNEX.Mobile.Android/
│   ├── ClassNEX.Widgets/
│   ├── ClassNEX.Server/
│   ├── ClassNEX.Web/
│   └── ClassNEX.Shared.Dto/
│
├── tests/
├── deploy/
├── docs/
└── ClassNEX.sln
```

### 本地运行

安装 .NET SDK 后：

```bash
git clone https://github.com/<OWNER>/ClassNEX.git

cd ClassNEX

dotnet restore
```

运行桌面客户端：

```bash
dotnet run --project src/ClassNEX.Client.Avalonia
```

运行服务端：

```bash
dotnet run --project src/ClassNEX.Server
```

### 开发原则

ClassNEX 的开发遵循以下原则：

```text
课表核心
   ↓
桌面展示
   ↓
组件系统
   ↓
AI 能力
   ↓
多设备集控
   ↓
Android / Web
```

优先保证核心课表能力稳定，再逐步扩展 AI、集控以及多端能力。

## 设计理念

### AI Native

AI 并不是附加的聊天窗口，而是 ClassNEX 的重要交互方式。

用户可以直接通过自然语言完成课表查询、调课、设置修改和设备控制。

### Cross Platform

使用 Avalonia 构建跨平台客户端，在统一核心代码的基础上适配 Windows、Linux、Android 等平台。

### Highly Customizable

组件、主题、布局、通知以及 AI 服务均可根据实际使用场景进行调整。

### Safe & Controllable

AI 不直接操作底层数据，而是生成结构化指令，再由本地执行器完成校验与执行。

## 参考项目

ClassNEX 在设计和开发过程中参考了以下开源项目：

* [ClassIsland](https://github.com/ClassIsland/ClassIsland)
* [Class-Widgets-2](https://github.com/RinLit-233-shiroko/Class-Widgets-2)
* [MornheIsland](https://github.com/HeyCrab3/MornheIsland)

感谢这些项目在课表展示、组件系统、集控等方向提供的优秀实践与参考。

## 贡献

欢迎任何形式的贡献：

* 提交 Issue
* 提交 Pull Request
* 改进文档
* 提供 UI / UX 建议
* 开发插件
* 提交主题
* 参与测试

在提交 Pull Request 前，请先阅读：

[CONTRIBUTING.md](CONTRIBUTING.md)

## 路线图

### 基础课表

* [ ] 课表核心
* [ ] 时间表
* [ ] 课程管理
* [ ] 课表导入导出
* [ ] 多周与单双周
* [ ] 临时调课
* [ ] 大屏展示

### AI

* [ ] AI Gateway
* [ ] Function Calling
* [ ] AI 课表操作
* [ ] AI 设置操作
* [ ] AI 问答
* [ ] AI 跑马灯
* [ ] 语音输入

### 集控

* [ ] SignalR Hub
* [ ] 设备注册
* [ ] 设备心跳
* [ ] 课表同步
* [ ] 远程设置
* [ ] 通知广播
* [ ] 设备分组
* [ ] 权限管理
* [ ] 审计日志

### 多端

* [ ] Windows
* [ ] Linux
* [ ] Android
* [ ] Web 管理端

## 致谢

感谢以下开源项目及技术：

* [Avalonia](https://github.com/AvaloniaUI/Avalonia)
* [FluentAvalonia](https://github.com/amwx/FluentAvalonia)
* [.NET](https://dotnet.microsoft.com/)
* [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
* [Microsoft Semantic Kernel](https://github.com/microsoft/semantic-kernel)
* [ASP.NET Core](https://github.com/dotnet/aspnetcore)
* [SignalR](https://github.com/dotnet/aspnetcore)
* [PostgreSQL](https://www.postgresql.org/)
* [Redis](https://redis.io/)
* [Docker](https://www.docker.com/)

特别感谢 ClassIsland、Class-Widgets-2 与 MornheIsland 等项目提供的参考。

## License

本项目采用 **GNU General Public License v3.0** 开源协议。

详见：

[LICENSE](LICENSE)

---

<div align="center">

**ClassNEX**

AI 驱动的智慧教室课表系统

</div>
