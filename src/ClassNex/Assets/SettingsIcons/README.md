# 设置窗口彩色图标

来自微软 [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons/tree/6c2c9075cf8952ef48369b2475233b6e9761efed/assets) 的 Color 资源；上游版本固定为 `6c2c9075cf8952ef48369b2475233b6e9761efed`。授权见同目录 `Fluent-LICENSE.txt`，随应用一起分发。

| 本地 PNG | 上游图标目录 |
| --- | --- |
| `home` | Home |
| `profile` | Calendar |
| `basic` | Clipboard Task |
| `appearance` | Paint Brush |
| `window` | Laptop |
| `widgets` | Puzzle Piece |
| `nex` | Bot |
| `notification` | Alert |
| `account` | Person |
| `about` | Book Open |
| `debug` | Code |
| `settings` | Settings |

每张图使用 `assets/<上游图标目录>/SVG/ic_fluent_<图标名小写且以底线分词>_24_color.svg`，等比例栅格化为 192 × 192 透明 PNG。保留原色、渐变、透明留白。导航与设置首页共用这些本地资源，不依赖网络加载。设置行和标题栏的单色字形使用 Windows 自带的 Segoe Fluent Icons 或 Segoe MDL2 Assets，不随包分发系统字体。
