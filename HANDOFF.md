# ClassNEX 续作指南（HANDOFF）

> 最后更新：2026-11-23　｜　工作分支桌面预览 **26w48b**（未打标签）　｜　最近已发布版本 **26w48a_Alpha**，上一版 **26w46d_Alpha**。
> 上线以 GitHub Release 中实际出现 Windows ZIP 为准；推送版本标签只会启动构建。`26w46d_Alpha` 及以前的发布保留。
> 恢复未发布的现有标签：`gh workflow run build-release.yml --ref main -f release_tag=26w48a_Alpha`。工作流从原标签编译并取发布说明，只在压缩之后读取派发提交中的 ZIP 校验器；不填标签只构建。已有发布不会被覆盖。
> 这份文档的目的：**第二天打开新会话，照着它就能无缝继续写。**

---

## 0. 一分钟上手

日常开发分支是 `main`。26w48a 的入口、托盘手势和版本复测见 [设置与托盘验收](docs/26w48a-settings.md)。主线此前从 `2949eff` 与 `aaba73d` 按功能合并，记录见 [主线验收](docs/mainline-desktop-acceptance.md) 与 [26.0 归档](docs/26.0-acceptance.md)。不用重置分支，下文各节保留历史记录。

26w48b 课堂通知与设置侧栏在 `codex/26w48b-notifications`，从 `09a6ada` 开始。运行 `./tools/preview_classroom.ps1 -ExpectedVersion 26w48b` 可打开匿名便携预览，详见[提醒规则与窗口验收](docs/26w48b-notifications.md)。不要因为版本号已更新就在 `main` 上打标签；本轮未要求发布或部署集控。

```powershell
cd E:\ClassNex
dotnet restore ClassNex.sln
dotnet build src\ClassNex\ClassNex.csproj -c Debug -t:Rebuild
./tools/review_desktop.ps1 -Round my-review
```

- 仓库：https://github.com/ClassNex/ClassNEX （GPL-3.0，分支 `main`）
- 技术栈：.NET 10、Windows App SDK 1.8（原生设置）、Avalonia 11.3.12 + AvaloniaFluentUI（透明课表浮窗、档案、OOBE、登录、托盘）、YamlDotNet 16.3.0。
- 数据目录：应用载荷的 `..\data\`，包含 `Settings.json`、`timetable.yaml` 等。要试改设置，请用隔离目录，不要复制真实的账户、密钥或集控配置。
- 正文使用中文；按照 `AGENTS.md`，仓库代码不添加注释。
- 调试开关（都在 Debug 构建生效）：
  - `CLASSNEX_VERIFY=1` 写 `_verify.log` + 跑数据自检 —— **只启动浮窗，不会弹设置/档案编辑器**
  - `CLASSNEX_VERIFY_WINDOWS=1` 额外打开「应用设置 + 档案编辑器」（一般情况下不需要，别加）
  - `CLASSNEX_VERIFY_PAGE=home|settings|basic|appearance|window|widgets|nex|notification|account|about` 打开设置窗口并停在指定页；`home` 是主页，`settings` 是设置总览
    （设了它就等于开了 `CLASSNEX_VERIFY_WINDOWS`）
  - `tools/verify_settings.ps1` 在匿名副本中验证原生设置和桌面浮窗，见 5.3。
  - `CLASSNEX_VERIFY_SHOT=1` 等 3 秒把**已存在的**窗口各自渲染成 `_shot_*.png`（不受遮挡影响）
  - `CLASSNEX_VERIFY_EDITMODE=1` 打开设置的组件管理页。
  - `CLASSNEX_VERIFY_NOTIFY=prepare|on|off|1` 触发课前、上课、下课或顺序提醒演示

---

## 1. 项目定位

参照成熟课表软件的交互与度量做的桌面课表浮窗：
主界面悬浮课表 + 应用设置 + 档案编辑器 + 系统托盘，四模块。

**铁律（用户明确定的）**：
1. **什么都先参照 CI** —— 去翻 CI 的**文件/源码**，不要只看截图猜；
   CI 没有就用类似的，最后才自己想。
2. **ClassIsland 一律叫 "CI"**。
3. 设置使用原生 WinUI 3；桌面课表使用恢复自 `26w46d_Alpha` 的透明 Avalonia 浮窗。档案、OOBE、登录和托盘继续使用 AvaloniaFluentUI。
4. 配色/尺寸不要自己发明 —— 优先用 CI 配置文件里的值。

---

## 2. CI 参考资料的权威来源

CI 完整安装包在（**已 gitignore，139MB，不要提交**）：
```
E:\ci-app\ClassIsland_app_windows_x64_full_folder\
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
| `MainWindowFont` | `#HarmonyOS Sans SC` | 全局 HarmonyOS Sans SC（Light/Regular/Medium/Bold）|
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

## 3. 历代功能与旧实现记录

以下是历代窗口、图标和动画的记录，不是现版规范。主线的原生设置与透明课表浮窗分属不同宿主，入口与项目边界见第 9 节。

### 主界面（悬浮课表）
- **结构**：`CardHost`(淡入) → `HoverHost`(悬停淡化) → `LayoutTransformControl RootScale`
  （`ScaleTransform = MainWindowScale×FontScale`，默认 1.9，**CI 的 RootLayoutTransformControl**）
  → `IslandBackground`（高 44、圆角 14、无外部描边、柔和投影、顶部高光 `IslandSheen`；
  表面色按主题取樱花渐变：浅色 `#FFFDFE → #FFF4F8 → #FFE6EF → #FFD8E6`，
  深色 `#2A1722 → #3A1E2C → #4A2434 → #562B3D`，整体透明度 = `BackgroundOpacity`）→ `CardContent`（Margin="16 0"）
- ⚠️ **三个必须记住的坑（都踩过）**：
  1. 岛宽 = **内容宽 + 两侧各 16**（`IslandContentMargin`）
  2. 当前课进度条改成了**自绘 2px 轨道 + 填充**（`ScheduleWidget` 的 `_progressTrack/_progressFill`），
     不再用 `ProgressBar`，因为主题 `ProgressBar` 默认 `MinWidth=200` 会撑宽当前课项
  3. 岛**不能加 `ClipToBounds`** —— 会把 `BoxShadow` 裁掉（阴影不渲染）
