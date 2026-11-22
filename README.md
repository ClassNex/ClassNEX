<!--markdownlint-disable MD001 MD033 MD041 MD051-->

<div align="center">

# <image src="1.png" height="28" width="28"/> ClassNEX

ClassNEX 在 Windows 桌面上展示当前课程、剩余时间和下节课，支持编辑课表、课堂提醒与多设备集控。

</div>

> [!WARNING]
> **ClassNEX 目前仍处于开发阶段。**
>
> 项目仍在快速迭代中，部分功能尚未完成，API、配置格式及项目结构可能发生变化。
> **目前不建议将 ClassNEX 用于生产环境。**
## 贡献者

<p align="center">
  <img
    src="assets/contributors.svg"
    width="100%"
    alt="ClassNEX Contributors"
  />
</p>
---

## Windows 桌面版

上课时，浮窗显示当前课程和剩余时间；课间显示接下来的安排。托盘菜单可以打开课表档案、调整组件或进入设置。CSES YAML 课表和便携版 `data/` 目录沿用 Alpha 格式。

本分支从 `26w46d_Alpha` 开发，使用 .NET 10、Avalonia 11.3.12、AvaloniaFluentUI 1.0.3 和 YamlDotNet 16.3.0。它不是新的发行版。

```powershell
dotnet restore ClassNex.sln
dotnet build src/ClassNex/ClassNex.csproj -c Debug -t:Rebuild
dotnet run --project src/ClassNex/ClassNex.csproj --no-build
```

- **课表与档案**：编辑课程、科目和上课／课间时间点。课表支持每周、单周和双周安排。
- **桌面与通知**：主题支持浅色、深色或跟随系统。浮窗可调整字号、顺序、比例与鼠标穿透；提醒不使用 TTS。
- **账户与集控**：ClassNEX ID 使用浏览器授权。登录集控端后可把设备分配给班级、修改课表与桌面设置。本机身份和接口密钥不随设置上传。
- **扩展**：插件运行在信任的桌面进程内，并非安全沙箱。NEX Intelligence 使用用户配置的接口；软件不携带通用 API 密钥。

模块边界见 [架构说明](docs/ARCHITECTURE.md)，插件开发见 [插件 SDK](docs/PLUGIN_SDK.md)，复测和已知边界见 [本分支交付记录](docs/26.0-acceptance.md)。

### 课表格式（CSES）

```yaml
version: 1
subjects:
  - name: 数学            # 课程名（必填）
    simplified_name: 数   # 简称（可选）
    teacher: 李梅          # 教师（可选）
    room: "101"            # 教室（可选）
schedules:
  - name: 星期一
    enable_day: 1          # 1-7：周一~周日
    weeks: all             # all / odd / even
    classes:
      - subject: 数学
        start_time: "08:00:00"
        end_time: "08:45:00"
```

---

## 获取帮助＆加入社区

您可以通过以下方式获取帮助：

* 项目文档
* GitHub Issues
* GitHub Discussions

如果您确定遇到的是 Bug，或者希望提出新的功能，请提交 Issue。

## 开发

桌面、核心和集控共用 `ClassNex.sln`。桌面窗口使用 Avalonia，集控服务使用 ASP.NET Core。验证本地改动可运行 `dotnet test ClassNex.sln`。

```text
src/
├── ClassNex/                        桌面窗口与平台适配
├── ClassNex.Core/                   业务模型与公共契约
├── ClassNex.Notification.Service/   提醒与投递队列
├── ClassNex.Plugin/                 插件发现与生命周期
├── ClassNex.AI/                     NEX Intelligence
├── ClassNex.Central/                独立集控站点
└── ClassNex.Launcher/               便携版启动器
```

## 致谢

本项目的开发参考了以下优秀的开源项目：

* [FluentUI（AvaloniaFluentUI）](https://github.com/IzumiPL/Avalonia-Fluent-UI)
* [ClassIsland](https://github.com/ClassIsland/ClassIsland)
* [FlClash](https://github.com/chen08209/FlClash)
* [Class-Widgets-2](https://github.com/RinLit-233-shiroko/Class-Widgets-2)
* [MornheIsland](https://github.com/HeyCrab3/MornheIsland)

感谢这些项目在课表软件,集控方案方面提供的参考。

## 许可证

ClassNEX 使用 GNU General Public License v3.0 开源协议。

详见 [LICENSE](LICENSE)。

---
**ClassNEX · Produced by LING & Washiyin**
