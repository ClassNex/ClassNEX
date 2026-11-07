# AGENTS.md

Codex 与 DSH 双代理协作约定。任何一个代理开始工作前必须先读本文件与 HANDOFF.md，结束时必须更新本文件再提交。

## 项目

ClassNEX，ClassIsland 风格的课表应用。Avalonia 11 + AvaloniaFluentUI，目标框架 net10.0-windows。仓库 https://github.com/ClassNex/ClassNEX。

## 每次开工必读

1. 本文件
2. HANDOFF.md 项目完整背景与踩坑记录
3. git log 最近 5 条提交

## 铁律

1. 设计一律先参照 CI 源码，目录 E:\ci-source，禁止自行发明
2. 界面只用 AvaloniaFluentUI 控件，所有窗口用 wd:AppWindow 沉浸标题栏加云母
3. 构建必须 0 警告 0 错误，命令 dotnet build src\ClassNex\ClassNex.csproj -c Debug -t:Rebuild
4. 增量构建会谎报 0 错误但 XAML 编译失败，报错 No precompiled XAML 时用 -t:Rebuild 看真实错误
5. 改文本文件只用编辑工具，PowerShell 写文件必须 UTF8Encoding false，禁止 Set-Content
6. git push 前确认代理 127.0.0.1:7890 通，推送后核对 HEAD 等于 origin/main
7. 打包发布只推 26w* 标签，GitHub Actions 会自动构建并发布，作者身份已绑定 LingOfficial；发布包结构照 CI：app-<版本>-<序号> 载荷目录 + ClassNex.exe 启动器 + PackageType（内容 folder）+ files.json（SHA512 清单），data 由首次启动在根目录生成
8. 仓库代码不带注释，发布说明与 md 文件不带注释
9. 字体加粗前先征得用户同意，颜色一律用 CiPalette
10. 运行验证优先用 CLASSNEX_VERIFY 环境变量自检

## 交接协议

1. 任何一方结束工作前必须 git add -A 加 git commit 加 git push
2. 更新下方当前状态块，写清做完了什么、下一步做什么、改了哪些文件
3. 另一方开工先读本文件，按当前状态续做，不重复已完成的步骤
4. 双方不要同时改同一批文件，以本文件的持有者为准

## 当前状态

持有者：无
上次完成：主界面悬浮课表视觉重做：浮岛改为 44 高 / 14 圆角 / 主题玻璃底 + 顶部高光 + 柔和投影，内容边距 16；日期组件改为强调色竖条 + 两行「周几 / MM/dd」+ 细分隔线；课表项改为胶囊（已上完淡化 0.42），当前课为强调色胶囊并内嵌 2px 自绘进度线，≤60s 显示实心倒计时胶囊；下一节课 / 倒计时 / 文本 / 分割线组件统一胶囊样式；编辑模式工具条与底栏改为玻璃底。另新增 1200×1200 应用图标设计 icon.html（樱花粉渐变、花瓣与樱花点缀、横向 ClassNEX 标题：Class 用资源圆体 Bold、NEX 用 Manrope 800 且同为 166px、字体与窗口均带柔和阴影、窗口 45% 白色底、课表进度条、内嵌字体），并导出 icon-1200.png。主界面浮岛改为浅色粉白樱花渐变 / 深色深玫瑰渐变，悬浮组件强调色统一为樱花玫瑰；新增启动动画：`LinePanel` 组合层 0.94→1.012→1 回弹缩放 + 420ms 淡入，组件按 90ms + 45ms 错峰淡入（已移除 composition Offset 位移，修复启动时组件堆叠）。设置窗口背景改为同一樱花渐变（`CiPalette.SettingsSurfaceBrush()`），打开时 `SettingsRoot` 做 0.965→1.006→1 回弹缩放 + 位移淡入；主页右侧加入整朵樱花飘落 Canvas（8 个 Path + 组合层 Offset/RotationAngle 无限循环，顶部均匀起点/独立风偏/横摆/5.5~14.5s 时长，无计时器与逐帧更新）；顶栏改为 700 宽胶囊、标题用资源圆体 Bold；托盘菜单增加从右下角弹簧展开/缩小淡出动画，关闭统一走 `CloseAnimated()`。已用 .NET 10.0.403 执行 `dotnet build src\ClassNex\ClassNex.csproj -c Debug -t:Rebuild`，0 警告 0 错误
改了文件：AGENTS.md、HANDOFF.md、README.md、icon.html、icon-1200.png、src/ClassNex/App.axaml.cs、src/ClassNex/Styles/CiPalette.cs、src/ClassNex/Widgets/WidgetBase.cs、src/ClassNex/Widgets/DateWidget.cs、src/ClassNex/Widgets/ScheduleWidget.cs、src/ClassNex/Widgets/ClockWidget.cs、src/ClassNex/Widgets/NextClassWidget.cs、src/ClassNex/Widgets/CountdownWidget.cs、src/ClassNex/Widgets/TextWidget.cs、src/ClassNex/Widgets/DividerWidget.cs、src/ClassNex/Views/MainWindow.axaml、src/ClassNex/Views/MainWindow.axaml.cs、src/ClassNex/Views/MainWindow.EditMode.cs、src/ClassNex/Views/SettingsWindow.axaml、src/ClassNex/Views/SettingsWindow.axaml.cs、src/ClassNex/Views/TrayMenuWindow.axaml、src/ClassNex/Views/TrayMenuWindow.axaml.cs、src/ClassNex/Assets/Fonts/ResourceHanRounded-Bold-Subset.ttf、src/ClassNex/Assets/Fonts/ResourceHanRounded-LICENSE.txt
进行中：无
下一步：图标已落地：`icon-rounded-1200.png` 加 18% 圆角后生成多尺寸 `src/ClassNEX/Assets/icon.ico`（16/20/24/32/40/48/64/96/128/256），替换了原蓝色日历图标；NEX 唤醒光效已改为 `NexGlowControl` 樱花花瓣，从屏幕四边向内侧飘入；托盘菜单阴影增加 28px 透明边距并去掉弹簧过冲，修复阴影被窗口裁切的边框感。