- **通知涟漪必须裁到岛形**：全屏顶层效果窗口里的涟漪从岛中心扩散，若不裁切会越过圆角在岛下方露出半圆。
  现在 `RippleEffect` 在播放时读取 `MainWindow.GetIslandScreenRect()`，按实际屏幕矩形和圆角建立 `RectangleGeometry` 裁切；
  颜色统一走 `CiPalette.SakuraAccentBrush()`。`GetIslandScreenRect()` 用岛的两个角 `PointToScreen` 计算尺寸，
  能正确包含 `LayoutTransform` 缩放和系统 DPI。⚠️ 不要改成直接给岛开 `ClipToBounds`。
- **课堂提醒（26w48b）**：课前准备、上课、下课、放学由独立通知服务调度，时序和界面见[课堂提醒与设置侧栏](docs/26w48b-notifications.md)。室内课沿用提前 3 分钟，室外有独立提前量。旧档案不默认开启放学提醒、声音或上传本地铃声。**项目禁用 TTS**，不影响麦克风识别和 NEX 唤醒。
  正式提醒优先于手动预览。课前/课间在有文案时每 10 秒上下翻页，课名取全称；没有下一节时不显示空白课程页。轨道从开场满格开始，按本次提醒的完整时长播放，不随换页重启。
  浮窗按文案、当前屏幕工作区和实际缩放测量，空间不足时课程及时间分行；不固定为 416 DIP。退出提醒后取**此时的课表内容**恢复背景大小，过期转场不得隐藏新提醒。
- **取色跟随主题，组件强调色改为樱花玫瑰**：主题明暗 = `ThemeMode=system`（`PreferSystemTheme`）；
  主界面表面由 `CiPalette.IslandSurfaceBrush()` 按 `IsDarkTheme()` 返回粉白/深玫瑰渐变，不再依赖
  `SolidBackgroundFillColorSecondaryBrush`（不带主题变体取资源会返回浅色变体，深色主题下卡片会发白）。
  悬浮组件的强调色统一走 `CiPalette.SakuraAccentBrush()` / `SakuraAccentGradient()`（浅色 `#D95C7C`、
  深色 `#F08AA3`），不再使用系统强调色；其余窗口仍跟随系统强调色。因此**组件文字必须跟随主题取色**
  （`WidgetBase.White()` 返回 `TextFillColorPrimaryBrush`：深色主题=浅字、浅色主题=深字），写死白色会在
  浅色主题下看不清。
- **组件样式**：课表项 = `Pill` 胶囊（高 29、圆角 8、7px 左右内边距、4px 间距），已上完项
  `Opacity=0.42` + 160ms 淡出；当前课项 = 高 36 的樱花强调色胶囊（18% 填充、46% 描边、圆角 10），
  底部内嵌 2px 进度线；倒计时 ≤60s 时换成**实心樱花渐变胶囊**（`OnAccentBrush` 文字）
- **日期组件**：左侧 3px 樱花强调色竖条 + 两行「周几 / MM/dd」+ 右侧细分隔线，
  不再是单行 `ddd MM/dd` 文本；字号 9.5 / 15，行高 11 / 18
- **进度条轨道**：自绘轨道用 `SakuraAccentTint(0.16)`，填充用 `SakuraAccentGradient()`，不经过主题 ProgressBar
- **倒计时胶囊**：樱花渐变底 + `OnAccentBrush` 文字，高 20、圆角 10、8px 内边距
- **文字渲染用灰度抗锯齿**（`RenderOptions.SetTextRenderingMode(block, TextRenderingMode.Antialias)`，
  见 `WidgetBase.Text` 和胶囊文本）：默认的亚像素渲染会在笔画边缘产生彩色毛边（看着像「像素点」），
  在半透明卡片上尤其明显。⚠️ Avalonia 11.3 的 `RenderOptions` **没有**可供 XAML Setter 用的
  `TextRenderingMode` 附加属性字段（写进样式会编译报错 AVLN2000），只能在代码里逐个控件设置。
  实测：日期区域 1621 个文字像素中彩色边缘 **0 个**（修前 13~17%）。
- **日期组件对齐 CI 的 `DateComponent`**：CI 里它就是一个普通 TextBlock
  （`StringFormat="{0:ddd MM/dd}"`），字号 = 继承的 `MainWindowBodyFontSize`（16）、字重 Normal。
  本项目此前是 22/SemiBold，已统一为 **Body 16 + Normal**（否则比课表项还重、看着字号不统一）。
  （2026-11-01 视觉重做后改为两行日期块，见上）
- **窗口固定为整屏宽**（CI 的做法）：`SizeToContent="Height"` + 代码里设 `Width = 主屏工作区宽/缩放`。
  内容宽度变化时窗口本身**不缩放、不重新居中**，只有岛在窗口内平滑变化 ——
  否则剩余时间每秒变宽变窄，窗口跟着一抖一抖（实测：修前时间文本宽 41.6↔40.0、岛宽 648.0↔646.4、
  窗口宽 1231.2↔1228.8；修后三者全部恒定）。
- **数字用等宽特性 `tnum`**（`TextElement.SetFontFeatures(block, TabularFigures)`，见 `WidgetBase`）：
  比例数字字体会让剩余时间每秒变化时改变文本宽度，因此统一启用等宽数字特性 ——
  这就是窗口抖动的根因。
- **悬停淡化按「岛」判定**（不是窗口）：窗口现在是整屏宽，用窗口矩形会把整条屏顶都算成悬停。
  用 `IslandBackground.PointToScreen` 取岛的屏幕矩形（对应 CI 的 `GetMouseStatusByPos` 判组件行自身）。
- **主界面全局字重 = Medium(500)**（CI 源码 `Settings._mainWindowFontWeight2 = (int)FontWeight.Medium`，
  用户 CI 配置里也是 `"MainWindowFontWeight2": 500`；CI 挂在 ZoomBorder 的 `TextElement.FontWeight` 上下发）。
  本项目：`LayoutTransformControl TextElement.FontWeight="Medium"` + `WidgetBase.Text()` 默认字重 Medium
  （当前课名仍是 Bold，与 CI 一致）。这就是「我们的字比 CI 细」的原因。
- **HarmonyOS Sans SC**：Light、Regular、Medium、Bold 四个字重原样嵌入，不修改字体文件；
  全局 `AppFontFamily`、设置窗口和 OOBE 的正文都使用该字体。图标必须单独指定与字形相配的图标字体，客户端设置窗口与其他窗口的约定见 5.6。
  验证：`_verify.log` 应显示四个字重均命中 HarmonyOS Sans SC。
