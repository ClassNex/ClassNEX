# AGENTS.md

Codex 与 DSH 双代理协作约定。任何一个代理开始工作前必须先读本文件与 HANDOFF.md，结束时必须更新本文件再提交。

## 项目

ClassNEX，ClassIsland 风格的桌面课表应用。Windows 客户端使用 .NET 10、Windows App SDK / WinUI 3，保留 Avalonia 11 + AvaloniaFluentUI 辅助窗口。仓库 https://github.com/ClassNex/ClassNEX。

## 每次开工必读

1. 本文件
2. HANDOFF.md 项目完整背景与踩坑记录
3. git log 最近 5 条提交

## 铁律

1. 设计一律先参照 CI 源码，目录 E:\ci-source，禁止自行发明
2. 设置窗口和桌面课表浮窗只用原生 Microsoft.UI.Xaml（WinUI 3）控件与系统主题；不得用其他框架模拟。既有辅助窗口继续使用 AvaloniaFluentUI。
3. 构建必须 0 警告 0 错误，命令 dotnet build src\ClassNex\ClassNex.csproj -c Debug -t:Rebuild
4. 增量构建会谎报 0 错误但 XAML 编译失败，报错 No precompiled XAML 时用 -t:Rebuild 看真实错误
5. 改文本文件只用编辑工具，PowerShell 写文件必须 UTF8Encoding false，禁止 Set-Content
6. git push 前确认代理 127.0.0.1:7890 通，推送后核对 HEAD 等于 origin/main
7. 打包发布只推 26w* 标签，GitHub Actions 会自动构建并发布，作者身份已绑定 LingOfficial；发布包结构照 CI：app-<版本>-<序号> 载荷目录 + ClassNex.exe 启动器 + PackageType（内容 folder）+ files.json（SHA512 清单），data 由首次启动在根目录生成
8. 仓库代码不带注释，发布说明与 md 文件不带注释
9. 字体加粗前先征得用户同意。原生窗口优先使用系统主题资源；遗留界面沿用 CiPalette。
10. 设置和浮窗全量构建后优先运行 `tools/verify_settings.ps1`，使用匿名隔离目录；`CLASSNEX_VERIFY` 用于原有数据自检。
11. 所有布尔设置一律使用对应界面框架的 ToggleSwitch，默认显示「开 / 关」，不用方形 CheckBox。

## 交接协议

1. 任何一方结束工作前必须 git add -A 加 git commit 加 git push
2. 更新下方当前状态块，写清做完了什么、下一步做什么、改了哪些文件
3. 另一方开工先读本文件，按当前状态续做，不重复已完成的步骤
4. 双方不要同时改同一批文件，以本文件的持有者为准

## 当前状态

持有者：无
上次完成：课表核心、通知服务、插件和 AI 分模块；客户端设置与桌面浮窗改为原生 WinUI 3。两个窗口复用同一份设置、账户和课程，旧的 Avalonia 设置/浮窗及专用动画已退役。档案、托盘、登录、OOBE 和独立集控站点未改用 WinUI。
改了文件：`src/ClassNex.Core/{Abstractions,Services}`、`src/ClassNex.{Notification.Service,Plugin,WinUI}`、`src/ClassNex/{Program.cs,App.axaml.cs,Services,ClassNex.csproj}`、解决方案、发布检查、`tools/verify_settings.ps1`、架构和交接文档。
验证：Debug 全量重建 0 警告 0 错误；Debug 和自包含 win-x64 载荷各通过 32 项真实窗口验收。按 26w46d Core 编译的测试插件实测加载、卸载成功。本次没有打发布标签。
下一步：部署到教室前用现场的投屏、缩放、课表和网络联测；集控仅验证本地模拟下发。复测流程见 `HANDOFF.md` 5.3，不使用真实账户进行自动验收。
