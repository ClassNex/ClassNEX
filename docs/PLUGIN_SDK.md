# ClassNEX 插件 SDK

入口程序集只引用 `ClassNex.Core`，目标为 .NET 10，入口类型实现 `IClassNexPlugin` 并提供公开无参构造函数。宿主为 `ClassNex.Plugin`。插件 API 级别为 `1`，与应用显示版本无关。`26w46d_Alpha` 的 `IClassNexPlugin`、`PluginContext` 和原有成员保留在同名、同程序集版本的 Core 中；旧清单无需更改。

## 安装与发现

将一个受信任的插件放入载荷的 `plugins/<目录>/`。以下是完整清单 `plugin.json`，依赖的是另一个已经安装的插件 ID，值是其最低数字版本；没有依赖时省略 `Dependencies`。

```json
{
  "Id": "school.lesson-board",
  "Name": "课前提示牌",
  "Version": "1.2.0",
  "Assembly": "SchoolBoard.dll",
  "EntryType": "SchoolBoard.Entry",
  "ApiVersion": 1,
  "Enabled": true,
  "Permissions": ["schedule", "notifications"],
  "Dependencies": { "school.calendar": "1.0.0" }
}
```

插件 ID 以 ASCII 字母或数字开头，可包含 `.`、`-`、`_`，最多 120 字符；匹配不区分大小写。入口 DLL 的相对路径应位于插件自己的目录。托管依赖及其 `.deps.json`、本机运行库应随插件一起分发；Core 一律使用宿主版本。依赖项只保证初始化顺序，不会将两个插件的私有类型变成共享类型。不兼容的 API、缺失或过低版本的必需依赖、循环、重复 ID 和失败的初始化都会显示为插件状态，不阻止其他独立插件加载。

## 使用宿主能力

`schedule` 提供 `context.Schedule`、`context.Time` 与旧版的课表时间布局服务；`widgets` 提供 `context.Widgets`；`notifications` 提供 `context.Notification`，且应用登记发布接口后可使用 `INotificationPublisher`；`ai` 提供 `context.Ai`。声明了 `Permissions` 之后，未授权服务不会通过 `Services.GetService` 暴露；直接调用禁止的通知或 AI 方法会抛出 `UnauthorizedAccessException`。空数组表示不使用宿主服务。没有 `Permissions` 的 Alpha 插件保留原有服务访问，在管理页标记兼容模式。

`context.ConfigurationDirectory` 位于便携数据的 `PluginSettings/plugins/<ID>/`，插件可在此存储配置。启停偏好保存在单独的 `PluginSettings/enabled.json`；点击开关不会编辑插件作者的 `plugin.json`。目录有写权限且已载入的插件才能正常使用配置文件；系统 I/O 错误会报告到加载状态。

## 生命周期与信任边界

`Initialize` 在后台调用；需要更新窗体时由插件自行调度到 UI 线程，不要阻塞等待 UI。初始化要快速返回。`Shutdown` 按启动顺序的逆序执行，也可能在部分初始化失败时调用；务必撤销宿主事件、取消后台任务、关闭文件以及释放自身资源。开关只影响目标插件和依赖它的插件，无关实例保持运行；显式「重新扫描」才重新发现目录和加载所有实例。启停偏好写盘前可取消；一旦写盘，本次依赖链变更将执行完毕。`Changed` 在提交整批状态后发出，界面接收方应切换到自己的调度器。

如果启停配置损坏，宿主仍显示发现的插件，但不默认执行其代码。用户下次显式启用插件时才备份损坏的文件并重建启停配置；其他插件保持禁用。

每个实例使用可回收的 `AssemblyLoadContext`。卸载是协作式的：宿主会释放自身引用，但不能终止仍在运行的插件线程、外部静态引用或插件私自登记的事件。程序集隔离与能力声明**不是安全沙箱**；进程内 DLL 与应用具有相同的操作系统权限，不能保护文件、网络或密钥不被恶意插件访问。仅安装并启用可信来源的 DLL。

示例工程在 `tests/Fixtures/ClassNex.SamplePlugin/`；`dotnet test tests/ClassNex.Plugin.Tests` 会把真实 DLL 复制到临时插件目录，实际加载、失效、停用和卸载，不使用在线账户或生产配置。
