# 26w48b：课堂提醒与设置侧栏

本轮是桌面预览，基线是已经发布 26w48a 的 `main@09a6ada`。不会从预览分支更新服务器或创建发行标签。

## 在教室里会看到什么

| 事件 | 桌面课表中的顺序 |
| --- | --- |
| 课前准备 | 开场标题 3 秒；然后显示距上课的倒计时、下一节课程全名和时段。有准备提示时，每 10 秒上下翻页，到上课即结束。室内、室外可以设置不同提前量。 |
| 上课 | 显示上课标题 5 秒，然后恢复当前课程。不会在课已上到一半时补播。 |
| 下课 | 有下一节时先显示标题 2 秒，再显示课间时长和下一节课程 10 秒；有自定义课间提示时再翻一页 10 秒。作息表里命名的课间可直接用作开场标题；用户指定的提醒标题优先。没有下一节时不显示空白课程页。 |
| 放学 | 可单独启用，默认关。标题 3 秒，接着显示放学文案 30 秒。若末课后还有课间，先正常下课，在末尾课间结束时再提醒放学；两个时刻相同时只播放学。 |

设置入口是 **通知服务 → 上下课提醒 / 放学提醒 / 提醒方式**。总览可以直接预览四种事件。手动课前预览最多占用桌面 23 秒，倒计时仍指向实际课程；正式提醒不受这一上限影响。铃声默认静音；开启后可使用内置短铃或本机 WAV，没有语音播报。集控只同步声音开关和音量，不会上传铃声文件或本机选择。室外课程标记随工作档案与集控往返保留；标准 CSES 另存不携带扩展字段。

课堂呈现的参考是本地 ClassIsland 源码快照 `235914a` 的上下课和放学提醒提供程序。适配范围是这四类课堂事件、桌面展示与设置；未引入 ClassIsland 运行时、天气预警或集控广播。默认保留 ClassNEX 的室内提前 3 分钟、放学提醒关闭。正式提醒优先于手动预览。

## 侧栏错位的原因

收起后，导航器和页面其实已经回到正确位置。问题在页面内部：滚动容器内直接放了一个限宽、居中的正文面板。可见宽度改变后，卡片仍发生额外的水平偏移。现在正文左缘对齐实际视区，超宽时用对称内边距保留阅读宽度；原生导航器负责动画，不再同时重设头像大小、侧栏模式或重建页面。宽屏的手动开合选择与窄屏的临时覆盖菜单分别处理。

相同的 1,224 DIP 客户区，收起时页面都从 x=49 开始：修复前集控卡片位于 x=414–1,438，开关右端 x=1,421；修复后卡片 x=125–1,149，开关右端 x=1,132。连续五次开合后也会落在同一位置。

## 实窗对照

- 同宽深色设置窗口：[修复前](../artifacts/26w48b-notifications/central-collapsed-before.jpg) / [修复后](../artifacts/26w48b-notifications/central-collapsed-after.jpg)。收起后标题、集控地址框和开关均留在视区内。
- [通知服务](../artifacts/26w48b-notifications/notification-preview.jpg)：在「预览」行选择事件并播放，不改动真实课表。
- 透明桌面浮窗：[完整课程名](../artifacts/26w48b-notifications/prepare-full-course.png) / [放学文案](../artifacts/26w48b-notifications/after-school.png)。

## 本轮验收

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
./tools/review_desktop.ps1 -Round w48b-local-review -EnabledReminders
./tools/review_desktop.ps1 -Round w48b-classroom-only -EnabledReminders -RemindersOnly
./tools/preview_classroom.ps1 -ExpectedVersion 26w48b
```

窗口回归和交互预览使用不同的匿名目录；再次运行 `review_desktop` 须更换 `-Round`，防止覆盖证据。预览不会使用真实账户、生产集控或已有课表。
