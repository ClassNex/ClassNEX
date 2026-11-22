# 日课表查询：同机对照

在同一台 Windows 机器、.NET SDK 10.0.401、Release、`DOTNET_TieredCompilation=0` 条件下运行 `tools/MeasureSchedule`。原版使用从 `26w46d_Alpha` 导出的 Core，并将其全部 26 个文件逐个与标签中的 Git 对象核对。两次运行使用同一份探针源码，各自启动一个新的进程。

| `GetTodaySummary` | 中位每次耗时 | 每次托管分配 | 结果校验值 |
| --- | ---: | ---: | ---: |
| `26w46d_Alpha` (`60d7b57`) | 23.354 μs | 3104 B | 2076000 |
| 本次重构 | 1.234 μs | 336 B | 2076000 |

数据为五个工作日、每天十二节课。先预热 12,000 次，再跑五轮、每轮 30,000 次；星期、时分秒在查询间变化。两种版本的当天课程数、当前课与下一课的累计校验一致。按日与档案修订缓存后，每次不再重复解析时间或创建所有课程对象；为保持公共 API 兼容，调用者仍获得可独立修改的时段列表。

此结果衡量稳定档案的查询路径，不包括首次读盘、应用工作集或 UI 帧率。时延随机器而变。复现当前版本：`dotnet run --project tools/MeasureSchedule -c Release`；对照原版时为同一工程指定 `-p:ProbeCore=<已导出的 Alpha Core csproj 绝对路径>`，使用不同的 `--artifacts-path`。
