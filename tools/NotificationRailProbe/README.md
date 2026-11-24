# 通知滑轨原件对照

在 Windows 和 .NET 10 环境中从仓库根目录运行：

```powershell
./tools/compare_ci_notification_rail.ps1 -Theme dark -Accent '#459BAC'
./tools/compare_ci_notification_rail.ps1 -Theme light -Accent '#459BAC'
```

脚本只构建这个探针，不构建或启动 ClassNEX/CI，也不读两者的账户、配置或课表。可用 `-CiSource`、`-CiRuntime`、`-ClientRuntime` 指定 CI 源码仓库、CI 安装包载荷和 ClassNEX 已构建客户端的目录。两组在独立进程、不可见窗口中运行：

- 参考端用 `git show 235914a2fac1733cc2a169457c7bad4edb2ca2f6:ClassIsland/XamlThemes/FluentTheme/Styles.axaml` 提取 `ProgressContainer`，使用 CI 安装包的 Avalonia 11.3.17 和 FluentAvalonia 2.4.1。
- 候选端从当前 `MainWindow.axaml` 提取自己的 `ProgressContainer`，递归读取控件引用的本地资源字典，使用 ClassNEX 客户端的 Avalonia 和 AvaloniaFluentUI。两端都使用参考安装包自带的 XAML 解析器，不共享主题 DLL 或控件模板。
- 宿主仅代替原窗口提供 `CountdownProgressValue`，给两端相同的画布、圆角、背景、深浅主题与输入强调色，不覆盖控件画刷、模板或绑定。分别输入 1、0.5、0.25，待界面稳定后读取真实 `PART_Indicator` 的几何和实际绘制结果。

输出在 `artifacts/ui-review/<轮次>/`：`reference/`、`candidate/` 各自带三张 320 × 40 PNG、XAML 样本和包含程序集版本、DPI 布局倍率及解析色值的 `result.json`；`comparison.json` 记录几何差异和像素差异。任一不同，脚本以失败退出。不覆盖已有轮次。

这是同一组输入下三个稳定画面的像素对照，不是两个完整应用的逐帧录屏。遮罩、翻页、时钟及退出时机另由桌面提醒验收覆盖。

## 通知正文对照

```powershell
./tools/compare_ci_notification_provider.ps1 -Theme dark
./tools/compare_ci_notification_provider.ps1 -Theme light
```

课前倒计时和课间各对比一次短课名、一次长课名与教师名，另外比较放学简文。两端使用同样的模拟课表、主题、强调色及分配的正文宽度。原版从同一固定提交读取 `ClassNotificationProviderControl.axaml` 与 `SimpleTextOverlayTemplate.axaml`，保留原 `Run`、绑定、秒数转换器、文本分层和滑动列表；仅将业务服务的数据上下文换为匿名课表。上游字体使用发行包自身的资源加载器和 `Assets/Fonts`。

候选端在独立的临时数据目录创建**真实的已编译 ClassNEX 浮窗**，设定匿名课表行的宽度，再调用它的 `ShowNotificationMask` 与 `ShowNotificationOverlay`；不设置输出正文的宽度。两端取正文控件实际排版的宽度、高度与相对位置，在相同的纯色底上绘制 560 或 860 × 40 的完整静止行，包含提供程序的留白。翻页过程中的位移、遮罩和滑轨不算入这组正文比较。仅将文字区域设为相同宽度会掩盖宿主滚动容器的内边距错误。

每轮输出五对 PNG、两份 `result.json`（实际文字、各层字号/字重、`Run`、前景、边距与坐标、运行时版本）、`comparison.json`（逐场景差异像素和几何）到新目录。文字遗漏、空图、任一像素或正文布局不符均不会标记通过。对比使用渲染时记录哈希的客户端 DLL，修改界面后须先完整重建客户端。本项不证明整窗位置、宽度选择算法或动画逐帧一致。
