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
工作分支：`main`。按功能合并主线 `2949eff` 与 `refactor/26w46d-flclash-ui-core` 的 `aaba73d`，共同祖先 `26w46d_Alpha`（`60d7b57`）。不重置分支，不删除原分支或推送发布标签。
本次内容：原生设置的 11 个入口、账户侧栏、Mica 与右侧 Frame；恢复 Alpha 浮窗、日期与时间独立迁移、可拖动与重启定位；OOBE 首屏、导航与退出；保留独立 Core、提醒、插件、NEX 和自动集控。落点见 `src/ClassNex.WinUI`、`src/ClassNex/Views` 及 `src/ClassNex.Core/Models/AppSettings.cs`。
验证：主线记录见 `docs/mainline-desktop-acceptance.md`；`docs/26.0-acceptance.md` 只记录此前重构分支。账户切换、头像归属、同安装根旧启动命令迁移均有独立回归；不以真实账户、系统注册项或生产服务器跑夹具。
后续：教室里的真实身份提供商、物理多屏与麦克风仍需现场验证；正式发布另行决定。
