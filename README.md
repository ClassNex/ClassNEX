# ClassNex

一个基于 **Avalonia** 的课程表软件，采用 **CSES（Course Schedule Exchange Schema）** 课程表交换格式，界面使用 **FluentUI（FluentAvalonia）** 风格。目标是对标 [ClassIsland](https://github.com/ClassIsland/ClassIsland) 的课程表展示能力。

## 当前进度

- ✅ 课程表周视图展示（7 天 × 时间段网格，按科目着色，单周/双周/全部切换）
- ✅ CSES YAML 课程表解析（`version` / `subjects` / `schedules` / `classes`）
- ✅ FluentUI 设置界面（外观主题切换、课表文件、关于）

## 技术栈

| 组件 | 说明 |
| --- | --- |
| [Avalonia](https://avaloniaui.net/) 11.2.x | 跨平台 UI 框架 |
| [FluentAvaloniaUI](https://github.com/amwx/FluentAvalonia) 2.4.0 | FluentUI / WinUI 风格的 Avalonia 主题库 |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) 16.x | YAML 解析 |
| .NET 8 | 运行时 / SDK |

## 目录结构

```
ClassNex.sln
src/ClassNex/
  ├── Program.cs            # 应用入口
  ├── App.axaml(.cs)        # 应用与主题
  ├── Models/               # CSES 数据模型
  ├── Services/             # CSES 解析服务
  ├── ViewModels/           # MVVM 视图模型
  ├── Views/                # 主窗口 / 设置窗口
  └── Assets/timetable.yaml # 内置示例课表（CSES）
```

## 构建与运行

```powershell
dotnet build
dotnet run --project src/ClassNex
```

## 课表数据（CSES）

课表采用 CSES v1 YAML 格式，示例见 `src/ClassNex/Assets/timetable.yaml`。核心结构：

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