- **主题切换要重建组件**：组件文字画刷是「创建时按当前主题取色」，不重建就会保持旧主题颜色
  （表现为「切深浅色字体不变黑白」）。`MainWindow` 里挂了 `ActualThemeVariantChanged` →
  `ApplySettings + RebuildWidgets + RefreshWidgets`
- **编辑模式**：右键浮窗 →「编辑主界面（编辑模式）…」进入；
  每个组件上方出现**它自己的工具条**（名称 + ⚙ 设置 + 🗑 删除 + ⋯ 上移/下移），
  末尾是「＋ 添加组件」，底部栏 = 添加组件 / 组件设置… / ✓ 完成。
  编辑模式下**不淡化、鼠标不穿透**（否则点不到按钮）；代码见 `Views/MainWindow.EditMode.cs`
- **组件内只用 CI 原值**（18/14/16/20 字号、16/10 间隔、8/2 胶囊内边距、1px 描边……），
  整体缩放交给 LayoutTransformControl —— 不再在各组件里 ×scale
- 课表项语义 = CI LessonsListBox：当前项(上课/课间)=全名 Bold + 剩余时间 + **进度线贴项底**
  （当前项高 36，进度线在胶囊内部底边）、其他课=简称胶囊、**已上完 `Opacity=0.42` 带 160ms CubicEaseInOut 过渡**、
  课间项隐藏（仅当前课间显示「课间休息」）；课间当前项使用较淡的强调色胶囊
- 当前项剩余时间统一显示为 `-m:ss`（超过一小时为 `-h:mm:ss`，不足一分钟为 `-0:ss`），
  不再使用「剩 x:y」或 `xs`。剩余时间使用 CI 次级字号 14、纯白文字、最小 46 宽和
  等宽数字，左边距 4；标题列占满剩余空间，时间列使用 Auto 宽并右对齐，时间末端贴课程胶囊右侧内边距，
  课程名与时间保持合理间隔；60 秒内继续切换为实心倒计时胶囊。
- **动画参数**：启动动画 = `LinePanel` 组合层 opacity 0→1（420ms，`0.16,1,0.3,1`）+ scale
  `(0.94,0.90)→(1.012,1.006)→(1,1)`（760ms，带回弹）；组件按 90ms + 45ms/个错峰淡入。
  组件不再用 composition `Offset` 做上浮位移（部分合成器/DPI 下会把子视觉叠到同一位置，表现为启动时组件堆在一起）；
  之后显示用 250ms `0.25,1,0.5,1` 淡入；岛宽变化 300ms `0.65,0,0.35,1.0`
  （CI BackgroundWidth；Avalonia 的 Width 过渡在 LayoutTransformControl 内会卡布局，改用 composition Scale 补间）；
  组件位移动画 = 移植的 `Controls/WrapPanelResizingAnimationAssist.cs`；进度线随每秒刷新；悬停淡化 0.05/100ms
- **档案编辑器页面切换动画**：`Styles/MetroTransitions.cs` 对一个页面面板的 Composition
  视觉层执行 220ms 淡入和 10px 纵向归位，cubic-bezier(0.1,0.9,0.2,1)；旧页停止动画后保持布局并隐藏，
  新页只创建一个整页视觉动画，不再给页面内每个卡片分别创建动画。动画目标必须是单个页面面板
  （不能是 PageScroll 滚动容器），否则整个内容加滚动条一起位移会产生上下抖动。
  组件页的“组件库 / 组件设置”切换使用 `Styles/WinUIPageTransitions.cs` 的
  `SlideNavigationTransitionInfo` 效果：前进从右侧进入、后退从左侧进入，300ms、
  cubic-bezier(0.13,0.96,0.22,1)，只动画新页面层。该类同时提供 Entrance、DrillIn 和 Suppress 模式。
- **强制置顶**（每秒自检 + 失焦重声明）、贴屏幕顶部水平居中（窗口比屏宽时向两侧对称溢出，岛保持居中）、**不可拖动**
- **主界面宽度变化**：扩展时立即把岛背景切到目标宽度，只有缩小时播放 300ms Composition Scale；
  否则新内容已按更大宽度排版，而背景仍从旧宽度放大，右端课程会短暂露出圆角背景之外。
- **鼠标穿透**（Win32 `WS_EX_TRANSPARENT`）+ **鼠标移入淡化**（淡到 0.05、100ms 线性过渡；
  因穿透后收不到鼠标消息，用 30ms 轮询 `GetCursorPos` 近似 CI 的 RawInput 即时判定 → `Services/WindowsOverlay.cs`）
- 浅色粉白樱花渐变 / 深色深玫瑰渐变玻璃底 + 不透明度设置 + 圆角 14 + 顶部高光 + 玫瑰色柔和投影

### 设置窗口与托盘菜单（历史实现）

26w48a 的设置根页使用 WinUI 分类行，子页有面包屑；通知区单击、长按、右键与键盘唤起同一菜单，不再按旧 `TrayClickBehavior` 切换窗口。下面的 Avalonia 设置外壳和旧点击行为仅为早期记录，不是当前实现。

