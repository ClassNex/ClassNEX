# ClassNEX 续作指南（HANDOFF）

> 最后更新：2026-10-06　｜　当前版本 **26w41d**　｜　仓库提交 **f7a6405**
> 已发布：`26w41a_Alpha` / `26w41b_Alpha` / `26w41c_Alpha` / `26w41d_Alpha`（GitHub Releases，均为预发布）
> 这份文档的目的：**第二天打开新会话，照着它就能无缝继续写。**

---

## 0. 一分钟上手

```powershell
cd E:\ClassNex
dotnet build src\ClassNex\ClassNex.csproj -c Debug          # 编译
$env:CLASSNEX_VERIFY="1"; Start-Process "E:\ClassNex\src\ClassNex\bin\Debug\net10.0\ClassNex.exe"   # 运行+自检
```

- 仓库：https://github.com/ClassNex/ClassNEX （GPL-3.0，分支 `main`）
- 技术栈：**.NET 10 + Avalonia 11.3.12 + AvaloniaFluentUI 1.0.3 + YamlDotNet 16.3.0**
  （2026-10-05 已从 FluentAvaloniaUI 迁到用户指定的 AvaloniaFluentUI，见 5.6）
- 数据目录：`<exe 同目录>\data\`（`settings.json` + `timetable.yaml`）
- **UI 语言全部中文**，注释也全用中文
- 调试开关（都在 Debug 构建生效）：
  - `CLASSNEX_VERIFY=1` 写 `_verify.log` + 跑数据自检 —— **只启动浮窗，不会弹设置/档案编辑器**
  - `CLASSNEX_VERIFY_WINDOWS=1` 额外打开「应用设置 + 档案编辑器」（一般情况下不需要，别加）
  - `CLASSNEX_VERIFY_PAGE=about|widgets|appearance|window|basic|account` 打开设置窗口并停在指定页
    （设了它就等于开了 `CLASSNEX_VERIFY_WINDOWS`）
  - `CLASSNEX_VERIFY_SEARCH=1` 模拟「输入关键字 → 点搜索结果」（同样会开设置窗口）
  - `CLASSNEX_VERIFY_SHOT=1` 等 3 秒把**已存在的**窗口各自渲染成 `_shot_*.png`（不受遮挡影响）
  - `CLASSNEX_VERIFY_LAYOUT=1` 每秒把窗口/岛/课表当前项的实测尺寸写 `_verify_layout.log`
  - `CLASSNEX_VERIFY_PILL=1` 强制显示最后 60 秒的倒计时胶囊（核对排版用）
  - `CLASSNEX_VERIFY_EDITMODE=1` 启动后直接进入编辑模式（核对组件工具条排版）

---

## 1. 项目定位

参照成熟课表软件的交互与度量做的桌面课表浮窗：
主界面悬浮课表 + 应用设置 + 档案编辑器 + 系统托盘，四模块。

**铁律（用户明确定的）**：
1. **什么都先参照 CI** —— 去翻 CI 的**文件/源码**，不要只看截图猜；
   CI 没有就用类似的，最后才自己想。
2. **ClassIsland 一律叫 "CI"**。
3. 界面**只用 AvaloniaFluentUI（Fluent）控件**，不要引入别的 UI 风格、也不要再回 FluentAvaloniaUI。
4. 配色/尺寸不要自己发明 —— 优先用 CI 配置文件里的值。

---

## 2. CI 参考资料的权威来源

CI 完整安装包在（**已 gitignore，139MB，不要提交**）：
```
E:\ClassNex\ClassIsland_app_windows_x64_full_folder (2)\
```

**主界面参数全在 `data\Settings.json`**，关键值（已全部抄进本项目）：

| CI 配置项 | 值 | 本项目对应 |
| --- | --- | --- |
| `Scale` | **1.9** | `AppSettings.MainWindowScale`（默认 1.9） |
| `WindowDockingLocation` | 1（屏幕顶部） | `MainWindow.EnforcePlacement()` |
| `WindowDockingOffsetX/Y` | 0 / 0 | 贴顶无偏移 |
| `Opacity` | 0.5 | `AppSettings.BackgroundOpacity` |
| `RadiusX/Y` | 8 | `CiPalette.CardCornerRadius` |
| `BackgroundColor` | `#000000FF` | `CiPalette.CardBackground` |
| `MainWindowFont` | `#HarmonyOS Sans SC` | 已换为小米 MiSans（Regular+Bold 真字重）|
| `MainWindowSecondaryFontSize` | 14 | `WidgetBase.CiSecondary` |
| `MainWindowBodyFontSize` | 16 | `WidgetBase.CiBody` |
| `MainWindowEmphasizedFontSize` | 18 | `WidgetBase.CiEmphasized` |
| `MainWindowLargeFontSize` | 20 | `WidgetBase.CiLarge` |
| `IsMouseInFadingEnabled` | **true** | 鼠标移入淡化（**移入淡到 0.05、100ms 线性过渡、30ms 轮询近似即时判定**）|
| `IsMouseClickingEnabled` | false | CI 默认**不做**穿透（本项目默认开，用户要求） |
| `IsProfileEditorClassInfoSubjectAutoMoveNextEnabled` | true | 选完科目自动移到下一课 |

