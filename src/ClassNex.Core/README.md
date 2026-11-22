# ClassNex.Core

ClassNEX 的 .NET 10 课表与扩展契约。不引用 Avalonia、桌面入口或平台窗口。库包含 CSES YAML、单双周、时间表、便携设置和插件使用的公共接口。项目内引用即可；没有向 NuGet 发布。

`ScheduleService` 管理活动档案。`TimeService.GetTodaySummary(profile, now)` 提供与指定时刻对应的当前课和下一课。上课起点包含在课程内，下课终点不包含；课间没有当前课。按档案、星期与单双周缓存转换后的课段，不持有已释放的档案，也不向调用方暴露可修改的缓存。

通过 `ScheduleService` 修改、导入或保存档案会自动更新缓存。自行构造的 `ScheduleProfile` 可用于无文件计算；第一次查询之后若直接改集合，请调用 `TimeService.Invalidate(profile)`。在桌面编辑器直接改活动档案的字段后调用 `ScheduleService.Save()`。

`ISettingsService` 的默认实现 `SettingsStore` 接受独立 JSON 路径，写入使用同目录临时文件与替换，不必使用全局数据目录。旧版本的 `SettingsService` 静态入口继续兼容。

插件入口实现 `ClassNex.Core.Abstractions.IClassNexPlugin`，宿主共享本程序集。API 级别独立于包版本；历史插件不需要改名或引用桌面程序。

性能探针位于仓库 `tools/MeasureSchedule`：`dotnet run --project tools/MeasureSchedule -c Release`。使用相同的五日、每日十二节模拟课表，预热后报告五轮查询中位耗时、每调用分配字节和结果校验值。可通过 MSBuild 属性 `ProbeCore` 指向导出的原版 Core 项目进行同机对照；这不是整个客户端的内存或帧率测量。