- **范围和参考**：这里只改 ClassNEX 客户端设置窗口。CI 的 `E:\ci-source\ClassIsland\Views\SettingsWindowNew.axaml` 用于核对原有设置交互；外壳和布局按用户指定的 `D:\TubaWinUi3` 移植。首页参考 `DashboardPage`（不是工具分类的 `HomePage`），总览参考 `SettingsPage`。解出的 XAML 和原图只留在已忽略的 `build\captures` 作对照，不随源码或安装包分发。
- **窗口与导航**：`SettingsWindow.axaml` 使用 AvaloniaFluentUI `AppWindow` 和支持时的 Mica。标题栏高 48，返回/折叠导航、应用图标与标题、最大宽 320 的左对齐搜索框在同一行；系统窗口按钮保留独立区域。侧栏展开宽 200、收起宽 48，宽度低于 600 时默认收起。配色集中在 `CiPalette.Settings*.cs`，只对设置窗口生效，不更改桌面课表、提醒、OOBE 或集控。
- **主页 `home`**：顶部保留问候横幅和 ClassNEX 原生绘制的课表插画。未登录显示“Hello，请先登录”，已登录显示分时问候和账户名。仅首页卡片借鉴 Windows 设置风格：上方状态卡分为名称、当前状态和底部操作行，根据窗口宽度显示完整卡片；账户和课表读本地实际状态。下方的九个入口分成两组，整行打开，独立的星标负责收藏。窄窗时分组纵排；编辑大卡片、翻页、搜索与“全部设置 / 收藏”保留，偏好继续使用原来的 `data\SettingsHome.json`。实现见 `SettingsWindow.Home.cs`。
- **设置总览 `settings`**：从侧栏底部“设置”进入；依次显示应用标识、版本、链接和各个已启用分类。宽 172 的“跳转到分类”在同一纵向滚动区定位；单独打开“基本”“外观”“窗口”“组件”“NEX Intelligence”“提醒”“账户”“关于”时使用同一组真实控件，从顶部显示。调试分类仍须解锁。统一行高下限 76、圆角 8、行距 4、标题/说明字号 14/12；窄窗会把尾部操作移到文字下方，三条外观滑轨使用一致的可伸缩网格。样式见 `SettingsWindow.SectionStyles.axaml`。
- **切页与搜索**：`SettingsWindow.Navigation.cs` 按导航项的稳定 `Tag` 注册目标、返回历史和分类锚点，不按可见中文文本或顺序猜页面。顶栏搜索 (`Ctrl+F`) 来自真实设置标题/子项及少量同义入口，回车激活并可展开父组、选中组件或滚到目标控件；`Alt+左` 返回。启动只显示主页，首次不预热、不播放叠页动画；后续切页只做 167ms 不透明度过渡，切换分类重置滚动位置。
- **图标**：侧栏与主页共用本地 Microsoft Fluent UI System Icons Color 资源；设置行和标题栏使用 Windows 的 Segoe Fluent Icons，以 Segoe MDL2 Assets 回退。原版 Tuba 横幅及未确认授权的私有图标未纳入仓库，不宣称素材逐像素相同。授权和维护约定见 5.6。
- **托盘菜单**：`TrayMenuWindow` 改为常驻单例并在启动空闲时 `Prewarm()`，保留原生窗口句柄和布局缓存；
  打开前 `PrepareForOpen()` 清理旧帧，显示后按自下而上的 280ms/16ms 级联执行 Composition 动画。
  失焦、点菜单项、再次点托盘图标都走 `CloseAnimated()` 后 `Hide()`，不再关闭并重建窗口；托盘点击不再有 300ms debounce。
  菜单项的逐条 stagger 已移除，避免组合层偏移/关闭时出现半截列表。
- **欢迎设置窗口入场**：`OobeWindow` 保留 Fluent `AppWindow`，把首屏拆成四组：
  左侧欢迎文案（左上）、右侧插图（右上）、底部页点（左下）、底部按钮（右下）。
  `OnOpened` 延迟 450ms（等窗口真正显示）后用 16ms `DispatcherTimer` 跑 780ms：
  各组 `RenderTransform`（`TranslateTransform` + `ScaleTransform` + `RotateTransform`）
  从窗口四角带旋转/缩放飞入，透明度同步淡入；组间延迟 0/70/150/220ms。
  动画结束立即停掉计时器，`Closing` 也会停。不要用全屏透明 Window 承载这个效果：
  实测会先黑屏/白卡再重排，且元素容易在窗口显示前就播完。

### 应用设置（FluentUI：`NavigationView` + `SettingsExpander` + `InfoBar`）

- 通用：单周开始日期、托盘点击行为
- 界面：主题、背景不透明度、**主界面缩放（默认 1.9）**、全局字号缩放、排列方向、鼠标穿透
- 外观页的背景不透明度、全局字号缩放、主界面缩放使用相同的 `32,*,56,32` 控制区，滑轨按可用宽度伸缩；仅保留“标准缩放”快捷入口。设置与组件参数的布尔项仍统一为显示「开 / 关」的 Avalonia `ToggleSwitch`。
- 主界面组件：**CI 风格 WrapPanel 紧凑组件库**，7 种组件
  （文本 / 分割线 / 课程表 / 日期 / 时钟 / 当前·下节课 / 倒计时），
  每张卡片宽 225、使用 32px 图标、名称与说明，点击整卡添加；不再使用全宽示例卡和右侧操作区，
  支持启用、字号、显示秒、自定义文本（占位符 `{date} {day} {time} {parity} {current} {next} {countdown}`）、排序、删除
- 课表、关于
- **账户登录**：恢复 Casdoor **浏览器授权码流程**，软件内不再显示用户名密码或邮箱验证码表单。
  `AccountService` 使用本机 `HttpListener` 接收 `http://localhost:17171/callback` 回调，校验随机 `state`
  后用授权码换取令牌并同步头像、用户名、邮箱；浏览器回调页只提示「浏览器授权完成，正在同步」，
  最终成功状态由应用确认。Casdoor 的 `app-classnex` 必须允许该回调地址。
  登录窗口采用居中卡片式排版，窗口固定为 `520x560`，已验证 125% 显示缩放；关闭窗口会停止本地回调监听。
  OOBE 账户页与应用设置账户页共用该浏览器登录窗口。
  设置里的账户页只读展示账户服务返回的头像、用户名、邮箱和连接状态；**已删除本地更换头像、
  编辑用户名、编辑邮箱及对应文件选择/保存逻辑**。需要变更资料时前往账户服务中心，应用内只保留
  切换账户与退出登录。账户页由账户总览卡和浏览器安全授权说明两张卡组成。

### 档案编辑器
- **顶部栏**：保留档案编辑器原布局，左侧返回历史按钮、应用图标和标题，中间显示当前 CSES 文件路径，
  右侧显示版本号和“更多选项”；保存/打开课表/重新加载/导出 CSES 位于页面标题下方，不要再放回标题栏。
- **整体布局**：使用 `NavigationView`（`PaneDisplayMode=Left`，`OpenPaneLength=220`，透明内容背景），左侧固定为
  「档案信息 / 课表 / 时间表 / 科目 / 调课」，右侧统一显示 28px 页面标题和独立页面面板；不要再退回顶部 TabControl。
  旧调用 `SelectTab(0-3)` 已映射到课表/时间表/科目/调课，`档案信息` 仍可从左侧进入；档案编辑器继续使用 `MetroTransitions`，其布局与转场未随设置窗口改版。
- **档案信息页**：显示档案名称、CSES 路径、开学日期、周次规则，以及课程/科目/时间点数量；切回该页时重新汇总。
- **课表页**：可视化周课表，**点空格 → 点科目直接排课**（含「选完科目自动移动到下一个课程」）、
  增删改课程、每周/单周/双周 + 单双周视图切换