**组件布局参数在 `data\Config\ComponentLayouts\Default.json`**：

```json
"FadeCompletedClasses": true,            // 已上完的课程淡化（已实现）
"HideFinishedClass": false,
"PlaceholderTextNoClass": "今天没有课程。",
"PlaceholderTextAllClassEnded": "今日课程已全部结束。",
"CustomCornerRadius": 8, "BackgroundOpacity": 0.5, "BackgroundColor": "#000000FF",
"LastWidthCache": 80      // 日期组件宽
"LastWidthCache": 564.8   // 课程表组件宽（×1.9 = 卡片宽，说明卡片是内容自适应）
```

**配色结论（踩过坑，别再犯）**：
- CI 主题 `XamlThemes/FluentTheme/Styles.axaml` 里**没有任何硬编码颜色**，
  只引用 FluentAvalonia 资源键 → **CI 的强调色 = 跟随 Windows 系统强调色**。
- 本项目 `CiPalette.cs`：`CustomAccentColor = null` + `PreferUserAccentColor = true`。
- 只保留 CI 真正写死的值：卡片 `#000000`@0.5/圆角8、`#7FFFD4`、阴影 `#48000000`/`#66000000`、
  `#333333`、`#F4EF74`。
- ⚠️ **教训**：曾把 CI 截图的选中色 `#589499` 当 CI 固有强调色（其实是用户 Windows 强调色
  `#459BAC` 经 Fluent 变体算出来的），把 `#292D2E` 当 CI 表面色（其实是 Fluent 深色底叠 Mica 壁纸透色）。
  **软件截图的颜色是「系统色 + 壁纸透色 + 透明度」的合成结果，判断上游默认值必须回到源码/配置文件。**

---

## 3. 当前已完成的功能

### 主界面（悬浮课表）
- **结构 = CI 原样**：`CardHost`(淡入) → `HoverHost`(悬停淡化) → `LayoutTransformControl RootScale`
  （`ScaleTransform = MainWindowScale×FontScale`，默认 1.9，**CI 的 RootLayoutTransformControl**）
  → `IslandBackground`（CI `.line-background`：高固定 40=IslandContainerHeight、圆角 8=RadiusX、
  1px 描边 ControlElevationBorderBrush、阴影 `0 4 8 2 #48000000`、背景 SolidBackgroundFillColorSecondaryBrush、
  透明度 BackgroundOpacity）→ `CardContent`（CI GridContentRoot：Margin="12 0"、MinWidth=20）
- ⚠️ **三个必须记住的坑（都踩过）**：
  1. 岛宽 = **内容宽 + 两侧各 12**（CI 的 `BackgroundWidth` 是含 `Margin="12 0"` 的 GridWrapper 宽度）；
     只按内容宽设 → 文字顶到岛边缘、没有内边距
  2. 进度条必须 **`MinWidth = 0`**（CI 源码里也写了这句）：主题 ProgressBar 默认 MinWidth=200，
     会把当前课项撑到 200 宽，文字与后一项之间出现 ~200px 假空档
  3. 岛**不能加 `ClipToBounds`** —— 会把 `BoxShadow` 裁掉（阴影不渲染）
