# ClassNex

一个基于 **Avalonia** 的课程表软件，采用 **CSES（Course Schedule Exchange Schema）** 课程表交换格式，界面使用 **FluentUI（FluentAvalonia）** 风格，参考 [ClassIsland](https://github.com/ClassIsland/ClassIsland) 的产品形态。

## 功能

程序由四个部分组成 ——「应用」与「课表」分离：

| 模块 | 说明 |
| --- | --- |
| **主界面**（悬浮课表） | 无边框半透明置顶卡片，悬浮在桌面上，实时显示日期与当前/下一节课；可拖拽、右键菜单 |
| **应用设置** | 左侧导航 + 通用 / 界面 / 课表 / 关于 四页（主题、主界面不透明度与字号、单周起始、托盘行为等） |
| **档案编辑器** | 课表 / 时间表 / 科目 / 调课 四个标签页；「科目」页可直接编辑并写回 CSES 文件 |
| **系统托盘** | 显示/隐藏主界面、编辑档案、加载课表、换课、编辑主界面、应用设置、重启、退出 |

## 运行

```powershell
# 在仓库根目录
dotnet run --project src/ClassNex
```

启动后：桌面上会出现**悬浮课表卡片**，同时**系统托盘**出现 ClassNex 图标（Windows 默认收纳在「显示隐藏的图标」里）。右键卡片或托盘图标可打开菜单。

## 技术栈

| 组件 | 说明 |
| --- | --- |
| [Avalonia](https://avaloniaui.net/) 11.2.x | 跨平台 UI 框架 |
| [FluentAvaloniaUI](https://github.com/amwx/FluentAvalonia) 2.4.0 | FluentUI / WinUI 风格的 Avalonia 主题库 |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) 16.x | CSES YAML 解析与序列化 |
| .NET 8 | 运行时 / SDK |

## 目录结构

```
ClassNex.sln
src/ClassNex/
  ├── Program.cs                 # 入口（先加载设置与课表）
  ├── App.axaml(.cs)             # 主题、托盘、窗口管理、应用生命周期
  ├── Models/
  │   ├── CsesModels.cs          # CSES 数据模型
  │   └── AppSettings.cs         # 应用设置模型
  ├── Services/
  │   ├── CsesService.cs         # CSES 读写（YAML）
  │   ├── SettingsService.cs     # 设置持久化（data/Settings.json）
  │   ├── AppServices.cs         # 共享状态（当前课表/设置）
  │   ├── ScheduleCalculator.cs  # 今天/当前/下一节课推算
  │   ├── TimetableGridRenderer.cs # 周课表网格渲染
  │   └── FilePickerHelper.cs    # 文件选择
  ├── ViewModels/                # MVVM
  ├── Views/
  │   ├── MainWindow.axaml       # 主界面（悬浮课表）
  │   ├── SettingsWindow.axaml   # 应用设置
  │   └── ProfileEditorWindow.axaml # 档案编辑器
  └── Assets/
      ├── timetable.yaml         # 内置示例课表（CSES）
      └── icon.ico               # 应用/托盘图标
```

## 数据存储

| 文件 | 内容 |
| --- | --- |
| `data/Settings.json` | 应用设置（与程序同级，便携） |
| `data/timetable.yaml` | 当前课表（首次运行从内置示例复制） |

## 课表格式（CSES）

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

## 构建

```powershell
dotnet build
```
