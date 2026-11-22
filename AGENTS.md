# AGENTS.md

Codex 与 DSH 双代理协作约定。任何一个代理开始工作前必须先读本文件与 HANDOFF.md，结束时必须更新本文件再提交。

## 项目

ClassNEX，ClassIsland 风格的课表应用。Avalonia 11 + AvaloniaFluentUI，目标框架 net10.0-windows。仓库 https://github.com/ClassNex/ClassNEX。

## 每次开工必读

1. 本文件
2. HANDOFF.md 项目完整背景与踩坑记录
3. git log 最近 5 条提交

## 铁律

1. 课堂产品逻辑先参照 E:\ci-source。本重构分支的桌面布局另参考 FlClash，只参考结构，不复制素材或实现。
2. 客户端保持 Avalonia 与 AvaloniaFluentUI。常规窗口复用 Fluent 控件和主题；桌面课表保留透明浮窗，不能强制套用设置窗口的窗口装饰。
3. 构建必须 0 警告 0 错误，命令 dotnet build src\ClassNex\ClassNex.csproj -c Debug -t:Rebuild
4. 增量构建会谎报 0 错误但 XAML 编译失败，报错 No precompiled XAML 时用 -t:Rebuild 看真实错误
5. 改文本文件只用编辑工具，PowerShell 写文件必须 UTF8Encoding false，禁止 Set-Content
6. git push 前确认代理 127.0.0.1:7890 通；推送后核对 HEAD 与本次获准推送的远端分支。不得用重构分支覆盖 main。
7. 打包发布只推 26w* 标签，GitHub Actions 会自动构建并发布，作者身份已绑定 LingOfficial；发布包结构照 CI：app-<版本>-<序号> 载荷目录 + ClassNex.exe 启动器 + PackageType（内容 folder）+ files.json（SHA512 清单），data 由首次启动在根目录生成
8. 不添加无意义的代码注释；可单独打包的 Core 公共契约提供 XML 文档。
9. 字体加粗前先征得用户同意。颜色复用 App.axaml 的主题资源、Fluent 系统画刷或浮窗的 CiPalette，避免每页写死颜色。
10. Windows 窗口验收优先使用 `tools/review_desktop.ps1` 的独立匿名目录；原有 `CLASSNEX_VERIFY` 数据自检仍可用。
11. 所有布尔设置一律使用 Avalonia `ToggleSwitch`，默认显示「开 / 关」，禁止再使用方形 `CheckBox`

## 交接协议

1. 任何一方结束工作前必须 git add -A 加 git commit 加 git push
2. 更新下方当前状态块，写清做完了什么、下一步做什么、改了哪些文件
3. 另一方开工先读本文件，按当前状态续做，不重复已完成的步骤
4. 双方不要同时改同一批文件，以本文件的持有者为准

## 当前状态

持有者：无
工作分支：`refactor/26w46d-flclash-ui-core`，直接从 `26w46d_Alpha`（`60d7b57`）建立，不含 WinUI 3 重构。
本次内容：Core 的课表缓存与持久化，独立提醒/插件模块，Avalonia 桌面浮窗、设置导航与主题，账户与自动集控同步，首次向导正常退出。产品提交 `4f2e667`；23 项单测、170 项实窗检查、便携包首启与退出均通过。代码、截图、性能数据和复测命令见 `docs/26.0-acceptance.md`。
Git 边界：执行前远端 main 已从授权值 `2815958` 前移到 `2949eff`，后者已备份到远端备份分支和独立离线 bundle。未重置 main，未发布新标签。不应自行放宽 SHA 检查。
下一步：实地验证教室的麦克风、物理显示器和生产身份提供商，再决定是否发布。本机测试使用匿名档案与回环身份服务。
