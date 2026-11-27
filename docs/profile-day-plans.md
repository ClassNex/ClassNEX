# 编辑档案：按日期调课

在「编辑档案 → 调课」选一个日期。编辑器先显示该日的已安排课表；未安排时，从当天的基础课表和实际单双周取课。可以直接改课，也可以展开「从其他教学日复制」调入另一日的基础课程。复制和编辑都只是草稿。「应用到这一天」才会影响课堂。去掉所有课程再应用，表示当天停课。

每个档案、每个自然日只有一份生效的临时课表。可以提前安排多天，也可以「撤销当天调课」；到下一天，按新日期重新选择安排，无需程序一直运行。未应用的草稿只保存在本次编辑器会话中，切换日期不会丢失；应用过的安排重启后仍在。

## 只调整当天的作息

展开「当天作息」，开启「单独设置这一天的作息」。首次开启时，以基础时间表的课间、活动和分隔点为草稿，可以改名称和时间、增删时段；上课时段由「当天的课程」决定。确认后点「应用到这一天」。

- 课间和活动不能与授课时段重叠。同类的非定点时段不能重叠；活动可以安排在课间内，此时浮窗先显示活动名称和倒计时，活动结束后恢复该课间。最后一节课之后仍可安排活动。
- 分隔点和起止相同的活动只标记时刻，不占用浮窗的倒计时。开启独立作息后移除所有作息项并应用，表示当天不安排课间或活动，不会按课程空档补出课间。
- 关掉开关并应用，即恢复基础作息，仍保留当天的课程改动。「撤销当天调课」则同时撤销当天的课程与作息安排。

## 数据边界

- 桌面以本地教学日为准，读取优先级是指定日期的临时课表 → 常规星期和单双周课表。浮窗缓存、提醒调度和助手查询使用同一份按日期解析结果。
- 即使基础档案没有科目或课程，浮窗和设置主页仍识别当天已安排的临时课。显式安排停课显示当天无课；撤销最后一份临时安排才恢复原来的未配置状态。
- 单日安排写入便携 `data/TemporarySchedules/` 中的独立 JSON 文件。文件身份基于课表路径；便携目录内的课表使用相对标识，外部课表使用完整路径。只改临时安排不会写入基础 YAML，也不发布集控基础课表更改。
- 写盘成功后才切换生效安排。调课文件损坏时保留原件、使用基础课表，并在编辑器说明原因。重启忽略过期记录；下一次用户写入时清理相应文件中的过期项。
- 基础作息中的课间、活动和分隔点不被单日调课改写。标准 CSES 导出仍只包含基础档案的标准字段；ClassNEX 的本地临时安排、作息扩展和室外标记不写入交换文件。
- 未设独立作息时，桌面也会读取基础时间表中命名的课间和活动。例如 08:45–09:00 的大课间里，08:50–08:55 的眼保健操在 08:52:10 显示 `-2:50`，到 08:55 又显示大课间。浮窗和组件显示条件用同一条作息；只有没有实际课间或活动的旧课表才从课程之间的空档推算课间。
- 对照本地 CI `235914a2fac1` 的 `ProfileService.CreateTempClassPlan`、`CleanExpiredTempClassPlan` 和 `LessonsService.GetClassPlanByDate`：先取指定日期、可复制一份作息、过期回退。ClassNEX 保持自己的文件格式和编辑布局，不包含 CI 的临时课表组。

## 档案工作区

「课表」在宽窗把科目池放在课表右侧，窄窗放到七天课表下方。星期和日期在两种宽度下均保留完整文字。「时间表」把时间轴和编辑区分开：未选中时间点时只显示提示；选中后显示该时间点的 24 小时制输入。切换主题时，时间轴跟随当前窗体的主题资源。

## 隔离验收

Core 测试：`dotnet test tests/ClassNex.Core.Tests --filter 'FullyQualifiedName~TemporaryScheduleTests|FullyQualifiedName~TemporaryTimeLayoutTests'`。通知服务测试：`dotnet test tests/ClassNex.Notification.Tests`。

