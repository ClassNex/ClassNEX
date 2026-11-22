# 主线合并与桌面验收 · 2026-11-22

合并前，远端默认分支是 `main`（`2949eff8417f37912724818b2b656f55162b27b8`），源分支是 `refactor/26w46d-flclash-ui-core`（`aaba73d994c7681388d015217d536607655064c0`）。相对共同祖先 `60d7b575b1b866a0bc388c9b80986c546950a2c7`，当时分别有 20、17 个独有提交。合并提交 `b17768d` 保留了这两个父提交、主线的工作流和贡献者历史。没有重置主线，也没有创建发布标签。

## 哪些行为来自哪一边

- **课表浮窗**以已发布的 `26w46d_Alpha` 为对照：`c124cdc` 改过课表排版，主线 `5ef4240` 又换过浮窗宿主。实测用同一套课表、透明度和缩放分别运行标签源码与合并客户端：[原版](../artifacts/mainline-review/overlay-alpha.png)、[恢复后](../artifacts/mainline-review/overlay-restored.png)。原版窗口左右有大片透明区域；新版只截取内容所需窗口，不能用两张 PNG 的画布宽度判断课签大小。下一课用短课签，提示及提醒保留全称。拖动、穿透、置顶及窗口菜单仍走桌面窗口路径。
- **已有布局**按版本迁移。原组合在原位置拆成「日期」和「时间」；继承启用状态与已有样式，不替已经移除的人补装，不复制已有独立时钟。浮窗不再画周型，排课仍按单双周进行。验收实际显示了两项均在、仅日期、仅时间、两项均无、调换后另起进程；也检查了分钟与午夜变化和拖动后重启定位。
- **设置**保留 Windows App SDK 1.8 的原生 `NavigationView` 与 `Frame`。头像在搜索及导航上方；主页显示实时课表和常用入口，通用集中管理启动、托盘与课堂偏好。旧「总览」「系统与启动」路由仍可用。一级页用 `EntranceNavigationTransitionInfo`，子页用 `DrillInNavigationTransitionInfo`；关闭动画时两者都走 `SuppressNavigationTransitionInfo`。仅对右侧页面应用过渡。
- **账户**仍是浏览器 OAuth 授权。切换期间保留原账户，直到新凭据和资料成功落盘。头像缓存按提供商、账户 ID 和图片地址隔开；不信任旧版无归属图片，没有邮箱不占行。回环测试完成两名测试用户的切换、取消、授权失败以及上一用户头像迟到，验证退出会清除登录资料，不影响独立配置的本机字段。
- **便携升级**继续使用原来的启动和 URL 设置键。若旧注册命令确属同一安装目录、仍启用且参数没有自定义，才将目标从会更换的版本目录改为包根的稳定启动器。隔离注册表验证了不创建缺失项、不接管其他安装、不启用被 Windows 禁用的项；另用真实启动器和本地参数接收程序检验了 URL 原样传递。
- **材质**：原主线已有 `MicaBackdrop`，但导航内容层会盖住它。改为在支持时让两层背景透出系统材质，在透明效果关闭、系统不支持或高对比度时使用不透明主题／系统窗口色。失焦保留原生窗口。进程内保存的 WinUI PNG 合成于纯色底上，不能用它们单独证明 DWM 云母；验收同时观察了实际桌面窗口及生效的系统策略。
- **提醒**：详情的展开状态不再代表提醒是否启用。全部类别初始收起，编辑中收到普通刷新保留展开，离开后再进恢复收起。匿名课表在详情全收起时达到了真实课前触发点，显示了完整科目并恢复原浮窗边界。
- **隐私与关于**：对照 ClassIsland 本地源码 `235914a` 的 `PrivacySettingsPage`、`AboutSettingsPage`，仅参考分组及定位方式。ClassNEX 展示自己实际使用的本机目录、浏览器账户、集控、模型与语音服务、受信任插件；关于页从运行程序集读版本，项目、问题及许可链接指向本仓库，更新另有独立页面。
- **首次向导**：去掉中文首屏固定等待和 UI 线程的逐帧淡入，把插画解码、非当页服务的准备移出首帧；取消旧页面任务，完成时才保存草稿。相同口径下，从窗口打开回调到中文可读为 832 → 172 ms，按钮可用为 877 → 173 ms。这不是进程启动时间，也不是 FPS。[前后两段录屏、条件和五步实测](../artifacts/mainline-review/oobe/README.md)。

