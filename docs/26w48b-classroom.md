# 26w48b 教室端与集控

## 从哪里开始

- **上课前改课表**：设置主页的「编辑课表」直接打开已有的档案窗口；「临时调课」直接进入该窗口的调课页。侧栏、搜索和返回历史都不会再另开一套设置内课表。主页的快捷操作可以自定义。
- **当日调课**：在独立编辑器选择日期、调整课程并应用，只影响所选档案和日期。可以预先安排或撤销，基础课表保持原样。改科目名时，同一档案内的常规课和临时课会一并更新。见[教学日与作息](profile-day-plans.md)。
- **编排桌面**：设置 → 组件与主界面 → 组件布局；或在托盘选择「编辑主界面」，直接拖动桌面组件，点齿轮调当前组件，Esc 退出。见[操作和对照表](widget-editor.md)。
- **不挡住课件**：普通状态下拖动课表不会改位置。编辑时才可移动整条课表；设置了移入淡化时，不必等动画播完就能点到后方窗口。见[穿透与移动](widget-editor.md#上课时点击课表后面的内容)。
- **设置提醒**：设置 → 通知服务 → 上下课提醒。图标、文字和箭头共用一整行点击区域。展开后仍使用原生开关、数值框和文本框。倒计时底轨使用真实的上课时刻，提醒结束恢复课表原来的位置和大小。
- **账户和集控分开**：桌面仍可用系统浏览器登录、切换 ClassNEX ID；教室仅需连接集控时，不必登录。点击「集控与自动化 → 获取配对码」，管理员在[集控网页](https://classnex.tech/Control/)登录并输入屏幕上的码，可选已有班级或沿用教室课表新建班级。见[鉴权、配对和自动同步](CENTRAL_MANAGEMENT.md)。
- **关于、更新**：产品名称和本机版本放在一起，项目主页、问题反馈、随附的离线许可可以直接打开。更新页把「检查更新」与安装区分开；没有联网或无法连到 GitHub 时给出重试和发布页入口，不后台安装。

## 实现依据

设置保留原生 WinUI 3 与 Mica；参照 Windows 的 [NavigationView](https://learn.microsoft.com/windows/apps/develop/ui/controls/navigationview) 和 [Mica 分层](https://learn.microsoft.com/windows/apps/design/style/mica)，收起导航不会另建页面。档案保留 Avalonia，并在材质不可用时使用主题底色。网页使用 HTML/CSS 实现一致的信息层级和间距，不依赖 Windows，不能把它称作原生 WinUI 3。没有借改集控网页替换课堂浮窗的设计。

设置主页参考 Windows 11「主页」的可操作快捷入口，不搬设备信息或广告；中学生教室最常用的编辑课表、调课、组件与提醒放在第一屏。关于与更新对照本地 ClassIsland `235914a` 的 `AboutSettingsPage` / `UpdateSettingsPage` 以及 [Class Widgets 2 的 About.qml](https://github.com/RinLit-233-shiroko/Class-Widgets-2/blob/cbecd22f6c1b25cba9741cc6c1cbf3d6431e9060/src/qml/ClassWidgets/pages/settings/About.qml)。只借鉴信息分组，不复制横幅、图标或实现。组件交互的兼容边界单列于[组件编辑](widget-editor.md)。

## 验证与交付边界

图形验收使用临时桌面、匿名数据和本机模拟身份服务。没有切换当前输入桌面、控制鼠标键盘、代登真实账户或使用教室个人档案。截图和报告位于[本次验收](../artifacts/26w48b-classroom-review/)和[独立档案实窗](../artifacts/26w48b-profiles/windows-offscreen/)。典型命令：

```powershell
dotnet build src/ClassNex/ClassNex.csproj -c Debug -t:Rebuild
dotnet test ClassNex.sln -c Debug
py -3 tools/run_review_offscreen.py --timeout 450 -- pwsh -NoProfile -File tools/review_desktop.ps1 -Round classroom-review-new
py -3 tools/run_review_offscreen.py --timeout 320 -- pwsh -NoProfile -File tools/verify_settings.ps1 -ExpectedProductVersion 26w48b -TimeoutSeconds 245
dotnet build tools/ClassNex.PairingReview -c Release
py -3 tools/run_review_offscreen.py --timeout 240 -- py -3 tools/test_central_oauth.py --desktop src/ClassNex/ClassNex.csproj --paired-desktop tools/ClassNex.PairingReview/ClassNex.PairingReview.csproj
node tools/test_control_web.mjs
py tools/test_deploy_central.py -v
```

`-Round` 使用尚不存在的目录名，不覆盖上一次截图。以上图形自动化只在非输入桌面中打开窗口；普通预览命令 `./tools/preview_classroom.ps1 -ExpectedVersion 26w48b` 则会在使用者的桌面上显示，应在方便时手动运行。它使用独立便携目录，允许用户主动访问正式账户与集控入口；自动化与 `-Migration` 样机仍隔离。桌面本轮只在预览分支交付，没有更新 main、推发布标签或替换已安装的客户端。

2026-11-27 集控单独上线。首次启动检查发现旧的会话清理逻辑在重启后同时打开同一个文件进行读和写，有过期会话时服务无法启动。修复后先关闭读流，再用同目录临时文件原子替换；本地验证有效会话保留、过期会话移除，新会话仍可创建和注销。部署前仅在服务器保护目录备份原有集控状态和程序，未清空会话、未导出班级资料。公网核对了页面和样式正文摘要、未登录响应、登录回调及 PKCE；没有代替用户执行生产登录或认领真实教室。校园网到站点的可达性、实物多屏 DPI 与触屏体验仍需现场确认。

### 线上和预览版怎么配合

线上配对版对应的源文件收录于 `7dd4ddb`；公开页面与已部署时保存的载荷一致，不是本分支后续的登录保护或增量同步版本。现在可在网页配对、选班、修改班级设置并让设备接收。新版教室端遇到旧服务时不会整包上传并覆盖老师在另一端的修改：自己改的那一项留在本机，关机、重开仍在；另一项仍能从班级接收。支持分项修订的集控和客户端一起使用后，设置、基础课表可分别双向自动保存；同一项发生冲突时，有草稿可核对，不强行取最后一次写入。临时调课和账户凭据均不是班级下发项。

### 课堂界面实测（2026-11-27）

- [宽窗主页](../artifacts/26w48b-classroom-review/settings-home.png)、[窄窗主页](../artifacts/26w48b-classroom-review/settings-home-narrow.png)、[关于](../artifacts/26w48b-classroom-review/settings-about.png)和[更新](../artifacts/26w48b-classroom-review/settings-update.png)：[整窗报告](../artifacts/26w48b-classroom-review/desktop-review.json)记录 598 项行为、几何或诊断条目、46 次捕获，包括拖动贴边保存和科目改名；UI 队列采样不是显示器帧率。调试完整重建为 0 警告、0 错误，全部 182 项单测通过（Core 112、通知 52、插件 6、集控 12）。
- [长课名提醒](../artifacts/26w48b-classroom-review/reminder-course.png)使用七年级信息科技课例。[定位专测](../artifacts/26w48b-classroom-review/notification-position.review.json)另有 67 项，覆盖课表居中、用户拖动、贴边和还原。[普通](../artifacts/26w48b-classroom-review/desktop-pointer.json)和[减少动画](../artifacts/26w48b-classroom-review/desktop-pointer-reduced.json)各 20 项，使用系统命中测试及浮窗后的原生按钮确认穿透。主机实际缩放为 125%；100% 与 150% 的渲染用例不能代替跨显示器实测。
- 档案同一组场景通过 [Headless 62 项](../artifacts/26w48b-profiles/headless/review.json)和[私有 Windows 桌面 74 项](../artifacts/26w48b-profiles/windows-offscreen/review.json)。后者取到 11 张真正窗口客户区截图；浅色与深色下可区分正在编辑的课程、课间、活动和分隔点。
- 原生设置因一次面包屑返回焦点失败，补了「目标卡片稍后才加载」的复现；修后多轮各通过 195 项。[最终逐项结果](../artifacts/26w48b-classroom-review/native-settings.txt)覆盖自定义快捷入口、分行排列、桌面就地编辑、按时段显示、草稿确认等。[组件设置](../artifacts/26w48b-classroom-review/native-widget-rows.png)与[桌面时钟属性](../artifacts/26w48b-classroom-review/desktop-clock-properties.png)取自同次运行；后者仅捕获弹出面板的内容，不是整张桌面。
