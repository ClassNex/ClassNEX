<!--markdownlint-disable MD001 MD033 MD041 MD051-->

<div align="center">

# <image src="1.png" height="28" width="28"/> ClassNEX

ClassNEX 是一款功能强大、可定制、跨平台的课表信息显示工具，多设备集控和强大的跨平台能力，内置Agent，让课表及课堂信息一目了然。

#### 🌐 官方网站 | 🚀 软件下载 | 📚 项目文档 | 💬 社区交流

</div>

> [!WARNING]
> **ClassNEX 目前仍处于开发阶段。**
>
> 项目仍在快速迭代中，部分功能尚未完成，API、配置格式及项目结构可能发生变化。
> **目前不建议将 ClassNEX 用于生产环境。**

---

## 当前实现（阶段一：桌面端课表）

仓库中 `src/ClassNex` 是当前**可运行的桌面客户端**，参考 [ClassIsland](https://github.com/ClassIsland/ClassIsland) 的产品形态实现，「应用」与「课表」分离，课表采用 **CSES** 交换格式。

| 模块 | 说明 |
| --- | --- |
| **主界面**（悬浮课表） | 无边框半透明置顶卡片，悬浮在桌面上，实时显示日期与当前/下一节课；可拖拽、右键菜单 |
| **应用设置** | 左侧导航 + 通用 / 界面 / 课表 / 关于 四页（主题、主界面不透明度与字号、单周起始、托盘行为等） |
| **档案编辑器** | 课表 / 时间表 / 科目 / 调课 四个标签页；「科目」页可直接编辑并写回 CSES 文件 |
| **系统托盘** | 显示/隐藏主界面、编辑档案、加载课表、换课、编辑主界面、应用设置、重启、退出 |

### 运行

```powershell
# 在仓库根目录
dotnet run --project src/ClassNex
```

启动后：桌面上会出现**悬浮课表卡片**，同时**系统托盘**出现 ClassNex 图标（Windows 默认收纳在「显示隐藏的图标」里）。右键卡片或托盘图标可打开菜单。

### 技术栈（桌面端）

| 组件 | 说明 |
| --- | --- |
| [Avalonia](https://avaloniaui.net/) 11.2.x | 跨平台 UI 框架 |
| [FluentAvaloniaUI](https://github.com/amwx/FluentAvalonia) 2.4.0 | FluentUI / WinUI 风格的 Avalonia 主题库 |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) 16.x | CSES YAML 解析与序列化 |
| .NET 8 | 运行时 / SDK |

### 目录结构（当前）

```text
ClassNex.sln
src/ClassNex/
├── Program.cs                      # 入口（先加载设置与课表）
├── App.axaml(.cs)                  # 主题、托盘、窗口管理、应用生命周期
├── Models/
│   ├── CsesModels.cs               # CSES 数据模型
│   └── AppSettings.cs              # 应用设置模型
├── Services/
│   ├── CsesService.cs              # CSES 读写（YAML）
│   ├── SettingsService.cs          # 设置持久化（data/Settings.json）
│   ├── AppServices.cs              # 共享状态（当前课表 / 设置）
│   ├── ScheduleCalculator.cs       # 今天 / 当前 / 下一节课推算
│   ├── TimetableGridRenderer.cs    # 周课表网格渲染
│   └── FilePickerHelper.cs         # 文件选择
├── ViewModels/                     # MVVM
├── Views/
│   ├── MainWindow.axaml            # 主界面（悬浮课表）
│   ├── SettingsWindow.axaml        # 应用设置
│   └── ProfileEditorWindow.axaml   # 档案编辑器
└── Assets/
    ├── timetable.yaml              # 内置示例课表（CSES）
    └── icon.ico                    # 应用 / 托盘图标
```

### 数据存储

| 文件 | 内容 |
| --- | --- |
| `data/Settings.json` | 应用设置（与程序同级，便携） |
| `data/timetable.yaml` | 当前课表（首次运行从内置示例复制） |

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

ClassNEX 使用 C# / .NET 开发。

主要技术：

* .NET
* Avalonia
* ASP.NET Core
* Blazor
* SignalR
* PostgreSQL
* Redis

### 项目结构

```text
ClassNEX/
├── ClassNEX.Core/
├── ClassNEX.Desktop/
├── ClassNEX.Server/
├── ClassNEX.Widgets/
├── ClassNEX.Intelligence.core/
├── docs/
└── tests/
```

## 致谢

本项目的开发参考了以下优秀的开源项目：

* [ClassIsland](https://github.com/ClassIsland/ClassIsland)
* [Class-Widgets-2](https://github.com/RinLit-233-shiroko/Class-Widgets-2)
* [MornheIsland](https://github.com/HeyCrab3/MornheIsland)

感谢这些项目在课表软件,集控方案方面提供的参考。

## 许可证

ClassNEX 使用 GNU General Public License v3.0 开源协议。

详见 [LICENSE](LICENSE)。

---

<div align="center">

**ClassNEX · Powered by LingOfficial**

</div>
