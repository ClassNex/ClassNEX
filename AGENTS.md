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
工作分支：`codex/26w48b-notifications`，基线 `09a6ada`。`src/DesktopVersion.props` 为 `26w48b`；桌面只作预览，不合并 `main`、不推新标签。集控 Web 已经单独上线，不要随桌面复验重复部署。
本次内容：`App.Onboarding` 将完成后的向导、课表、托盘和提醒服务交给同一进程；独立预览仅使用自己的匿名课表。`ClassNex.Notification.Service` 让课前字幕和底轨共享实际上课时刻，手动预览仍限时。`MainWindow.ReminderLayout/ReminderReturn` 处理长文字、超屏滚动和退场时的实时课表尺寸；托盘、NEX 唤醒与档案页适配系统动画偏好。铃声默认静音、无 TTS。原 CI 2.x 同窗导入与便携目录保护保留，验收未动真实安装。
历史验证（2026-11-24）：118 项单测通过（Core 66 / 通知 46 / 插件 6）；原生设置 91 项、托盘 6 条路线共 122 项通过。OOBE 9 条实窗路线共 121 项，其中 5 条完成路线验证进程不变；另验 Debug/Release 各两种预览误用，均未写入真实设置。116 项静态核对和 26 帧逐像素相同是当时等宽布局的结果，见 `docs/26w48b-notifications.md` 与 `docs/26w48b-migration.md`，不代表新版正文。
11 月 27 日通知定位：`MainWindow.Placement` 用常态课表的屏幕中心展开提醒；`MainWindow.ReminderLayout` 将倒计时、课程与时段按实际宽度居中，只有超出工作区才换行。默认居中、导入停靠和用户拖动的位置仍分别处理。复验使用 `tools/review_desktop.ps1 -Round <新名称> -ReminderGeometryOnly`，同时检查原生窗口、文字边界及收起后保存的位置。
11 月 27 日教室端：`NativeSettingsWindow` 主页改为直接打开常用操作，课表与临时调课只在独立档案窗口编辑。导航回退等原卡片加载再恢复焦点。Core 按档案和日期叠加单日课程、课间及活动，不改基础 YAML；桌面/设置共用组件实例，支持按行拖放、命名布局、就地属性和指定时段显示。完整入口、真实截图和已知 CI 差异见 `docs/26w48b-classroom.md`、`docs/profile-day-plans.md`、`docs/widget-editor.md`。
浮窗输入：`MainWindow.Placement/EditMode` 普通显示禁止移动；编辑手柄松手才保存实际屏幕坐标。移入不透明度低于 100% 或开启鼠标穿透时，`WindowsOverlay` 先把窗口设为系统穿透，再独立淡化。编辑与超长提醒暂时接管输入，退出后还原，不改用户设置。档案科目须显式保存，`ScheduleService` 同时更新当前档案的基础、临时课引用，不改同名活动。最新验收：客户端完整重建 0 警告、0 错误；182 项单测、598 项整窗、195 项原生设置；`-DesktopPointerOnly` 正常、减少动画各 20 项，`-ReminderGeometryOnly` 67 项，`-SubjectOnly` 6 项。证据见 `docs/26w48b-classroom.md`。
集控交付分两层：现网 `/Control/` 是已上线的配对版（源文件收录于 `7dd4ddb`），教室输入端免登录、网页选班。预览分支把设置与基础课表分开校验，冲突可以核对草稿；本机保存时保护账户、设备信息和 NEX 私有配置。**不要将预览分支的同步与身份保护写成已部署**。认证和新旧协议兼容表在 `docs/CENTRAL_MANAGEMENT.md`。
不占用输入桌面：从 `tools/run_review_offscreen.py` 执行 WinUI 和 Avalonia 验收，网页用一次性的无头浏览器。`tools/preview_classroom.ps1` 是人工查看入口，会在输入桌面显示；无人值守不要运行。
下一步：`./tools/preview_classroom.ps1 -ExpectedVersion 26w48b -Migration` 试用向导完成后的匿名课表；不加 `-Migration` 可在设置 → 通知服务试播四类提醒。实物音箱、跨显示器混合 DPI 和教室设备帧率未由本机回归覆盖。
发布：`26w48a_Alpha` 已发布为 Windows x64 预览版。[Actions 45093411428](https://github.com/ClassNex/ClassNEX/actions/runs/45093411428) 成功，公开 ZIP 已回下载，547 项文件哈希全部通过。版本说明见 `RELEASE_NOTES.md`，附件摘要见 `docs/26w48a-settings.md`。实体触摸屏、跨显示器混合 DPI 与生产身份提供商仍需现场验证。
发布恢复：`26w48a_Alpha` 固定于 `ffd24fa`。首次校验把 SDK 实际的 `Microsoft.ui.xaml.dll` 误报为缺失，见 `tools/verify_portable.py`。没有发布的既存 Alpha 标签可以从 main 手动派发 `.github/workflows/build-release.yml` 恢复；只更新校验工具，不移动标签、不覆盖已有 Release。
