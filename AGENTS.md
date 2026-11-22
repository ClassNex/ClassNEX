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
11. 所有布尔设置一律使用 Avalonia `ToggleSwitch`，默认显示「开 / 关」，禁止再使用方形 `CheckBox`

## 交接协议

1. 任何一方结束工作前必须 git add -A 加 git commit 加 git push
2. 更新下方当前状态块，写清做完了什么、下一步做什么、改了哪些文件
3. 另一方开工先读本文件，按当前状态续做，不重复已完成的步骤
4. 双方不要同时改同一批文件，以本文件的持有者为准

## 当前状态

持有者：无
上次完成：已交付 TubaWinUi3 风格的客户端设置窗口，以及单独一轮 Windows 设置风格的首页卡片优化。上排显示账户、课表等实际状态，整卡可翻页；下方九个入口分成“课表与桌面”和“账户与应用”，星标与导航独立。修复从收藏返回全部时列表偶尔未恢复。侧栏、横幅、未登录问候、原偏好以及桌面浮窗、通知、OOBE 和集控均保留。
改了文件：`src/ClassNex/Views/SettingsWindow*`、`src/ClassNex/Styles/{CiPalette*,SettingsIcons.cs}`、`src/ClassNex/Assets/SettingsIcons/`、`src/ClassNex/{ClassNex.csproj,Services/WidgetSettingsBuilder.cs,ViewModels/WidgetItem.cs}`、`tools/{capture_window,verify_settings}.ps1`、`HANDOFF.md`、`AGENTS.md`。
进行中：两阶段均已完成。全量 Debug 构建 0 警告、0 错误；最终通过 `tools/verify_settings.ps1` 在未登录、关闭集控的独立副本中验证 40 项，包括两种主题、宽窄窗、收藏恢复、大卡片偏好、键盘导航和设置回写。未打包或创建发布标签。
下一步：如需再次验收，运行 `tools/verify_settings.ps1`，按输出路径查看八张截图。不要复用曾经登录的测试副本，也不要提交 `build` 下的测试数据、截图与参考程序素材；发布等待用户另行要求。