## 复测环境

Windows 11（build 26200）、.NET SDK 10.0.401。不同轮次的原生设置窗口分别报告 125%、150% 栅格化比例，检查了 620、720、1080 DIP 窗宽及深浅主题；浮窗另向 Avalonia 窗口发送 96、120、144 DPI。高对比度、材质不可用与减少动画通过仅对匿名窗口生效的测试策略覆盖，没有修改 Windows 全局偏好。

| 验什么 | 实测结果与证据 |
| --- | --- |
| 设置空间与主题 | [主页](../artifacts/mainline-review/settings-home.png)、[宽屏通用](../artifacts/mainline-review/settings-general.png)、[窄屏通用](../artifacts/mainline-review/settings-general-narrow.png)。复查修掉了短页标题偏移，以及折叠侧栏的放大镜被按钮内边距裁切的问题。620 和 720 DIP 都完整显示 16 DIP 图标；点它会展开搜索并聚焦输入框，提交「通用」会进入对应页面、收回侧栏。 |
| 交互而不只是截图 | [45 项原生交互记录](../artifacts/mainline-review/native-checks.txt)：点击四张主页卡片后校验目标、左栏及返回；进入档案信息打开真正的编辑器；连续切页的最终焦点、重复点击、已保存启动开关、集控主题、本地账户及折叠提醒均通过。 |
| 动态切页 | [一次主页到通用的实测样本](../artifacts/mainline-review/navigation-sample.json)：系统动画开启，观察约 593 ms，记录到 28 次不同的 XAML 渲染回调。这个窗口包含切页后的观察时间，不等于动画耗时，不据此报告帧率。 |
| 实窗遍历 | [154 项逐项记录](../artifacts/mainline-review/desktop-checks.json)全部通过，同轮生成 41 张实窗截图（31 张 Avalonia、10 张 WinUI），此处仅选留代表画面。拖动后重新命中时钟位置，通过连续两次点按触发一次双击、进入设置主页；穿透状态不移动也不打开设置，另起进程保留位置与组件顺序。 |
| 浮窗与提醒 | [仅显示时间](../artifacts/mainline-review/clock-time-only.png)、[提醒开启而详情全收起](../artifacts/mainline-review/reminders-collapsed.png)。上面的原生交互记录另验证了详情收起时，开启的提醒按实际课表触发。 |
| 构建、单测及兼容 | 客户端完整 Debug 重建和集控编译均为 0 警告、0 错误；Core 14 项、通知 9 项、插件 6 项全部通过，另实测用原 Alpha SDK 编译的插件可加载和卸载。注册项迁移有 20 项隔离检查。 |
| Windows x64 便携启动 | 从产品提交 `5ea3b1150e8af6bea986b2753690e91dbb642f15` 导出干净源树生成自包含 Release。根启动器与 546 个载荷文件均通过 SHA-512；解压时无 `data`，首次运行 5.18 秒观察到 OOBE，在根目录生成 `data/Logs`。正常关闭、退出码 0，无残留进程或崩溃日志。[包指纹与验收明细](../artifacts/mainline-review/portable-summary.json)。这是本地候选，不是 GitHub Release。 |

```powershell
dotnet restore ClassNex.sln
dotnet build src/ClassNex/ClassNex.csproj -c Debug -t:Rebuild
dotnet test ClassNex.sln -c Debug --no-restore
./tools/review_desktop.ps1 -Round my-mainline-review -EnabledReminders
./tools/verify_settings.ps1 -TimeoutSeconds 130
py -3 tools/test_central_oauth.py --desktop src/ClassNex/ClassNex.csproj
./tools/test_alpha_binary.ps1
```

上述检查均使用独立目录；回环账户、课表、设备与插件均为测试数据。没有使用生产 Casdoor 账号或实际教室麦克风，也没有用物理双屏验证跨显示器拖动。未打发布标签、未部署服务器。