- **取色全部跟随系统**：主题明暗 = `ThemeMode=system`（`PreferSystemTheme`）、强调色 = 系统强调色
  （`CustomAccentColor=null` + `PreferUserAccentColor=true`）。因此**组件文字必须跟随主题取色**
  （`WidgetBase.White()` 现在返回主题 `TextFillColorPrimaryBrush`：深色主题=浅字、浅色主题=深字），
  写死白色会导致浅色主题下白字看不见
- **进度条轨道**用 `CiPalette.ProgressTrackBrush()`：**只给 6% 对比**（深色主题 = 白 6%、浅色 = 黑 6%）。
  CI 的 FluentAvalonia 轨道几乎和卡底同色（填充段之后直接就是卡底），AvaloniaFluentUI 的默认轨道
  偏亮，会在青色填充后面露出一截「浅色白边」—— 之前压到 0.35 仍嫌亮，现在 0.06 基本看不见。
- **倒计时胶囊**：CI 源码里背景层有 `Height="{Binding Bounds.Height, ElementName=BorderStroke}"`，
  **这一句必须有** —— 漏了它背景层只有 padding 高（6px），会在胶囊正下方露出一截小线条
  （见 `ScheduleWidget.BuildCountdownPill` 的 `bg.Height = height`）
- **文字渲染用灰度抗锯齿**（`RenderOptions.SetTextRenderingMode(block, TextRenderingMode.Antialias)`，
  见 `WidgetBase.Text` 和胶囊文本）：默认的亚像素渲染会在笔画边缘产生彩色毛边（看着像「像素点」），
  在半透明卡片上尤其明显。⚠️ Avalonia 11.3 的 `RenderOptions` **没有**可供 XAML Setter 用的
  `TextRenderingMode` 附加属性字段（写进样式会编译报错 AVLN2000），只能在代码里逐个控件设置。
  实测：日期区域 1621 个文字像素中彩色边缘 **0 个**（修前 13~17%）。
- **日期组件对齐 CI 的 `DateComponent`**：CI 里它就是一个普通 TextBlock
  （`StringFormat="{0:ddd MM/dd}"`），字号 = 继承的 `MainWindowBodyFontSize`（16）、字重 Normal。
  本项目此前是 22/SemiBold，已统一为 **Body 16 + Normal**（否则比课表项还重、看着字号不统一）。
- **窗口固定为整屏宽**（CI 的做法）：`SizeToContent="Height"` + 代码里设 `Width = 主屏工作区宽/缩放`。
  内容宽度变化时窗口本身**不缩放、不重新居中**，只有岛在窗口内平滑变化 ——
  否则剩余时间每秒变宽变窄，窗口跟着一抖一抖（实测：修前时间文本宽 41.6↔40.0、岛宽 648.0↔646.4、
  窗口宽 1231.2↔1228.8；修后三者全部恒定）。
- **数字用等宽特性 `tnum`**（`TextElement.SetFontFeatures(block, TabularFigures)`，见 `WidgetBase`）：
  MiSans 的数字是比例宽度（"1" 比 "4" 窄），剩余时间每秒变化会让文本宽度变来变去 ——
  这就是窗口抖动的根因。
- **悬停淡化按「岛」判定**（不是窗口）：窗口现在是整屏宽，用窗口矩形会把整条屏顶都算成悬停。
  用 `IslandBackground.PointToScreen` 取岛的屏幕矩形（对应 CI 的 `GetMouseStatusByPos` 判组件行自身）。
- **主界面全局字重 = Medium(500)**（CI 源码 `Settings._mainWindowFontWeight2 = (int)FontWeight.Medium`，
  用户 CI 配置里也是 `"MainWindowFontWeight2": 500`；CI 挂在 ZoomBorder 的 `TextElement.FontWeight` 上下发）。
  本项目：`LayoutTransformControl TextElement.FontWeight="Medium"` + `WidgetBase.Text()` 默认字重 Medium
  （当前课名仍是 Bold，与 CI 一致）。这就是「我们的字比 CI 细」的原因。