- **时间表页**：**可视化时间轴**（上课=强调色块、课间=中性块），增删/上移/下移，
  改时间**自动同步课表**，可从课表重建（`TimeLayout.FromProfile` 会自动补课间块）
- **科目页**：表格（科目名 / 简称 / 户外课程 / 科任老师），`Subject.IsOutDoor` 对齐 CI
  （表格行必须显式使用 `CiPalette.PrimaryTextBrush()`，未选中行背景必须为 `Brushes.Transparent`，否则文字不显示且无法点击）
- **调课页**：占位，未实现
- **任务栏图标**：`Program.Main` 在 Avalonia 启动前调用 `SetCurrentProcessExplicitAppUserModelID("ClassNex.ClassNEX.Desktop")`；
  不要移除该调用，否则 Windows 可能把窗口显示成通用任务栏图标。EXE 的 `ApplicationIcon` 与各窗口 `Icon`
  必须继续指向 `Assets/icon.ico`；该 ICO 由 `icon-1200.png` 加 18% 圆角生成（`icon-rounded-1200.png`），
  含 16/20/24/32/40/48/64/96/128/256 全套尺寸，圆角外为透明。

### 其它
- **Casdoor OIDC 提供商**：`ClassNEX/smart-teach` 已接入 `https://auth.smart-teach.cn`，类型为 OIDC，
  使用授权码流程和 PKCE，范围为 `openid profile email`；已绑定 `app-classnex`，允许登录、注册、解绑并按邮箱绑定。
  `auth.smart-teach.cn` 当前仍指向旧 Casdoor 源站，会导致 `invalid_client`。因此 ClassNEX 客户端
  使用 `AppSettings.DefaultAccountEndpoint = https://classnex.tech`，
  `SettingsService.Load()` 会把历史域名和新旧服务器 IP 自动迁移到当前可用源站。
  `stcn` 登录生成的 AccessToken 约 14 KB，公共 Nginx 会拒绝这么大的请求头。集控容器已加入
  `casdoor_casdoor-net` 并通过 `CASDOOR_INTERNAL_URL=http://casdoor:8000` 读取用户资料；
  桌面客户端改用集控服务的 `/api/v1/auth/profile` 代理读取标准账户字段，不再直接请求 Casdoor userinfo。
  `app-classnex` 的 Client ID 为 `fa7664d772fac4b5097ce15c`，`smart-teach` OIDC 提供商的
  Client ID 为 `913736707004a6f7c323`；Client Secret 只存在于 Casdoor 数据库，禁止写入仓库或交接文档。
  Casdoor 已从 `124.221.160.188` 迁移至 `206.187.201.60`，新机使用 Docker Compose 运行
  Casdoor、MariaDB、Nginx 和头像服务。`staticBaseUrl` 固定为相对路径 `/casdoor-static`，
  Nginx 将该路径直接反向代理到当前 Casdoor 容器的 `/static/`，不再使用容易过期的静态文件副本；
  因此 Casdoor 升级或重建容器后，图标、提供商图片和站点资源仍会跟随当前版本。
- **集控管理**：`src/ClassNex.Central` 是独立 ASP.NET Core 集控服务，默认端口 `7010`，
  `docs/CENTRAL_MANAGEMENT.md` 记录运行和 API。管理端和客户端统一使用 ClassNEX ID 登录，
  管理端通过 Casdoor 授权码与 PKCE 建立 HttpOnly 会话，客户端复用当前账户的 AccessToken；
  管理端会话写入持久化 `central-sessions.json`，默认保留 7 天，服务重启后仍保持登录。
  正式托管路径为 `https://classnex.tech/Control/`。Nginx 将 `/Control/` 去前缀代理到
  `classnex-central:8080`，并通过 `X-Forwarded-Prefix` 让 OAuth 回调动态生成
  `https://classnex.tech/Control/auth/callback`；网页 API 和登录入口使用子路径 Base。
  ⚠️ 域名根路径 `/` 必须继续交给 Casdoor，**禁止把 `/` 返回 `302 /Control/`**。`stcn`
  等第三方提供商登录后会先回到 Casdoor 的 `/callback`，再访问 `/` 完成 OAuth 授权码生成；
  根路径被集控占用时，浏览器会显示登录成功，但 Casdoor 授权链被截断，集控仍是 `401`。
  集控回调与退出登录都按 `X-Forwarded-Host`、`X-Forwarded-Proto` 和 `X-Forwarded-Prefix`
  返回 `/Control/`，不能写死 `/`。公开入口只使用 80/443 标准端口；`classnex.tech` 与
  `www.classnex.tech` 当前都只有 A 记录并指向 `206.187.201.60`，不要把访问依赖放在 7010/8000。
  桌面客户端只登记设备，不显示班级选择；管理端在“设备连接”页选择设备并指定班级。
  管理端客户端设置改为 WinUI 风格开关、下拉框、滑动条和文本输入，不再要求手写 JSON；
  课程表采用科目池与星期 × 节次网格，可点击格子排课、切换每周/单周/双周、直接修改或增删节次，
  保存时由服务端转换成 CSES YAML。进入“班级与课程”会自动打开第一个班级；
  左侧页面、客户端设置/课程表标签和每周/单周/双周切换都会将内容滚动位置重置到顶部。客户端每 2 秒拉取
  `/api/v1/client/assignment`，应用远端设置前保留本机集控连接参数，随后刷新主界面和课程。
  `AppServices.SaveSettings` 和课程表 `ScheduleService.Saved` 会触发客户端自动发布，
  经 `/api/v1/client/publish` 更新班级修订；集控网页每 2 秒检查修订并在没有未保存编辑时刷新。
  应用远端设置时额外保留账户端点、Client ID、用户名、邮箱、OOBE 和调试状态，避免本地设置被误判为退出登录。
  远端设置应用后会调用 `App.ApplyTheme`，深浅色与系统主题切换会立即重建主界面。
  集控网页读取设置时按不区分大小写匹配现有 JSON 键，避免 camelCase/PascalCase 生成重复主题字段。
  已移除管理员令牌、注册令牌和客户端令牌方案。历史设置中的空集控地址和旧服务器地址
  会在加载时迁移到 `http://206.187.201.60:7010`，避免旧安装只显示示例地址。
  ⚠️ 客户端集控地址现已统一为 `https://classnex.tech/Control`，旧版使用的 `7010`
  直接 IP 地址会在加载时迁移。公开客户端必须走标准 443 域名入口，不能再依赖美国源站 IP
  和非标准端口，否则校园网、移动网等环境会直接不可达。
  Casdoor 的 `AuthCallbackHandler.js` 还存在同域子路径判断问题：`method=signin` 时，
  位于同域 `/Control/auth/callback` 的合法 `response_type=code` 会被误判为普通站内登录并
  跳回候选站点根路径 `/`。线上通过 `/opt/casdoor/static/AuthCallbackHandler.js` 覆盖静态文件，
  Nginx 在 `/AuthCallbackHandler.js` 返回该补丁文件并设置 `Cache-Control: no-store`；
  补丁内容保存在 `deploy/casdoor/AuthCallbackHandler.patch`，Casdoor 升级后必须重新确认。
  国内网络入口使用旧腾讯云服务器 `124.221.160.188` 作为反向代理，最终内容仍由
  `206.187.201.60` 提供，`deploy/classnex-relay/nginx.conf` 保存代理配置。
  `classnex.tech` 与 `www.classnex.tech` 的 A 记录应指向国内入口时，境外源站地址不应直接公开给客户端。