同一套控件用例支持 Avalonia.Headless 和真实 Windows 两种后端；测试专用依赖不进入客户端。以下 PowerShell 从仓库根目录运行，给每次验收分配一个新的匿名目录。测试不发送系统输入、不读取真实账户；任何截图都只取本进程的窗口，不截屏幕或其他应用：

```powershell
$qaBuild = Join-Path (Get-Location) 'build/profile-editor-qa'
dotnet build tools/ClassNex.ProfileReview -c Debug -t:Rebuild --artifacts-path $qaBuild
if ($LASTEXITCODE -ne 0) { throw '构建失败' }
foreach ($qaMode in 'headless', 'windows-offscreen') {
    $qaRoot = [IO.Directory]::CreateTempSubdirectory('classnex-profile-')
    $qaApp = Join-Path $qaRoot.FullName 'app'
    Copy-Item -LiteralPath (Join-Path $qaBuild 'bin/ClassNex.ProfileReview/debug') -Destination $qaApp -Recurse
    $qaDll = Join-Path $qaApp 'ClassNex.ProfileReview.dll'
    if ($qaMode -eq 'headless') { dotnet $qaDll $qaRoot.FullName }
    else { py -3 tools/run_review_offscreen.py --timeout 120 -- dotnet $qaDll $qaRoot.FullName --windows-offscreen }
    if ($LASTEXITCODE -ne 0) { throw "验收未通过：$qaMode" }
    Write-Output (Join-Path $qaRoot.FullName 'review.json')
}
```

Headless 验证控件、布局与数据，不验证原生窗材质或系统缩放。Windows 模式须处于运行器创建的非输入桌面，在普通桌面调用会在创建数据之前拒绝。它验证 HWND 属于测试进程且可见，优先使用 `PrintWindow` 获取客户区。如果 Windows 不提供有效窗口像素，会明确标记为 Avalonia 内容渲染，不作为合成器效果的证据。浅→深→浅的时间轴对比度按控件实际解析的主题画刷和不透明画布计算，不代表任意壁纸下的合成对比度；报告分别记录 Avalonia 材质及原始 DWM 属性。

验收使用同一份中学校运动会安排：周五原为语文，调课后 08:00–09:20 为体育，09:30–10:15 为班会；补水休息 09:20–09:30 中安排接力颁奖 09:23–09:27，09:27 标记检录结束，10:15–10:25 为闭幕集合。报告的 `Scenario` 从实际保存的单日安排读取日期、名称、课程和作息。浮窗用例检查活动切换、精确倒计时、末节之后的活动和退出独立作息后的回退。

2026-11-27 实测：[Headless 控件报告](../artifacts/26w48b-profiles/headless/review.json) 62 项通过，保留 10 张图；[私有 Windows 桌面报告](../artifacts/26w48b-profiles/windows-offscreen/review.json) 74 项通过，11 张图均以 `PrintWindow` 获取测试窗口客户区，系统缩放 125%。课程、课间和活动的文字在浅色、深色、切回浅色时均达到 4.5:1 的对比度检查。浮窗另检查了基础作息保存后立即生效和系统时钟回拨。

[浅色宽窗](../artifacts/26w48b-profiles/windows-offscreen/swap-light-1080.png)和[深色窄窗滚动后](../artifacts/26w48b-profiles/windows-offscreen/swap-dark-700-scrolled.png)显示课程字段与固定操作区；[浅色时间轴](../artifacts/26w48b-profiles/windows-offscreen/workspace-time-light-1080.png)和[深色时间轴](../artifacts/26w48b-profiles/windows-offscreen/workspace-time-dark-700.png)显示课间、活动和时间。本机 Avalonia 报告 Mica；在测试窗口临时取消材质后，验证了[不透明主题回退](../artifacts/26w48b-profiles/windows-offscreen/swap-dark-700-fallback.png)。这些不是实体教室设备、触控或多显示器测试。
