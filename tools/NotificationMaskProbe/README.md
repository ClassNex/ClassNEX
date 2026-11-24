# 通知开场的双端对照

在 Windows 上完整重建客户端后，分别运行 `./tools/compare_ci_notification_mask.ps1 -Theme dark` 和 `-Theme light`。默认比较本机 ClassIsland 2.1.0.1 安装包与 ClassNEX Debug；不打开可见窗口，也不使用已有的用户资料。每轮单独写入 `artifacts/ui-review/`，存在的轮次不覆盖。

原件来自 `ClassIsland/ClassIsland@235914a2fac1733cc2a169457c7bad4edb2ca2f6` 的通知遮罩，在原版 `ClassIsland.dll` 和原版 Skia 环境下绘制。测试装载其 `OverlayMask` 子树，将依赖窗口位置、尺寸和提醒服务的绑定接到固定的 320 × 40 DIP 区域和“即将上课”双图标模板。标题模板、基线修复和字体使用原版发行包，遮罩的进入和退出关键帧读取固定提交。候选端则通过实际生产浮窗的 `ShowNotificationMask` / `HideNotificationMask` 触发。

两端使用各自 Avalonia 的独立动画时钟，输入同一个强调色 `#459BAC`。打开后取 100、330、1100 毫秒，关闭后取 100、300 毫秒。每帧输出一张完整遮罩和一张单独的标题层；报告记录全部 ARGB 像素、透明度覆盖、五区进度、标题位置及字号所占的实际尺寸、缩放和填充颜色。原件停住、原件标题为空或两端有任何差异时，命令不会报告通过。

这是课前双图标标题和五段斜条在指定宽度、两种主题及五个时刻的测试；不包含外侧阴影、整个浮窗、其他提醒标题、连续播放帧率或外接显示器表现。滑轨与正文的独立对照见相邻的 `NotificationRailProbe`。
