# 26w48b：课堂提醒与设置侧栏

本轮是桌面预览，基线是已经发布 26w48a 的 `main@09a6ada`。不会从预览分支更新服务器或创建发行标签。

## 在教室里会看到什么

| 事件 | 桌面课表中的顺序 |
| --- | --- |
| 课前准备 | 开场标题 3 秒；然后显示距上课的倒计时、下一节课程全名和时段。有准备提示时，每 10 秒上下翻页，到上课即结束。室内、室外可以设置不同提前量。 |
| 上课 | 显示上课标题 5 秒，然后恢复当前课程。不会在课已上到一半时补播。 |
| 下课 | 有下一节时先显示标题 2 秒，再显示课间时长和下一节课程 10 秒；有自定义课间提示时再翻一页 10 秒。作息表里命名的课间可直接用作开场标题；用户指定的提醒标题优先。没有下一节时不显示空白课程页。 |
| 放学 | 可单独启用，默认关。原版通用双图标标题默认 5 秒，接着显示放学文案 30 秒。若末课后还有课间，先正常下课，在末尾课间结束时再提醒放学；两个时刻相同时只播放学。 |

设置入口是 **通知服务 → 上下课提醒 / 放学提醒 / 提醒方式**。总览可以直接预览四种事件。手动课前预览最多占用桌面 23 秒，倒计时仍指向实际课程；正式提醒不受这一上限影响。铃声默认静音；开启后可使用内置短铃或本机 WAV，没有语音播报。集控只同步声音开关和音量，不会上传铃声文件或本机选择。室外课程标记随工作档案与集控往返保留；标准 CSES 另存不携带扩展字段。

课堂呈现的参考是本地 ClassIsland 源码快照 `235914a` 的上下课和放学提醒提供程序。适配范围是这四类课堂事件、桌面展示与设置；未引入 ClassIsland 运行时、天气预警或集控广播。默认保留 ClassNEX 的室内提前 3 分钟、放学提醒关闭。正式提醒优先于手动预览。

### 通知底部滑轨

课前和课间使用原版 `SlidingListBox.axaml`：每页 10 秒，换页 250 ms，透明度线性、上下位移 40 DIP 的末帧使用 `0.65, 0, 0.35, 1`。正文沿用 16 + 12 DIP 边距及 16/14/18/20 DIP 的字号层级。常规提醒行高 40 DIP、圆角 8 DIP。课程信息按文本实际宽度排列，不再把倒计时和课程强制分成等宽两栏。没有教师名时，课名和时段间保留 6 DIP。放学使用独立的消息模板，左右各 24 DIP。通知图标沿用原字形，不影响软件其它 Lucide 图标。

开场遮罩上没有滑轨。课前、下课和放学切入正文时，滑轨才从满格开始缩短。只有五秒标题的上课提醒不会出现滑轨。课前的时间基准是上课时刻；下课、放学按正文已播放的时长计算。翻页由底轨的同一次刷新驱动，以正文开始后的实际经过时间选择每十秒该显示哪一页；系统忙过一帧不会令后面的翻页一直迟到。渲染取样停顿超过 200 ms 时，滑轨下一帧直接追上当前进度，再恢复正常过渡。手动课前预览仍在预览窗口结束时收起，不改变上课时间。

例如，课前正文出现时距上课还有 120 秒，显示「还剩 60 秒」时，轨道约为一半。若手动预览的下一节课在 40 分钟后，23 秒预览结束时轨道就应当仍接近满格：它表示距离上课的时间，不是关闭预览的进度。

这里直接移入了 ClassIsland `235914a` 主题内的 `ProgressContainer` / `OverlayTimeProgressBar`，以及它实际使用的 FluentAvalonia 2.4.1 单控件模板。背景轨为 1 DIP，指示条为 3 DIP，可用宽度取自整行，端部由浮窗圆角裁剪。仅在显示正文时按渲染优先级每 50 ms 取样，`Value` 做 50 ms 线性过渡，不再叠加宿主默认模板的 500 ms 宽度动画。底轨不跟随文字位移，随正文淡出。以前的“从遮罩开始按整条提醒寿命缩放”已移除。来源、依赖与许可在[移植清单](../third_party/ClassIsland/NOTICE.md)。

## 长通知与回到课表

拖过的课表保存的是左上角，不是通知的起点。提醒宽度变化时，以该位置上的常态课表中心向两侧展开，贴到工作区边界则向内收；收起后仍用原保存位置。默认顶部居中、从 CI 导入的左/中/右停靠与偏移不变。

日常提醒沿用 CI 的 40 DIP 行高。标题或课名变长时，浮窗先用当前显示器能提供的宽度，再换行、增高；教师、时段、提示语也在测量范围内。翻页共用容得下两页的高度，避免普通课间来回跳动。若一页比整个工作区还高，则按当前页排版；只有这一页可以滚动时，鼠标移上去才不淡化，且只在圆角轮廓里接收输入。换回短页、通知结束或 NEX 接管后恢复用户原有的鼠标穿透设置，不改变已保存的偏好。

