# ClassNEX 插件 SDK

插件项目引用 `src/ClassNex.Core/ClassNex.Core.csproj`，入口类型实现
`ClassNex.Core.Abstractions.IClassNexPlugin`。

插件部署在应用目录的 `plugins/<插件 ID>/`。目录中需要 `plugin.json`：

```json
{
  "Id": "example.plugin",
  "Name": "示例插件",
  "Version": "1.0.0",
  "Assembly": "ExamplePlugin.dll",
  "EntryType": "ExamplePlugin.Entry",
  "Enabled": true
}
```

入口示例：

```csharp
using ClassNex.Core.Abstractions;

public sealed class Entry : IClassNexPlugin
{
    private PluginContext? _context;

    public void Initialize(PluginContext context)
    {
        _context = context;
    }

    public void Shutdown()
    {
        _context = null;
    }
}
```

`PluginContext` 提供：

- `Services`：课程、时间、组件等核心服务。
- `Notification`：公开通知测试入口。
- `Ai`：创建独立 AI Agent 会话。

插件程序集使用可回收加载上下文。`ClassNex.Core` 始终由主程序共享，避免类型身份不一致。
