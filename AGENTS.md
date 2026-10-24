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
上次完成：发布包结构照 CI 重构（新增 src/ClassNex.Launcher 启动器 + tools/make_files_json.py 清单生成器 + GitHub Actions 打包新流程，本地演练通过：启动器拉起 app-版本-0 载荷、首启生成根目录 data）；仓库整理（_tools 改名 tools、根目录日志与 _casdoor_dbpass.txt 等杂项移除、录屏帧移除、CI 参考包移到 E:\ci-app、HANDOFF/README 同步）；OOBE 问候语轮换改到窗口打开后才启动（首屏「你好」可见再开始多语言切换）；发布 26w43b_Alpha（版本号与发布说明按模板，GitHub Actions 自动打包上传）；打包问题修复（启动器补图标、改 GUI 子系统不再弹命令行、双 exe manifest 显式 asInvoker + supportedOS 消除 UAC，26w43b 已重打并覆盖附件）
改了文件：src/ClassNex.Launcher/*（新增）、tools/make_files_json.py（新增）、.github/workflows/build-release.yml、ClassNex.sln、.gitignore、HANDOFF.md、README.md、AGENTS.md
进行中：档案编辑器顶部导航重做（用户要求确认后再做，勿擅动）
下一步：待用户指定
