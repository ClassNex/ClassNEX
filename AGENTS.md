# AGENTS.md

Codex 与 DSH 双代理协作约定。任何一个代理开始工作前必须先读本文件与 HANDOFF.md，结束时必须更新本文件再提交。

## 项目

ClassNEX，ClassIsland 风格的桌面课表应用。Windows 客户端使用 .NET 10、Windows App SDK / WinUI 3，保留 Avalonia 11 + AvaloniaFluentUI 辅助窗口。仓库 https://github.com/ClassNex/ClassNEX。

## 每次开工必读

1. 本文件
2. HANDOFF.md 项目完整背景与踩坑记录
3. git log 最近 5 条提交

## 铁律

1. 课堂产品逻辑先参照 E:\ci-source。设置用 Windows 桌面的布局习惯，只参考界面结构，不复制其他项目的素材或实现。
2. 设置复用主线原生 WinUI 3（Frame、Mica）；桌面课表保留透明 Avalonia 浮窗。档案、OOBE、登录与托盘沿用 AvaloniaFluentUI。两套窗口共享一个应用生命周期。
3. 构建必须 0 警告 0 错误，命令 dotnet build src\ClassNex\ClassNex.csproj -c Debug -t:Rebuild
4. 增量构建会谎报 0 错误但 XAML 编译失败，报错 No precompiled XAML 时用 -t:Rebuild 看真实错误
5. 改文本文件只用编辑工具，PowerShell 写文件必须 UTF8Encoding false，禁止 Set-Content
6. git push 前确认代理 127.0.0.1:7890 通；推送后核对 HEAD 与本次获准推送的远端分支。不得用重构分支覆盖 main。
7. 打包发布只推 26w* 标签，GitHub Actions 会自动构建并发布，作者身份已绑定 LingOfficial；发布包结构照 CI：app-<版本>-<序号> 载荷目录 + ClassNex.exe 启动器 + PackageType（内容 folder）+ files.json（SHA512 清单），data 由首次启动在根目录生成
8. 不添加无意义的代码注释；可单独打包的 Core 公共契约提供 XML 文档。
9. 字体加粗前先征得用户同意。颜色复用 App.axaml 的主题资源、Fluent 系统画刷或浮窗的 CiPalette，避免每页写死颜色。
10. Windows 窗口验收优先使用 `tools/review_desktop.ps1` 的独立匿名目录；原有 `CLASSNEX_VERIFY` 数据自检仍可用。
11. 布尔设置使用其宿主的 `ToggleSwitch`（开 / 关），不使用方形 `CheckBox`。

## 交接协议

1. 任何一方结束工作前必须 git add -A 加 git commit 加 git push
2. 更新下方当前状态块，写清做完了什么、下一步做什么、改了哪些文件
3. 另一方开工先读本文件，按当前状态续做，不重复已完成的步骤
4. 双方不要同时改同一批文件，以本文件的持有者为准

## 当前状态

持有者：无
工作分支：`main`；本轮基线 `943e47a`。此前的主线合并 `b17768d`、便携包 `5ea3b11` 及重构分支均保留，未重置 main、未打发布标签。
本次内容：`src/ClassNex.WinUI` 将五个设置分类拆为可整行点击的导航卡和十二个详情页，保留搜索、返回焦点、设置保存及共用的右对齐开关；桌面课表外观不变。`src/ClassNex/Services/WindowsTrayIcon.cs` 统一通知区单击、长按、右键和键盘菜单；保留显隐命令，忽略旧动作偏好，首次向导只显示继续和退出。产品版本从 `src/DesktopVersion.props` 读取，为 `26w48a`。
验证：客户端 Debug 完整重建及 Release 构建均 0 警告 / 0 错误；41 项单测、84 项原生设置交互、91 项托盘检查、380 项实窗回归及账户 / 集控回环联测通过。截图、布局量测与复测命令见 `docs/26w48a-settings.md`。此前验收分别见 `docs/mainline-desktop-acceptance.md` 和 `docs/26.0-acceptance.md`。均不以真实账户、系统注册项或生产服务器跑夹具。
后续：本次未发布安装包；实体触摸屏、跨显示器混合 DPI 与生产身份提供商仍需现场验证，正式发布另行决定。
