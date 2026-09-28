> [!WARNING]
> **ClassNEX 目前仍处于开发阶段。**
>
> 项目仍在快速迭代中，部分功能尚未完成，API、配置格式及项目结构可能发生变化。
> **目前不建议将 ClassNEX 用于生产环境。**

<div align="center">

# ClassNEX

<!-- 这里放 ClassNEX Logo -->

<!-- <img src="docs/images/logo.svg" height="72"/> -->

![Banner](docs/images/banner.png)

ClassNEX 是一款功能强大、可定制、跨平台的智慧教室课表信息显示工具，融合 **NEX Intelligence 智能引擎**、自然语言交互、桌面组件与多设备集控，让课表及课堂信息一目了然。

#### 🌐 官方网站 | 🚀 软件下载 | 📚 项目文档 | 💬 社区交流

**Powered by NEX Intelligence**

</div>

## 功能

### 课表信息显示

* [ ] 显示当天课表、当前课程信息
* [ ] 显示上下课倒计时
* [ ] 在重要时间点发出提醒
* [ ] 支持提醒音效、强调特效、语音等提醒方式
* [ ] 支持单双周、A/B 周课表
* [ ] 支持临时调课与校历

### NEX Intelligence

**NEX Intelligence** 是 ClassNEX 内置的智能引擎，为课表管理、信息查询和设备控制提供自然语言交互能力。

* [ ] 自然语言查询课表
* [ ] 自然语言修改课表
* [ ] 智能生成课表操作
* [ ] 自然语言修改应用设置
* [ ] 批量执行课表操作
* [ ] AI 课堂信息问答
* [ ] 语音交互
* [ ] NEX Intelligence 跑马灯
* [ ] 支持 OpenAI 兼容 API
* [ ] 支持本地及第三方 AI 模型

例如：

```text
“明天下午第三节改成物理。”

“今天还有几节课？”

“把周三第一节和第四节对调。”

“把所有设备切换到考试模式。”
```

NEX Intelligence 会将自然语言转换为结构化操作，并交由 ClassNEX 的执行系统进行验证和处理。

> **NEX Intelligence 负责理解与编排，ClassNEX 负责验证与执行。**

### 自定义

* [ ] 自由组合显示内容
* [ ] 支持课表、时钟、倒计时、天气、通知等组件
* [ ] 自定义组件位置与大小
* [ ] 自定义透明度、圆角等显示效果
* [ ] 自定义主题与主题色
* [ ] 支持插件扩展
* [ ] 支持自定义组件

### 多设备集控

* [ ] 远程同步课表
* [ ] 远程修改设备设置
* [ ] 远程发送通知
* [ ] 远程控制组件
* [ ] 设备状态监控
* [ ] 设备分组
* [ ] 远程截屏
* [ ] 权限管理
* [ ] 操作日志

### 跨平台

ClassNEX 致力于在多个平台提供一致的使用体验。

* [ ] Windows
* [ ] Linux
* [ ] Android
* [ ] Web 管理端

## 软件截图

> 截图将在开发过程中持续更新。

### 主界面

![ClassNEX 主界面](docs/images/main.png)

### NEX Intelligence

![NEX Intelligence](docs/images/nex-intelligence.png)

### 课表编辑

![课表编辑](docs/images/schedule-editor.png)

### 桌面组件

![桌面组件](docs/images/widgets.png)

### 集控中心

![集控中心](docs/images/control-center.png)

## 开始使用

### 桌面端

推荐使用：

* Windows 10 及以上
* Linux
* .NET 运行时

### Android

Android 客户端用于移动端课表管理、NEX Intelligence 交互以及多设备集控。

### Web

Web 管理端用于：

* 课表管理
* 设备管理
* 集控操作
* NEX Intelligence 管理
* 用户及权限管理

详细安装说明请参阅项目文档。

## 获取帮助＆加入社区

您可以通过以下方式获取帮助：

* 项目文档
* GitHub Issues
* GitHub Discussions

如果您确定遇到的是 Bug，或者希望提出新的功能，请提交 Issue。

## 开发

ClassNEX 使用 C# / .NET 开发。

主要技术：

* .NET
* Avalonia
* ASP.NET Core
* Blazor
* SignalR
* PostgreSQL
* Redis
* NEX Intelligence

### 项目结构

```text
ClassNEX/
├── ClassNEX.Core/
├── ClassNEX.Desktop/
├── ClassNEX.Android/
├── ClassNEX.Web/
├── ClassNEX.Server/
├── ClassNEX.Widgets/
├── ClassNEX.Intelligence/
├── docs/
└── tests/
```

### 本地运行

```bash
git clone https://github.com/<OWNER>/ClassNEX.git

cd ClassNEX

dotnet restore
```

运行桌面客户端：

```bash
dotnet run --project src/ClassNEX.Desktop
```

运行服务端：

```bash
dotnet run --project src/ClassNEX.Server
```

## 路线图

### 基础功能

* [ ] 课表核心
* [ ] 课表显示
* [ ] 课表编辑
* [ ] 时间与倒计时
* [ ] 临时调课
* [ ] 组件系统
* [ ] 主题系统

### NEX Intelligence

* [ ] NEX Intelligence Core
* [ ] Function Calling
* [ ] 自然语言课表操作
* [ ] 自然语言设置操作
* [ ] 课表智能问答
* [ ] 语音交互
* [ ] 多模型支持

### 集控

* [ ] 设备注册
* [ ] 设备心跳
* [ ] 课表同步
* [ ] 远程设置
* [ ] 通知广播
* [ ] 设备分组
* [ ] 权限系统

### 多端

* [ ] Windows
* [ ] Linux
* [ ] Android
* [ ] Web

## 致谢

本项目的开发参考了以下优秀的开源项目：

* [ClassIsland](https://github.com/ClassIsland/ClassIsland)
* [Class-Widgets-2](https://github.com/RinLit-233-shiroko/Class-Widgets-2)
* [MornheIsland](https://github.com/HeyCrab3/MornheIsland)

感谢这些项目在课表展示、组件系统及智慧教室软件方面提供的参考。

## 许可证

ClassNEX 使用 GNU General Public License v3.0 开源协议。

详见 [LICENSE](LICENSE)。

---

<div align="center">

**ClassNEX · Powered by NEX Intelligence**

</div>
