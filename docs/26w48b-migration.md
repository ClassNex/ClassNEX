# 从 ClassIsland 2.x 迁移到 ClassNEX

首次打开 ClassNEX，在欢迎页选择「同意并继续」即可进入「数据与迁移」。两个入口在同一个向导窗口中，不需要先退出向导，也不用手工导出配置。

- **作为新设备设置**：直接进入偏好设置，不读取旧安装。
- **从 ClassIsland 2.x 完整迁移**：选择旧安装根目录的 `ClassIsland.exe`，也可以选择对应 `app-2.*` 下的 `ClassIsland.Desktop.exe`。选择程序只做预检，不运行它。先看可迁移的档案数、组件数和跳过项，再点「开始迁移」。

经典便携版（`folderClassic`）读取原桌面程序旁边的配置；一个启动器下面有多个可能的经典版本时，向导会请你直接选原来使用的 `ClassIsland.Desktop.exe`。经典版和安装版只导入，向导不会替你卸载或回收它们。

## 会带过来哪些内容

| 来源 | ClassNEX 中的结果 |
| --- | --- |
| 浮窗和外观 | 窗口缩放、透明度（含 0%）、圆角、停靠及偏移、置顶、显隐、点击穿透；主题跟随系统或使用原先的深浅模式，并保留可用的强调色、自定义前景和背景。壁纸取色保留上次保存的颜色，不复制壁纸。 |
| 活动组件布局 | 保留对应组件的先后、重复实例和开关状态。支持日期、时钟、课表、分隔线、静态文本和固定日期倒计时；相应的秒数选项、文本、颜色、边距等按组件导入。没有对应实现的组件跳过，不装载旧 DLL。 |
| 本地课表档案 | 迁入可表达的科目、简称、教师、单双周、课时，以及作息中的课间、分割线和行动名称。每个档案单独保存为工作课表，原先选中的档案自动启用。用「档案管理 → 切换档案」可以打开同目录的其他档案。 |

导入不是复制整套软件。多行布局会按行展开，多个作息模板中的课程保留各自起止时刻，编辑器使用最常用的模板。按具体日期预排的临时调课会在预检中提示数量和涉及的日期，不会被误写为每周重复的课。插件专属组件、复杂轮换、条件规则及可执行行动不会在 ClassNEX 中自动运行。源账户、令牌、插件程序集和缓存不会写进 ClassNEX。

## 原目录如何处理

「高级设置 → 迁移完成后删除 ClassIsland 文件夹」默认开启。即使高级设置处于折叠状态，提交前也能看到原目录将如何处理。想并行试用两款软件时，先关闭此开关。

迁移会重新检查源文件，将目标设置和课表落盘并回读验证。只有这些步骤都成功，才会尝试将**独立便携版的整个目录移到 Windows 回收站**，不作永久删除。安装版、根目录或版本目录混入其他文件的安装、共享或重叠目录、包含链接的目录、无法确认已退出的原程序以及无法安全回收的位置均保留。旧 `data` 内的插件、缓存及其他未迁入内容也会随整目录进入回收站；要找回时还原该目录即可。打开设置里的向导预览不会写入或回收文件。

## 实际窗口与验证

[两个入口](../artifacts/26w48b-migration/data-and-migration.png) → [选程序后的预检](../artifacts/26w48b-migration/migration-ready.png) → [迁移中](../artifacts/26w48b-migration/migration-working.png) → [完成回执](../artifacts/26w48b-migration/migration-result.png)。忙时只在标题下面显示进度条，预检和完成才显示档案与组件计数。实窗测试也把标题压窄至换行，检查轨道和可见文字的实际边界没有相交。

2026-11-24 在同一份 Windows Debug 客户端上完成了以下验证：

- [保留原目录](../artifacts/26w48b-migration/migration-keep.json)和[回收便携测试副本](../artifacts/26w48b-migration/migration-recycle.json)各通过 22 项。两条路线均另起进程，确认「地理」、命名课间、日期 → 时钟 → 课程顺序、自定主题色以及浮窗显隐没有丢失。
- [新设备直接完成](../artifacts/26w48b-migration/new-device.json)通过 10 项；[减少动态效果与取消草稿](../artifacts/26w48b-migration/reduced-motion.json)通过 13 项。选择新设备不读取旧安装，不必为了继续而登录或打开麦克风。
- 上游公开示例档案实际导入得到 2 节课、4 个作息时间点，原件哈希未改变。经典版、混入用户文件、目录联接、原程序运行中、取消与目标设置并发写入等边界另由 Core 测试覆盖。验收没有回收真实安装。

## 本地复验

以下命令只从参考便携包取程序文件；两次运行各生成独立的匿名配置，在临时安装上实际完成迁移和重启。不要拿正在使用的安装目录测试回收。

```powershell
dotnet build src/ClassNex/ClassNex.csproj -c Debug -t:Rebuild
./tools/preview_classroom.ps1 -ExpectedVersion 26w48b -Migration  # 只读预览，同一窗口
./tools/review_oobe_motion.ps1 -Mode migration-keep -ClassIslandPackage 'E:\ci-app\ClassIsland_app_windows_x64_full_folder'
./tools/review_oobe_motion.ps1 -Mode migration-recycle -ClassIslandPackage 'E:\ci-app\ClassIsland_app_windows_x64_full_folder'
./tools/verify_classisland_example.ps1  # 上游公开的示例课表；不回收源
```