提醒正文用 125 ms 淡出。旧字完全消失后，课表再用 150 ms 显出；圆角底板一直保留，不让课程和通知叠在一起。底板按当时的课表收回，不沿用开场前的尺寸。即使在退场时修改组件、收到另一条提醒或唤醒 NEX，旧动画也不能把新内容收走。NEX 返回课表时直接使用最新布局，包括在对话框显示期间改成的纵排。

托盘菜单快速重开会从当前透明度返回；NEX 连续唤醒共用同一扇窗口。即使已经在读某条提醒，关闭 Windows 动画也会立即停止移动，仍按原来的十秒节奏翻页。重新开启动画不会重播当前页。关闭动画不改动通知时长或上课时间。

## 侧栏错位的原因

收起后，导航器和页面其实已经回到正确位置。问题在页面内部：滚动容器内直接放了一个限宽、居中的正文面板。可见宽度改变后，卡片仍发生额外的水平偏移。现在正文左缘对齐实际视区，超宽时用对称内边距保留阅读宽度；原生导航器负责动画，不再同时重设头像大小、侧栏模式或重建页面。宽屏的手动开合选择与窄屏的临时覆盖菜单分别处理。

相同的 1,224 DIP 客户区，收起时页面都从 x=49 开始：修复前集控卡片位于 x=414–1,438，开关右端 x=1,421；修复后卡片 x=125–1,149，开关右端 x=1,132。连续五次开合后也会落在同一位置。

## 实窗对照

- 同宽深色设置窗口：[修复前](../artifacts/26w48b-notifications/central-collapsed-before.jpg) / [修复后](../artifacts/26w48b-notifications/central-collapsed-after.jpg)。收起后标题、集控地址框和开关均留在视区内。
- [通知服务](../artifacts/26w48b-notifications/notification-preview.jpg)：在「预览」行选择事件并播放，不改动真实课表。
- 透明桌面浮窗：[完整课程名](../artifacts/26w48b-notifications/prepare-full-course.png) / [放学文案](../artifacts/26w48b-notifications/after-school.png)。

## 2026-11-27：课前倒计时、换行与退场

在独立 Windows 桌面重跑了[正常动画](../artifacts/26w48b-notifications/motion/normal-review.json)（128 项报告条目、18 次捕获）和[减少动画](../artifacts/26w48b-notifications/motion/reduced-review.json)（124 项、16 次捕获）。用例从短标题切到七年级信息科技长课名，检查[课名换行](../artifacts/26w48b-notifications/motion/wrapped-course.png)、[旧字退场](../artifacts/26w48b-notifications/motion/handoff-outgoing.png)与[课表还原](../artifacts/26w48b-notifications/motion/restored.png)。这是应用运行时的截图，不是手工绘制或桌面帧率测试。

[定位专测](../artifacts/26w48b-classroom-review/notification-position.review.json)另有 67 项：同时量系统窗口、圆角底板、字幕和课名边界，检查拖动后播放、贴边与收起。最新版紧凑布局通过 112 项固定上游源码核对；下列 116 项和 26 帧零差是早先等宽布局的结果，不适用于新正文的横坐标。

重新渲染两端的[浅色](../artifacts/26w48b-classroom-review/reminder-typography-light.json)和[深色](../artifacts/26w48b-classroom-review/reminder-typography-dark.json)正文，每种主题 5 个场景都通过独立字形与行内纵向位置核对。两种主题均只有放学文案（1/5）整行未平移时逐像素相同；四个课程场景的水平留白有意不同。报告也保留了原坐标的像素差异。

## 2026-11-24：通知原件对照

对照使用固定 CI 源码 `235914a`、CI 2.1.0.1 Windows 发行包与 26w48b 的实际渲染结果。移入的模板、字体、布局、文字与关键帧通过 116 项静态核对。下面是相同输入下两端分别渲染的结果，每份报告都保留逐场景的像素差异，而不是截图的相似度评分。

| 对照对象 | 深色 | 浅色 | 比较范围 |
| --- | --- | --- | --- |
| 开场遮罩 | [5 帧](../artifacts/26w48b-notifications/parity/mask-dark.json) | [5 帧](../artifacts/26w48b-notifications/parity/mask-light.json) | 320 × 40，展开后 100 / 330 / 1100 ms、关闭后 100 / 300 ms；标题、图标、底色和五段斜条 |
| 底部滑轨 | [3 帧](../artifacts/26w48b-notifications/parity/rail-dark.json) | [3 帧](../artifacts/26w48b-notifications/parity/rail-light.json) | 320 × 40，进度 100%、50%、25%；轨道、填充范围和颜色 |
| 完整正文行 | [5 个场景](../artifacts/26w48b-notifications/parity/provider-dark.json) | [5 个场景](../artifacts/26w48b-notifications/parity/provider-light.json) | 560 或 860 × 40，课前、课间各含短课名和长课名＋教师，另有放学文案 |

这 26 张旧版采样画面均为零像素差异，不是 11 月 27 日紧凑正文的对照结果。在强调色 `#459BAC` 的对照中，开场与滑轨使用原版的深色 `#73B9C7`、浅色 `#3B8492`。这项结果不包含所有设备、系统强调色、外侧阴影或连续播放的每一帧。