- ⚠️ **MiSans 字体已做过二进制修补**（脚本 `_tools\fix_misans_weights.ps1`，重新下载字体后必须重跑）：
  官方 ttf 的 `usWeightClass` 非标准（Regular 330 / Medium 380 / Demibold 450 / Bold 630），
  且 Medium/Demibold 的**族名是独立的**（"MiSans Medium"/"MiSans Demibold"）→ Avalonia 永远匹配不到
  500/600。脚本把 name 表重建成 `MiSans` + `Medium`/`Demibold`、字重改成标准 500/600、并重算校验和。
  验证：`_verify.log` 的字体探针应显示 `MiSans Medium = MiSans / weight Medium`。
- **主题切换要重建组件**：组件文字画刷是「创建时按当前主题取色」，不重建就会保持旧主题颜色
  （表现为「切深浅色字体不变黑白」）。`MainWindow` 里挂了 `ActualThemeVariantChanged` →
  `ApplySettings + RebuildWidgets + RefreshWidgets`
- **编辑模式**：右键浮窗 →「编辑主界面（编辑模式）…」进入；
  每个组件上方出现**它自己的工具条**（名称 + ⚙ 设置 + 🗑 删除 + ⋯ 上移/下移），
  末尾是「＋ 添加组件」，底部栏 = 添加组件 / 组件设置… / ✓ 完成。
  编辑模式下**不淡化、鼠标不穿透**（否则点不到按钮）；代码见 `Views/MainWindow.EditMode.cs`
- **组件内只用 CI 原值**（18/14/16/20 字号、16/10 间隔、8/2 胶囊内边距、1px 描边……），
  整体缩放交给 LayoutTransformControl —— 不再在各组件里 ×scale
- 课表项语义 = CI LessonsListBox：当前项(上课/课间)=全名 Bold 18 + 剩余时间 + **进度条贴项底**
  （项高=岛高 40，进度条即落在岛底边）、其他课=简称 18、**已上完淡化 0.6 带 150ms CubicEaseInOut 过渡**、
  课间项隐藏（仅当前课间显示「课间休息」）
- **动画参数**：启动/显示淡入 250ms `0.25,1,0.5,1`；岛宽变化 300ms `0.65,0,0.35,1.0`
  （CI BackgroundWidth；Avalonia 的 Width 过渡在 LayoutTransformControl 内会卡布局，改用 composition Scale 补间）；
  组件位移动画 = 移植的 `Controls/WrapPanelResizingAnimationAssist.cs`；进度条数值 50ms；悬停淡化 0.05/100ms
- **强制置顶**（每秒自检 + 失焦重声明）、贴屏幕顶部水平居中（窗口比屏宽时向两侧对称溢出，岛保持居中）、**不可拖动**
- **鼠标穿透**（Win32 `WS_EX_TRANSPARENT`）+ **鼠标移入淡化**（淡到 0.05、100ms 线性过渡；
  因穿透后收不到鼠标消息，用 30ms 轮询 `GetCursorPos` 近似 CI 的 RawInput 即时判定 → `Services/WindowsOverlay.cs`）
- 黑底 50% 不透明、圆角 8

### 应用设置（FluentUI：`NavigationView` + `SettingsExpander` + `InfoBar`）
- 通用：单周开始日期、托盘点击行为
- 界面：主题、背景不透明度、**主界面缩放（默认 1.9）**、全局字号缩放、排列方向、鼠标穿透
- 主界面组件：**组件库卡片网格**，7 种组件
  （文本 / 分割线 / 课程表 / 日期 / 时钟 / 当前·下节课 / 倒计时），
  支持启用、字号、显示秒、自定义文本（占位符 `{date} {day} {time} {parity} {current} {next} {countdown}`）、排序、删除
- 课表、关于
- **账户**：Win11 默认账户头像（`Assets\avatar.png`，取自系统 user.png）+ 用户名 + 邮箱（可编辑，
  存 `AppSettings.UserName/Email`）；导航栏账户卡 + 搜索框布局**模板取自 FluentUI-Gallery**（zhuzichu520 的 Qt/QML 版）

### 档案编辑器
- **课表页**：可视化周课表，**点空格 → 点科目直接排课**（含「选完科目自动移动到下一个课程」）、
  增删改课程、每周/单周/双周 + 单双周视图切换
