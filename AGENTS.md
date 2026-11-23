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
工作分支：`codex/26w48b-notifications`，基线 `09a6ada`。`1052b26` / `3d1b5d5` 为课堂调度、浮窗和设置侧栏；`89a00ad` 处理末尾课间、真实课间名称与正式提醒优先；`a351590` 记录侧栏 UI 响应与时钟跨进程回归。`src/DesktopVersion.props` 为 `26w48b`；只作预览，不合并 `main`、不推新标签、不部署服务器。
本次内容：`ClassNex.Notification.Service` 与透明浮窗对齐课前、上课、下课、放学的展示顺序与持续时间；原生通知页提供四种事件预览。铃声默认静音、没有 TTS，本机 WAV 不参与集控。`SettingsWindow` 使用可视宽度排版；窄窗覆盖菜单和宽屏手动选择分开管理，切页不丢刚输入的文字。
验证：客户端 Debug 完整重建、集控服务构建均 0 警告 / 0 错误；63 项单测、90 项原生设置交互通过；全套桌面报告含 447 项通过的检查与 1 项侧栏响应采样。620–1240 DIP 下反复开合、125%/150% 缩放、长课名与浮窗复位已验证。同宽实窗前后对照、报告及复测命令见 `docs/26w48b-notifications.md`。下一步由用户在匿名预览上试用；实物音箱、混合 DPI 显示器和教室现场体验不由本机回归替代。
发布：`26w48a_Alpha` 已发布为 Windows x64 预览版。[Actions 45093411428](https://github.com/ClassNex/ClassNEX/actions/runs/45093411428) 成功，公开 ZIP 已回下载，547 项文件哈希全部通过。版本说明见 `RELEASE_NOTES.md`，附件摘要见 `docs/26w48a-settings.md`。实体触摸屏、跨显示器混合 DPI 与生产身份提供商仍需现场验证。
发布恢复：`26w48a_Alpha` 固定于 `ffd24fa`。首次校验把 SDK 实际的 `Microsoft.ui.xaml.dll` 误报为缺失，见 `tools/verify_portable.py`。没有发布的既存 Alpha 标签可以从 main 手动派发 `.github/workflows/build-release.yml` 恢复；只更新校验工具，不移动标签、不覆盖已有 Release。