- **NEX Intelligence（内置 AI Agent，2026-10-11 新增）**
  - 设置里新增「NEX Intelligence」页（导航项在「组件」之后）+ 独立对话窗（沉浸标题栏 + 云母）
  - 代码在 `Services\NexIntelligence\`：`NexAiClient`（OpenAI 兼容 /v1/chat/completions，流式 SSE；增量为 `delta.content` 与 `delta.reasoning_content`，工具调用分片在 `delta.tool_calls` 里按 index 拼装）、`NexAgent`（一轮 = 发消息 → 流式收正文/思考 → 执行工具 → 回填 → 继续，最多 8 轮）、`NexAgentTools`（六个工具：读应用状态/读课表/列目录/读文件/写文件/执行命令）、`NexModelCatalog`（拉模型列表/测连接）
  - 权限三档：**仅可查看**（只读工具）/ **工作区内修改**（可写 `SettingsService.DataDirectory` 里的文件）/ **完全权限**（任意路径 + `run_command`）。对话框顶栏随时切换，设置页设默认档
  - **全屏唤醒光效**：`Views/NexGlowWindow` + `Controls/NexGlowControl`。紧贴屏幕边缘的
    粉白光晕已改为 **44 圈连续渐变**（越靠内越透明、偏粉），不再是几条分层描边；
    150 片樱花花瓣从四边向内飘入，只走屏幕短边的 5%~15%，集中在边框附近。
    「请讲」浮窗在该模式下会 `SetWindowPos(HWND_TOPMOST)` + `Activate()`，确保压在
    其他置顶窗口（包括花瓣层）之上。设置页「NEX Intelligence」顶部有「预览跑马灯」按钮，
    原来的「测试跑马灯」也保留。
  - **NEX 会话收尾**：唤醒后 12 秒没有识别到语音会自动收起；语音超时、错误和无回答都会进入短超时后关闭。
    延迟显示“思考中”时会校验会话代次，取消后不会把旧浮窗重新贴出来。每次对话使用独立
    `CancellationTokenSource`，结束、取消或关闭聊天窗都会取消请求；聊天窗关闭后清空历史和工具卡。
    跑马灯结束后关闭全屏窗口，控制控件缓存花瓣几何与画笔，不再每帧创建数百个对象；
    会话结束时执行一次受控 GC 和工作集回收。实测唤醒时工作集约 374 MB，结束回收后约 89 MB。
  - **NEX 唤醒条**：窗口显示后额外设置 `WS_EX_TOOLWINDOW`，不进入 Alt+Tab；扫描高光使用
    2.2 秒 Composition 关键帧循环移动 `Offset` 和透明度，不再用 30ms UI 计时器逐帧计算
    正弦/余弦。悬停、闪烁和边框画刷均静态复用，颜色通过 `BrushTransition` 平滑过渡。
  - ⚠️ **接口密钥绝不写进源码/仓库**（仓库是公开的）：只在设置页填写，落盘到本机 `data\Settings.json`；新机器要重新填一次
  - 自检：`CLASSNEX_VERIFY_NEX=<文本>` 打开对话框并自动发一条消息；出图看 `_shot_nex.png`
- **档案编辑器 Metro 转场**：`Styles/MetroTransitions.cs` 使用 Avalonia Composition 硬件动画，不再用 UI 线程计时器；
  旧页以 `Opacity=0` 保留布局，新页整页只做一次 220ms 淡入和 10px 纵向归位，不再逐项错峰。
  `PrewarmPagesAtIdle()` 仅保留给档案编辑器。客户端设置窗口的内容宿主是纵向 `StackPanel#PageHost`，使用独立的导航与过渡，详见“设置窗口与托盘菜单”。
  `Styles/MetroTilt.cs` 给档案导航项、对应的长条卡片、托盘菜单项和 OOBE 按钮
  做按压倾斜：原点永久锁定 50%,50%，不修改任何布局属性；使用
  `TransformOperationsTransition`（按下 70ms / 回弹 180ms，cubic-bezier(0.1,0.9,0.2,1)），
  倾斜按长条阻尼公式 `skewY=dx*150/max(W,100)`、`skewX=-dy*0.6`、位移 1.5/0.8px、scale 0.985，
  不再逐帧计时，也不会把卡片整体拉下去。若模板内有名为 `TiltHost` 的内层 Border 会优先使用它。
  `MetroTransitions` 切页时旧页保留布局但关闭绘制和命中测试，新页由 Composition 动画进入；
  标题先隐藏，过渡完成后再写入新标题，避免「新标题 + 旧内容」同屏。
- 首次启动向导（OOBE，五页）：多语言问候 + 隐私/许可链接 + 语言选择 + 外观/开学日期/开机自启动 + 账户与更多设置预留页 + 烟花完成页；走完向导前不显示浮窗。
  页脚圆点已移除，顶部显示「第 n 步 / 共 5 步」；切页为 360ms 的 WP8.1 风格翻页
  （左外侧轴心、ScaleX 收拢 + skew，cubic-bezier(0.1,0.9,0.2,1)，两页交叉淡出；切换期间锁输入）。
  随时检查：**设置 → 关于 ClassNEX → 首次启动向导 → 预览 OOBE**；预览模式不会改
  `IsOobeCompleted`、不会重启，关闭预览也不会退出主程序。