- **时间表页**：**可视化时间轴**（上课=强调色块、课间=中性块），增删/上移/下移，
  改时间**自动同步课表**，可从课表重建（`TimeLayout.FromProfile` 会自动补课间块）
- **科目页**：表格（科目名 / 简称 / 户外课程 / 科任老师），`Subject.IsOutDoor` 对齐 CI
- **调课页**：占位，未实现

### 其它
- 系统托盘（显示/隐藏主界面、编辑档案、应用设置、退出）
- CSES YAML 课表导入/导出；内置 24 科目 / 7 天 / 每天 11 节的示例课表
- 内置 **MiSans**（小米字体，免费商用，授权见 `Assets\Fonts\LICENSE.txt`；Regular/Bold 两字重）
  ⚠️ MiSans 官方 ttf 的 OS/2 `usWeightClass` 是**非标准值**（Regular=330 / Bold=630），
  Avalonia 按 50 步进匹配字重会全部回退成 Bold。本项目已把这两个文件修补为 400/700 并重算
  checksum —— **以后若替换字体文件，必须重新修补**，否则界面会全部变粗。

---

## 4. 待办 TODO（按建议优先级）

| 优先级 | 功能 | 说明 |
| --- | --- | --- |
| P0 | **设置/组件页继续对齐 CI** | 现在结构已照 CI（SettingsExpander 行 + 横向组件条 + 组件库/组件设置标签），还差 CI 的「高级设置 / 行设置」两个标签、拖拽排序 |
| P0 | **调课 / 临时调课** | CI 的 `IsSwapMode: true`；只覆盖某一天，不动基础课表 |
| P0 | **明日课表** | CI `TomorrowScheduleShowType`；能顺带把主界面宽度拉到 CI 的水平 |
| P1 | **档案编辑器：双击单元格弹窗改课** | CI `ScheduleDataGridCellControl` 的 `IsEditPopupOpen` + 格内 Popup |
| P1 | **容器类组件** | CI 组件库有 轮播容器 / 滚动容器 / 分组容器 / 堆叠容器 |
| P1 | **天气简报组件** | CI 有；需要接天气数据源 |
| P1 | **多套组件「配置方案」** | CI `CurrentComponentConfig` / `ComponentLayouts\*.json` |
| P2 | **全面审查文字溢出** | 已修「关于页 header / 组件卡片 / 搜索框」；还需逐页过一遍（界面页 Description、课表页长路径、编辑器长科目名） |
| P2 | 主界面编辑模式 | CI `HasEditModeTutorialShown` / 编辑模式 |
| P2 | 多显示器支持 | 目前固定主屏 |
| P3 | 通知/提醒（上课铃等） | CI 有完整通知系统 |

---

## 5. 环境与工程注意事项（踩过的坑）

### 5.1 Git 推送必须开代理
```powershell
# 代理已持久化在仓库级配置里，确保 VPN 开着即可
git config http.proxy    # → http://127.0.0.1:7890
git config https.proxy   # → http://127.0.0.1:7890
git push origin main
```
直连 github.com:443 会超时（curl 28）。

### 5.2 ⚠️ 不要用 PowerShell 的 `Set-Content` 改源文件
曾经用
```powershell
(Get-Content x.csproj -Raw) -replace ... | Set-Content x.csproj
```
把 `ClassNex.csproj` 写坏（**中文注释变乱码，read 工具直接报 invalid UTF-8**）。
**改文本文件一律用编辑工具**；万不得已用 pwsh 时必须显式指定编码：
```powershell
[System.IO.File]::WriteAllText($p, $content, (New-Object System.Text.UTF8Encoding($false)))
```

### 5.3 ⚠️ 本机 DSH 的点击/截图工具不可靠
会发生「幽灵点击」：乱点控件、把窗口最小化、甚至**改掉设置里的值**
（曾把 `FontScale` 从 1.0 改成 0.9，导致好几次测量结论失真）。
**验证方式优先用「环境变量驱动的自检」**：
```powershell
$env:CLASSNEX_VERIFY = "1"
Start-Process "E:\ClassNex\src\ClassNex\bin\Debug\net8.0\ClassNex.exe"
Remove-Item Env:\CLASSNEX_VERIFY
Get-Content "E:\ClassNex\src\ClassNex\bin\Debug\net8.0\_verify.log"
```
`App.axaml.cs` 里 DEBUG 分支会写 `_verify.log` 并顺带打开全部窗口。
pwsh 控制台看中文会乱码（**只是显示问题，值本身是对的**）。

