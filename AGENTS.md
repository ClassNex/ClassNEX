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
上次完成：主界面岛移除外部描边；组件库改为 WinUI Gallery ControlExample 的标题、显示区、操作区全宽布局，并修复设置页横向 ScrollViewer 无限测量导致的右侧越界；集控改为管理端与客户端统一 ClassNEX ID/Casdoor 授权码登录，同一账户共享班级并同步完整设置与课程表，彻底移除令牌方案；空集控地址和旧服务器地址会自动迁移到 `http://206.187.201.60:7010`；由于 `auth.smart-teach.cn` 仍指向旧源站并报 `invalid_client`，管理端和客户端暂时统一使用 `http://206.187.201.60:8000` 的新 Casdoor 源站；Casdoor `/casdoor-static` 改为反向代理当前容器资源，避免升级后图标失效
改了文件：src/ClassNex.Central/*、src/ClassNex.Core/Models/AppSettings.cs、src/ClassNex.Core/Models/CentralManagement*.cs、src/ClassNex.Core/Services/SettingsService.cs、src/ClassNex.Core/Services/CentralManagementClient.cs、src/ClassNex/Services/CentralManagementService.cs、src/ClassNex/Views/MainWindow.axaml(.cs)、src/ClassNex/Views/SettingsWindow.axaml(.cs)、docs/CENTRAL_MANAGEMENT.md、HANDOFF.md、AGENTS.md
进行中：无
下一步：待用户实际登录同一 ClassNEX ID，验证集控管理端创建班级后桌面端可选择并同步