- 系统托盘（左键弹菜单：显示/隐藏主界面、编辑档案、加载课表、换课、编辑主界面、应用设置、重启、退出）
- CSES YAML 课表导入/导出；**新激活不再内置示例课表**（示例课表只在老安装升级时播种，打包时还要删掉 `Assets\timetable.yaml`）
- 内置 **HarmonyOS Sans SC** 的 Light、Regular、Medium、Bold 四种字重，授权文件为
  `Assets\Fonts\HarmonyOS-Sans-LICENSE.txt`，随构建和发布复制。

---

## 4. 待办 TODO（按建议优先级）

| 优先级 | 功能 | 说明 |
| --- | --- | --- |
| P0 | **🖼 首次启动向导的插图换掉** | 现在用的是希沃白板那张照片（用户 2026-10-11 明确：**下个版本替换掉**）。位置在「欢迎」页右侧（`OobeWindow.axaml` 里 `Border + ImageBrush`，`Margin="445,121,0,23"`），换成官方设备渲染图或插画 |
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
| P3 | 提醒音效/铃声 | 当前已支持遮罩、浮层和三类课堂事件；项目禁用 TTS，自定义音效与音量仍待补齐 |

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

### 5.3 隔离验证客户端设置

全量构建后运行 `tools/verify_settings.ps1`。它根据 MSBuild 输出清单复制应用文件，在 `build/verify-native-<随机标识>/data` 创建匿名配置与示例课表，然后用 `--verify-native` 启动相邻的独立 `app/ClassNex.exe`。不要复制日常用户的 data、插件、凭据或密钥。脚本不会停止普通客户端。

检查结果在该 `data/_native_verify.log`。只有逐项 `PASS` 且以 `RESULT PASS` 结束才算通过；`-KeepOpen` 可保留该测试窗口。`CLASSNEX_VERIFY_SETTINGS`、旧的 `_settings_verify.log` 和旧设置窗口的截图流程已经退役。

同目录的 `native-home.png`、`native-reminders-collapsed.png` 来自原生设置；`_native_navigation_metrics.json` 记录一次切页，不作为帧率基准。透明课表的截图和拖动、双击、穿透、重启检查由 `tools/review_desktop.ps1` 生成。成功后脚本保留报告、截图和数据；`tools/verify_settings.ps1 -KeepFixture` 可保留独立测试程序。匿名验收不会连接线上集控，另需现场联测才能断言网络可用。

`XamlControlsResources` 要在 `DesktopApplication.OnLaunched` 加载，不能在应用构造函数中加载。自包含发布必须保留 MSBuild 生成的 `ClassNex.pri`；仅有 `Microsoft.UI.Xaml.dll` 或构建成功都不能证明发布包可以启动。发布流程与验收脚本均检查资源清单。

不要在桌面浮窗构造函数订阅 `AccessibilitySettings.HighContrastChanged`。在这台桌面上它会抛出 COM 异常并由 XAML 终止进程。沿用 `ActualThemeChanged`、`UISettings.ColorValuesChanged`，浮窗可见时由原有时钟检测高对比状态，状态不变不重建资源。

### 5.4 FluentAvalonia 2.4.0 的 API 与直觉不同
- `NavigationViewItem` **没有 `Icon`** 属性，要用 `IconSource`（`ui:FontIconSource`），
  否则报 `AVLN2000`
- `ui:ToggleSwitch` 的 `x:Name` **不会生成字段**（XAML 能编译但拿不到引用）
  → 这是迁移前旧库的限制；现项目用 Avalonia 原生 `ToggleSwitch` 和 `IsCheckedChanged`，不回退到 `CheckBox`。
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

### 5.6 AvaloniaFluentUI（现用于辅助窗口，2026-10-05 迁移记录）

**它其实是 FluentAvalonia 的 fork**（README 明说），所以控件名基本同名：
`SettingsExpander` / `NavigationView` / `CommandBar` / `InfoBar` / `TabView` / `NumberBox` / `FontIcon(Source)` / `IconSourceElement` 都有。
迁移 = **改命名空间**：`FluentAvalonia.UI.Controls` → `AvaloniaFluentUI.Controls`；
主题 `FluentAvalonia.Styling.FluentAvaloniaTheme` → `AvaloniaFluentUI.Styling.FluentAvaloniaTheme`。
**它只提供 `net10.0`**（1.0.3 依赖 Avalonia 11.3.12），所以必须装 .NET 10 SDK。

- **图标与字体按窗口选择**：正文用 HarmonyOS Sans SC，`FontIcon` / `FontIconSource` 不能继承正文去绘制图标。客户端设置专用 `SettingsIcons.Navigation(key)` 缓存本地 192 × 192 透明 PNG；上游是 Microsoft Fluent UI System Icons Color（MIT），来源提交、每项对应的 SVG 和授权分别记录在 `Assets/SettingsIcons/README.md`、`Fluent-LICENSE.txt`。设置行、页内操作和标题栏用 Windows 自带 `Segoe Fluent Icons, Segoe MDL2 Assets` 的匹配字形，不嵌入系统字体。其他原有使用 Lucide 的窗口继续同时保留 Lucide 专属字形码和 `avares://ClassNex/Assets/Fonts/#lucide`；不要全局替换字体名。
- ⚠️ **`TryFindResource(key)` 不带主题变体时返回的是「浅色变体」的值**
  （`CardBackgroundFillColorDefaultBrush = #b3ffffff` = 白），深色界面里会画出**白卡片（发白）**。
  要么 `TryFindResource(key, ActualThemeVariant, out v)`，要么直接按 `ActualThemeVariant` 给色。
- ⚠️ **设置页导航由稳定键驱动**：读取选中 `NavigationViewItem` 的 `Tag`，从 `_destinations` 定位目标；不要依赖显示名、列表索引或仅 `MenuItems`（“设置”在 `FooterMenuItems`）。
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

## 6. 工具脚本 `tools\`


| 脚本 | 用途 |
| --- | --- |
| `make_sample_timetable.py` | 按 CI 截图还原示例课表，生成 `Assets\timetable.yaml` |
| `measure_overlay_gap.py` | 对截图做像素分析，量卡片位置/宽度/距顶（对照 CI） |
| `read_font_names.py` | 读 TTF 的 name 表，取字体家族名（Avalonia 的 `avares://...#Family` 要用准） |
| `find_ci_default_color.py` | 扫描 CI 程序集提取内嵌色值 |
| `analyze_ci_palette.py` | 对 CI 截图做色彩直方图统计 |
| `make_icon.py` | 生成 `Assets\icon.ico` |
| `capture_window.ps1` | 默认只通过 Win32 `PrintWindow` 捕获指定窗口；显式传 `-AllowScreenCopy` 才允许激活并截取屏幕区域。设置窗口优先用 5.3 的自渲染截图，不依赖 Computer Use。 |