### 5.4 FluentAvalonia 2.4.0 的 API 与直觉不同
- `NavigationViewItem` **没有 `Icon`** 属性，要用 `IconSource`（`ui:FontIconSource`），
  否则报 `AVLN2000`
- `ui:ToggleSwitch` 的 `x:Name` **不会生成字段**（XAML 能编译但拿不到引用）
  → 布尔项改用 CheckBox，或用 `PropertyChanged` 判 `e.Property.Name == "IsChecked"`
- 存在的控件：`NavigationView` / `SettingsExpander` / `TabView` / `NumberBox` / `InfoBar` /
  `ContentDialog` / `FAComboBox` / `SymbolIcon`(+`Symbol` 枚举) / `FontIcon` / `ItemsRepeater`
- 图标用 `FontIcon` + 字形码更稳（`\uE713` 设置、`\uE787` 日历、`\uE710` 添加、`\uE74D` 删除…）

### 5.5 Avalonia 的坑
- `TrayIcon` 的事件是 **`Clicked`**（不是 `Click`）
- XAML 里设 `SelectedIndex` 会**过早触发** `SelectionChanged`（此时 `x:Name` 字段还没赋值）
  → 一律在构造函数里 `InitializeComponent()` 之后用代码设
- ⚠️ **`Background = null` 的控件不参与命中测试**：可点击的格子/Border 未选中态必须给
  `Brushes.Transparent`（不能给 null），否则「格子点不动、编辑不了」
- ⚠️ **不要用 `ListBox.SelectionChanged` 做「点一下执行一次」的动作**：点已选中项不触发。
  改为在 ListBox 上 `AddHandler(PointerReleasedEvent, handler, RoutingStrategies.Tunnel)`，
  再沿 `e.Source` 的视觉父链找 `DataContext`（CI 的做法）

### 5.6 ⚠️ AvaloniaFluentUI（现用 UI 库，2026-10-05 迁移后新增的坑）

**它其实是 FluentAvalonia 的 fork**（README 明说），所以控件名基本同名：
`SettingsExpander` / `NavigationView` / `CommandBar` / `InfoBar` / `TabView` / `NumberBox` / `FontIcon(Source)` / `IconSourceElement` 都有。
迁移 = **改命名空间**：`FluentAvalonia.UI.Controls` → `AvaloniaFluentUI.Controls`；
主题 `FluentAvalonia.Styling.FluentAvaloniaTheme` → `AvaloniaFluentUI.Styling.FluentAvaloniaTheme`。
**它只提供 `net10.0`**（1.0.3 依赖 Avalonia 11.3.12），所以必须装 .NET 10 SDK。

- ⚠️ **图标必须显式指定字体，且只能用 `Segoe MDL2 Assets`**：
  Win11 的 `Segoe Fluent Icons`（`SegoeIcons.ttf`）**删掉了一批旧字形码**
  （`E51E`/`E06F`/`E9E4`/`EBAC`/`E304` 等会显示成**空方块**）；
  `segmdl2.ttf`（Segoe MDL2 Assets）码位齐全。另外 FontIcon/FontIconSource **不设字体就会继承全局 MiSans** → 全乱码。
  正确写法：`FontFamily="Segoe MDL2 Assets"`。
  **验证字形是否存在的方法**：PowerShell `PrivateFontCollection` + `Graphics.DrawString` 把候选码画成 PNG，再 `read_image` 自己看（见 `_tmp_glyphs*.png` 的做法）。
- ⚠️ **`TryFindResource(key)` 不带主题变体时返回的是「浅色变体」的值**
  （`CardBackgroundFillColorDefaultBrush = #b3ffffff` = 白），深色界面里会画出**白卡片（发白）**。
  要么 `TryFindResource(key, ActualThemeVariant, out v)`，要么直接按 `ActualThemeVariant` 给色。
