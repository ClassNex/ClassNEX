# 主线合并与桌面验收 · 2026-11-22

合并前，远端默认分支是 `main`（`2949eff8417f37912724818b2b656f55162b27b8`），源分支是 `refactor/26w46d-flclash-ui-core`（`aaba73d994c7681388d015217d536607655064c0`）。相对共同祖先 `60d7b575b1b866a0bc388c9b80986c546950a2c7`，当时分别有 20、17 个独有提交。保留两个父提交以及主线的工作流、贡献者历史。没有重置主线，也没有创建发布标签。

## 哪些行为来自哪一边

- **课表浮窗**以已发布的 `26w46d_Alpha` 为对照：`c124cdc` 改过课表排版，主线 `5ef4240` 又换过浮窗宿主。实测用同一套课表、透明度和缩放分别运行标签源码与合并客户端：[原版](../artifacts/mainline-review/overlay-alpha.png)、[恢复后](../artifacts/mainline-review/overlay-restored.png)。原版窗口左右有大片透明区域；新版只截取内容所需窗口，不能用两张 PNG 的画布宽度判断课签大小。下一课用短课签，提示及提醒保留全称。拖动、穿透、置顶及窗口菜单仍走桌面窗口路径。
- **已有布局**按版本迁移。原组合在原位置拆成「日期」和「时间」；继承启用状态与已有样式，不替已经移除的人补装，不复制已有独立时钟。浮窗不再画周型，排课仍按单双周进行。验收实际显示了两项均在、仅日期、仅时间、两项均无、调换后另起进程；也检查了分钟与午夜变化和拖动后重启定位。
- **设置**保留 Windows App SDK 1.8 的原生 `NavigationView` 与 `Frame`。头像在搜索及导航上方；主页显示实时课表和常用入口，通用集中管理启动、托盘与课堂偏好。旧「总览」「系统与启动」路由仍可用。一级页用 `EntranceNavigationTransitionInfo`，子页用 `DrillInNavigationTransitionInfo`；关闭动画时两者都走 `SuppressNavigationTransitionInfo`。仅对右侧页面应用过渡。
- **材质**：原主线已有 `MicaBackdrop`，但导航内容层会盖住它。改为在支持时让两层背景透出系统材质，在透明效果关闭、系统不支持或高对比度时使用不透明主题／系统窗口色。失焦保留原生窗口。进程内保存的 WinUI PNG 合成于纯色底上，不能用它们单独证明 DWM 云母；验收同时观察了实际桌面窗口及生效的系统策略。
- **提醒**：详情的展开状态不再代表提醒是否启用。全部类别初始收起，编辑中收到普通刷新保留展开，离开后再进恢复收起。匿名课表在详情全收起时达到了真实课前触发点，显示了完整科目并恢复原浮窗边界。
- **首次向导**：去掉中文首屏固定等待和 UI 线程的逐帧淡入，把插画解码、非当页服务的准备移出首帧；取消旧页面任务，完成时才保存草稿。相同口径下，从窗口打开回调到中文可读为 832 → 172 ms，按钮可用为 877 → 173 ms。这不是进程启动时间，也不是 FPS。[前后两段录屏、条件和五步实测](../artifacts/mainline-review/oobe/README.md)。

## 复测环境

Windows 11（build 26200）、.NET SDK 10.0.401。设置在实际 125% 桌面缩放下检查了 620、720、1080 DIP 窗宽及深浅主题；浮窗另向 Avalonia 窗口实际发送 96、120、144 DPI。高对比度、材质不可用与减少动画通过仅对匿名窗口生效的测试策略覆盖，没有修改 Windows 全局偏好。

```powershell
dotnet restore ClassNex.sln
dotnet build src/ClassNex/ClassNex.csproj -c Debug -t:Rebuild
dotnet test ClassNex.sln -c Debug --no-restore
./tools/review_desktop.ps1 -Round my-mainline-review
./tools/verify_settings.ps1 -TimeoutSeconds 130
py -3 tools/test_central_oauth.py --desktop src/ClassNex/ClassNex.csproj
./tools/test_alpha_binary.ps1
```

上述检查均使用独立目录；回环账户、课表、设备与插件均为测试数据。没有使用生产 Casdoor 账号或实际教室麦克风，也没有用物理双屏验证跨显示器拖动。未打发布标签、未部署服务器。