---

## 7. 编译与打包

### 日常调试
```powershell
dotnet build src\ClassNex\ClassNex.csproj -c Debug -t:Rebuild
```
目标：**0 错误 0 警告**。客户端产物在 `src\ClassNex\bin\Debug\net10.0-windows10.0.19041.0\`。

### 出测试包（CI 同款目录结构：版本数据文件夹 + 启动器 + PackageType + files.json）
```powershell
$ver = "26w41f"
$root = "E:\ClassNex\build\release"
dotnet publish src\ClassNex\ClassNex.csproj -c Release -r win-x64 --self-contained true `
  -p:DebugType=none -o "$root\app-$ver-0"
Remove-Item "$root\app-$ver-0\data" -Recurse -Force          # ⚠️ 必删，否则把本机配置/课表打进去
Remove-Item "$root\app-$ver-0\Assets\timetable.yaml" -Force  # ⚠️ 必删：用户要求「新激活不要带测试的课表文件」
Remove-Item "$root\app-$ver-0\使用说明.txt" -Force -ErrorAction SilentlyContinue
dotnet publish src\ClassNex.Launcher\ClassNex.Launcher.csproj -c Release -r win-x64 `
  -p:PublishSingleFile=true -p:PublishTrimmed=true -p:DebugType=none -o "$root\..\launcher"
Copy-Item "$root\..\launcher\ClassNex.exe" "$root\ClassNex.exe"
[System.IO.File]::WriteAllText("$root\PackageType", "folder", (New-Object System.Text.UTF8Encoding($false)))
python tools\make_files_json.py $root $ver 0
Compress-Archive -Path "$root\*" -DestinationPath "E:\ClassNex\build\ClassNEX-$ver-Alpha-win-x64.zip"
```
- 目录结构照 CI：`app-<版本>-<序号>\`（应用载荷）+ `ClassNex.exe`（启动器）+ `PackageType`（内容 "folder"）+ `files.json`（每文件 SHA512 清单，供增量更新）。
- 首次启动时应用会在**安装根目录**生成 `data\`（SettingsService.DataDirectory = 载荷目录的 `..\data`），不需要随包携带。

### 正式发布与旧手工流程

当前只在用户明确要求发布时推送一个**新**的 `26w*` 版本标签，由 `.github/workflows/build-release.yml` 生成发行包。普通 `main` 推送不发布；切勿重推旧标签或删除同名 Release。以下是早期 `tools\gh_release.py` 的历史记录，不是现行操作步骤：
```powershell
git tag -f 26w41f_Alpha ; git push -f origin 26w41f_Alpha
$cred = "protocol=https`nhost=github.com`n" | git credential fill
$env:GITHUB_TOKEN = ($cred | Select-String '^password=').Line.Substring(9)
$env:HTTPS_PROXY="http://127.0.0.1:7890"; $env:HTTP_PROXY=$env:HTTPS_PROXY
$env:GH_TAG="26w41f_Alpha"; $env:GH_TITLE="ClassNEX 26w41f_Alpha"
python tools\gh_release.py "E:\ClassNex\build\ClassNEX-26w41f-Alpha-win-x64.zip"
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
  **警告块之后直接写「## 更新内容」**，中间不要再加 NOTE 版本头；**也不要写「打包信息」段**（用户要求：说明文档 = 警告块 + 更新内容，仅此）
  **发布说明里不要提到 CI / ClassIsland**，也不要用内部控件类名（RippleEffect 等），用功能语言描述
- ⚠️ **不要自己打包上传**，用户说打包才打包（他还没说完就别发）
- 历史发布 `26w41a` ~ `26w41f_Alpha` 都保留，别删

---

## 8. 版本号

**例外（一次性）**：`26.0Beta1` —— 2026-10-11 用户要求把当次发布对外改叫 26.0Beta1；**之后的版本继续用 `YYwWWa_Alpha`**。
- 落地：`InformationalVersion` 直接写 `26.0Beta1`，产物名 `ClassNEX-26.0Beta1-win-x64.zip`，tag `26.0Beta1`
- 该版发布说明的警告块仍是原样照抄（含 26.0 那句），没有跟着改名
**测试版命名规则：`YYwWWa`**（按发布日期，与 Minecraft 快照同款）

- `YY` = 年份后两位（26 = 2026）
- `w` = week
- `WW` = 该年的第几周（ISO 周号）
- `a` = 当周发布的第几个快照（a、b、c…）

工作分支的客户端产品版本为 **`26w48b`**，没有对应 Release 或 Tag。已发布的 `26w48a_Alpha` 和 `ClassNEX-26w48a-Alpha-win-x64.zip` 保持不变；更改客户端版本仍须另行获准才推送新标签。

落地方式：
- `src/DesktopVersion.props` 定义 `ClassNexDesktopVersion`，由客户端和启动器共享。设置的「关于」「更新」、档案编辑器与文件属性「产品版本」读取同一版本。
- `<Version>0.5.0</Version>` 保留给程序集数字版本用（`26w41a` 不是合法 SemVer）
- `<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>`
  —— 否则 MSBuild 会自动在版本号后面接 `+<git哈希>`
- 获准发布时沿用 `ClassNEX-<版本>-Alpha-win-x64.zip`；发布时检查启动器和载荷版本一致。

---

## 9. 项目结构速查

`ClassNex.Core` 提供供课表、设置、时间、插件和通知共用的模型与接口。`ClassNex.Notification.Service`、`ClassNex.Plugin`、`ClassNex.AI`、`ClassNex.WinUI` 分别实现提醒、插件加载、助手和原生桌面界面，彼此不依赖。`ClassNex` 是客户端入口和服务组合根；`ClassNex.Central` 与 `ClassNex.Launcher` 单独生成。详见 `docs/ARCHITECTURE.md`、`docs/PLUGIN_SDK.md`。

原生设置使用 WinUI `Frame` 与系统 Mica。桌面课表使用历史透明浮窗和独立日期、时间组件；所有窗口共用主 STA、设置实例和便携数据目录。

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
> 设置使用真正的 WinUI 3；桌面课表保留透明 Avalonia 浮窗，既有辅助窗口沿用 AvaloniaFluentUI。不要用 `Set-Content` 改源文件；
> 打包上传要等我说，发布说明只写这一版。
> 今天想做：<你要做的功能>