- ⚠️ **`NavigationView.SelectedItem` 是内部包装对象**：`MenuItems.IndexOf(SelectedItem)` 恒为 -1。
  按 `((NavigationViewItem)SelectedItem).Content as string` 匹配页面名。
- ⚠️ **在 `SelectionChanged` 事件内部清空 ItemsSource 会闪退**：把导航/清理用
  `Dispatcher.UIThread.Post(..., DispatcherPriority.Background)` 推迟到事件处理之后。
- ⚠️ **每秒刷新时 `Clear()+重建` 会让进度条闪烁**：结构不变时只更新 `ProgressBar.Value`，不要重建控件树。
- ⚠️ **布局**：同一 Grid 里「固定高度控件 + 内容会变的控件」同层时，后者展开会把前者挤走
  （搜索结果曾把搜索框顶下去）→ **拆成独立层**，各自的 `VerticalAlignment="Top"`。

### 5.7 验收方式：可以自己截图核对（模型支持图像输入）

不用再让用户当眼睛：
1. `app_list` 拿窗口 `windowId`（= HWND）；
2. PowerShell `Add-Type -AssemblyName System.Drawing` + `user32.PrintWindow(hwnd, hdc, 2)`
   —— **被其它窗口盖住也能抓到**（`CopyFromScreen` 会抓到前台的别的程序）；
3. 存 PNG 后用 `read_image` 自己看。
另外 `screen_read`（a11y）能拿到元素 bounds，用来量位置/颜色都很可靠。

---

## 6. 工具脚本 `_tools\`


| 脚本 | 用途 |
| --- | --- |
| `make_sample_timetable.py` | 按 CI 截图还原示例课表，生成 `Assets\timetable.yaml` |
| `measure_overlay_gap.py` | 对截图做像素分析，量卡片位置/宽度/距顶（对照 CI） |
| `read_font_names.py` | 读 TTF 的 name 表，取字体家族名（Avalonia 的 `avares://...#Family` 要用准） |
| `find_ci_default_color.py` | 扫描 CI 程序集提取内嵌色值 |
| `analyze_ci_palette.py` | 对 CI 截图做色彩直方图统计 |
| `make_icon.py` | 生成 `Assets\icon.ico` |

---

## 7. 编译与打包

### 日常调试
```powershell
dotnet build src\ClassNex\ClassNex.csproj -c Debug
```
目标：**0 错误 0 警告**（一直是这个标准）。产物在 `bin\Debug\net10.0\`。

### 出测试包（**自包含**，免装运行时，~120MB → zip ~55MB）
```powershell
$out = "E:\ClassNex\build\ClassNEX-26w41c-Alpha-win-x64"
dotnet publish src\ClassNex\ClassNex.csproj -c Release -r win-x64 --self-contained true `
  -p:DebugType=none -o $out
Remove-Item "$out\data" -Recurse -Force          # ⚠️ 必删，否则把本机配置/课表打进去
Remove-Item "$out\使用说明.txt" -Force -ErrorAction SilentlyContinue   # 用户不要使用说明
Compress-Archive -Path "$out\*" -DestinationPath "E:\ClassNex\build\ClassNEX-26w41c-Alpha-win-x64.zip"
```

### 发布到 GitHub Releases（一条命令）
`_tools\gh_release.py` 会自动：删同名旧 release → 建预发布 → 上传 zip。
```powershell
git tag -f 26w41c_Alpha ; git push -f origin 26w41c_Alpha
$cred = "protocol=https`nhost=github.com`n" | git credential fill
$env:GITHUB_TOKEN = ($cred | Select-String '^password=').Line.Substring(9)
$env:HTTPS_PROXY="http://127.0.0.1:7890"; $env:HTTP_PROXY=$env:HTTPS_PROXY
$env:GH_TAG="26w41c_Alpha"; $env:GH_TITLE="ClassNEX 26w41c_Alpha"
python _tools\gh_release.py "E:\ClassNex\build\ClassNEX-26w41c-Alpha-win-x64.zip"
```
- 发布说明取自 `RELEASE_NOTES.md`
- ⚠️ **RELEASE_NOTES.md 只写「这一次」的更新**，不要带上一版（用户明确要求）
- ⚠️ **发布说明开头固定用 caution 警告块（原样照抄，一个字都别改，包括「26.0」和 Android 那句）**（用户模板，2026-10-07 起）：
  ```markdown
  > [!caution]
  > # 警告！请不要使用此版本
  >
  > 当前版本为 26.0 的早期技术预览版本，仅适用于开发者进行修复和技术性预览，不要在生产环境使用此版本。
  > 本版本的 Android 版本仍有大量功能未适配，仅作跨平台可行性验证，欢迎在 Issue 中汇报您使用时遇到的问题。
  ```
  **警告块之后直接写「## 更新内容」**，中间不要再加 NOTE 版本头（用户要求）