### 上一版通知原件的客户端与桌面验收

- Windows 客户端 Debug、Release 完整重建和集控服务 Release 构建均为 0 警告 / 0 错误。106 项单元测试通过（Core 66、通知 34、插件 6），[原生设置的 91 项交互验证](../artifacts/26w48b-notifications/parity/native-checks.txt)全部通过。此次没有部署集控。
- [桌面实窗报告](../artifacts/26w48b-notifications/parity/desktop-review.json)记录 37 个渲染画面、465 项通过的行为和布局断言，另有 1 项侧栏 UI 队列响应采样，不是合成器帧率。覆盖 100%、125%、150% 缩放，含真实浮窗的[课前长课名](../artifacts/26w48b-notifications/parity/reminder-subject-dark-144.png)和[课间下一节课](../artifacts/26w48b-notifications/parity/reminder-break-dark-144.png)，以及结束后恢复正常课表宽度。
- 对应的 Debug `ClassNex.dll` SHA-256 为 `F6C8D0B90C41E1CE8D921A8713A57C53D79A36D271FFCC2EF9E03FCF3005D894`。同一客户端的首次设置、迁移与重启验收见[迁移文档](26w48b-migration.md)。

## 上一轮：通知调度与设置侧栏验收

- Windows 客户端 Debug 完整重建、集控服务 Debug 构建：均为 0 警告 / 0 错误。63 项单元测试通过（Core 28、通知 29、插件 6）；集控页面的两个脚本均通过语法检查，未部署到服务器。
- [90 项原生设置交互](../artifacts/26w48b-notifications/native-checks.txt)全部通过，包括返回/搜索、提醒开关与放学文案落盘、真实调度、账户保持登录和版本入口一致性。已从桌面实窗打开「通知服务」，用菜单选择并播放了课前预览。
- [桌面实窗报告](../artifacts/26w48b-notifications/desktop-review.json)：447 项行为/布局检查通过，另记录一项 UI 线程响应采样；33 个窗口画面进入报告。覆盖 100%、125%、150% 缩放，620、720、1080、1240 DIP 窗宽；包含独立进程重开后恢复时钟、日期顺序与浮窗位置。不是多台教室电脑的性能测试。
- 1240 DIP 窗宽下，单次收起只改变页面和卡片宽度各 1 次；快速连续开合五次则各 5 次。16ms 的 UI 队列定时器实测回调间隔中位数分别为 31.2 / 31.9ms，最大 126.1 / 180.2ms。此数据来自 Debug 实窗回归，不是合成器帧率；没有据此承诺恒定帧率。

## 如何复验

```powershell
dotnet build src/ClassNex/ClassNex.csproj -c Debug -t:Rebuild
dotnet test ClassNex.sln -c Debug --no-restore
dotnet build src/ClassNex.Central/ClassNex.Central.csproj -c Debug
./tools/verify_settings.ps1 -ExpectedProductVersion 26w48b -TimeoutSeconds 180
./tools/verify_ci_notification_templates.ps1
foreach ($theme in 'light','dark') {
    ./tools/compare_ci_notification_mask.ps1 -Theme $theme
    ./tools/compare_ci_notification_rail.ps1 -Theme $theme
    ./tools/compare_ci_notification_provider.ps1 -Theme $theme -LayoutContract Compact
}
./tools/review_desktop.ps1 -Round w48b-local-review -EnabledReminders
./tools/review_desktop.ps1 -Round w48b-classroom-only -EnabledReminders -RemindersOnly
./tools/review_desktop.ps1 -Round w48b-classroom-geometry -ReminderGeometryOnly
./tools/review_desktop.ps1 -Round w48b-classroom-reduced -EnabledReminders -RemindersOnly -ReducedMotion
./tools/review_desktop.ps1 -Round w48b-wheel -ReminderInputOnly
./tools/review_desktop.ps1 -Round w48b-external-wheel -InteractiveWheel
./tools/preview_classroom.ps1 -ExpectedVersion 26w48b
```

窗口回归和手动交互预览使用不同的匿名目录；再次运行 `review_desktop` 须更换 `-Round`，防止覆盖证据。自动回归不会使用真实账户、生产集控或已有课表。普通手动预览不复制原有数据，但允许用户主动登录正式账户或获取配对码；`-Migration` 预览仍隔离账户和集控。

`Compact` 比较文字内容、字形、纵向位置和紧凑布局，同时保留未平移的整行像素差异；不应把水平位置有意变化后的课程行报告为与旧版逐像素相同。

`-ReminderInputOnly` 会检查窗口命中、原生穿透标志、实际滚动偏移，以及最后一个字有没有进入视区。在 Windows 启用 pointer 输入时，这个自动检查从命中的 Avalonia 控件派发事件，并不等同于系统滚轮测试。`-InteractiveWheel` 则只接受外部滚轮：匿名放学通知会临时出现在任务栏，鼠标停在正文内往下滚，直到看到第 60 项。测试不会自己改滚动位置，也不会改 Windows 的输入或动画设置。