- ⚠️ **不要自己打包上传**，用户说打包才打包（他还没说完就别发）
- 历史发布 `26w41a/b/c_Alpha` 都保留，别删

---

## 8. 版本号

**测试版命名规则：`YYwWWa`**（按发布日期，与 Minecraft 快照同款）

- `YY` = 年份后两位（26 = 2026）
- `w` = week
- `WW` = 该年的第几周（ISO 周号）
- `a` = 当周发布的第几个快照（a、b、c…）

当前：**`26w41c`** = 2026 年第 41 周的第 3 个快照（`26w41a`/`26w41b`/`26w41c` 都已发布为预发布）。

落地方式：
- `ClassNex.csproj` → `<InformationalVersion>26w41a</InformationalVersion>`
  （**对外产品版本**，显示在文件属性「产品版本」与 设置→关于）
- `<Version>0.5.0</Version>` 保留给程序集数字版本用（`26w41a` 不是合法 SemVer）
- `<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>`
  —— 否则 MSBuild 会自动在版本号后面接 `+<git哈希>`
- 出包时 zip 名用同一版本号：`ClassNEX-26w41a-test-win-x64.zip`

---

## 9. 项目结构速查

```
src/ClassNex/
  App.axaml(.cs)          应用入口：FluentAvaloniaTheme、全局字体、托盘、静态窗口操作
  Styles/CiPalette.cs     颜色集中定义（全部来自 CI 或系统主题）
  Models/                 Subject / Course / Schedule / ScheduleProfile /
                          ClassTime / TimeLayout / CourseSlot / WidgetConfig / AppSettings
  Services/               ScheduleService / TimeService / TimeLayoutService /
                          WidgetService / WidgetRegistry / CsesCodec /
                          SettingsService / AppServices(组合根) / WindowsOverlay
  Widgets/                WidgetBase + 7 个组件 + WidgetFactory
  Controls/               TimetableGridBuilder（周课表网格）
  Views/                  MainWindow（浮窗）/ SettingsWindow / ProfileEditorWindow
  Assets/                 icon.ico / timetable.yaml / Fonts/（MiSans 字体+授权）
```

---

## 10. 记忆核心（memory-eternal）

本地知识库位于 `C:\Users\Lenovo\.dsh\memory-vault`，**禁止用文件工具直接读写**。

⚠️ **当前自动沉淀是坏的**：蒸馏调用报
`MISSING_CREDENTIAL llm-deepseek: no API key for provider route "deepseek-official"`。
需要配 `DEEPSEEK_API_KEY`（设置 → 记忆 → 用量/今日 可看日志）。
在修好之前，**跨会话的上下文靠这份文档 + 项目记忆工具（`save_lesson` / `query_memory`）**。

---

## 11. 明天开新会话时，直接这样跟 AI 说

> 读一下 `E:\ClassNex\HANDOFF.md`，然后我们继续做 ClassNEX。
> 记住：ClassIsland 简称 CI，什么都要先参照 CI 的文件/源码（在 `E:\ci-source`，已 clone）；
> 界面只用 **AvaloniaFluentUI** 控件（不是 FluentAvaloniaUI，见 5.6 的坑）；
> 图标字体只用 `Segoe MDL2 Assets`；改文件不用 PowerShell 的 `Set-Content`（会写坏编码）；
> 打包上传要等我说，发布说明只写这一版。
> 今天想做：<你要做的功能>
