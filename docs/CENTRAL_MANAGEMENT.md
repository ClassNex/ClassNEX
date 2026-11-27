# ClassNEX 集控管理

## 连接一台教室设备（26w48b 预览）

配对需要桌面和集控服务都支持新版协议。**只在管理网页登录 ClassNEX ID**；教室电脑不用登录账户、输入服务器地址、填写令牌或选择班级。

1. 在教室端打开「应用设置 → 集控与自动化」，点击「获取配对码」。只把屏幕上的 `XXXX-XXXX` 交给获准管理这台设备的人；配对码五分钟内有效，只能使用一次。
2. 在集控网页用自己的 ClassNEX ID 登录，打开「设备连接」，输入配对码。默认新建班级并沿用教室现有课表；也可以在配对前选一个已有班级，直接使用那个班的设置和课表。点击「配对设备」即可，之后仍可在网页更改分配。
3. 在网页的「应用设置」或「档案设置」选择该班级后直接编辑。设置和课表自动分别保存，成功时显示修订号；失败会保留当前页的修改，可点击「重试同步」。已连接客户端约每两秒检查新修订，本机保存后也会自动上传，不需要再按同步按钮。

新班级的空表单不会清掉教室原有课表。收到第一份有内容的课表后，主动清空并保存才会同步为空。更改部分设置不会重置客户端未指定的偏好。共享的是课堂设置和基础课表，不是整份用户目录：账户、设备凭据、NEX 密钥和系统提示词、本机路径及本地铃声文件不随班级同步。

网页「断开班级」保留设备绑定，可稍后分配其他班级。「解除配对」或客户端的「解除绑定」会停止这台设备的同步，不会删除班级或本机课表，本机仍可继续编辑。如果教室电脑离线，本机解除只保证本地已断开；管理员可在网页撤销服务端绑定。

## 认证

集控中心使用 Casdoor OAuth 授权码登录，并在服务端建立 HttpOnly 会话。
会话保存在持久化数据目录的 `central-sessions.json`，有效期 7 天；服务或容器重启后仍保持登录。
已确认配对的桌面使用单独的设备凭据，只能查询本机绑定和同步分配给本机的配置，不能管理账户、列出其他设备或班级。客户端的 `data/central-device.bin` 由当前 Windows 用户保护，服务端只保存凭据的哈希。不要把该文件复制给其他用户或作为配置分发。

未认领的配对码只存在于服务进程内：服务重启或码过期后请重新获取。保留数据目录重启服务时，已确认的绑定继续有效，已撤销的绑定不会恢复。浏览器会话独立于设备配对。旧客户端仍可使用原有账户 Bearer 登记、分班及同步；已通过账户连接的设备不会被静默改绑。要切换为配对模式，先在客户端断开旧连接。新客户端遇到尚未支持配对的服务会显示相应提示。

集控服务需要配置以下环境变量：

```powershell
$env:CASDOOR_URL="https://classnex.tech"
$env:CASDOOR_INTERNAL_URL="http://casdoor:8000"
$env:CASDOOR_CLIENT_ID="<Casdoor 应用 Client ID>"
$env:CASDOOR_CLIENT_SECRET="<可选，公开客户端保持为空>"
$env:CENTRAL_PUBLIC_URL="https://classnex.tech/Control"
```

Casdoor 应用的 Redirect URI 必须包含：

```text
http://<服务器>:7010/auth/callback
https://classnex.tech/Control/auth/callback
```

应用必须允许 `authorization_code` 授权类型。启用 PKCE 时由集控服务生成 State 与 S256 Code Challenge。
`stcn` 等上游提供商可能返回较大的 AccessToken，集控容器应加入 Casdoor 的 Docker 网络，
并通过 `CASDOOR_INTERNAL_URL` 在容器网络内读取用户资料，避免公共反向代理的请求头大小限制。

## 启动服务

```powershell
dotnet run --project src\ClassNex.Central\ClassNex.Central.csproj --urls http://0.0.0.0:7010
```

默认在程序目录的 `data/` 保存 `central-state.json`（班级和绑定）和 `central-sessions.json`（浏览器会话）；可用 `CENTRAL_DATA_DIRECTORY` 指定持久目录。容器更新时应保留同一数据卷。

## 管理端

管理端地址为 `https://classnex.tech/Control/`。2026-11-27 已仅更新新服务器的 `classnex-central`：公网首页和样式 SHA-256 与预发布产物一致，未登录访问账户、设备状态及班级接口被拒绝，Casdoor 的 S256 授权请求回调为 `https://classnex.tech/Control/auth/callback`。没有用生产用户代登、认领真实设备，或改动 Casdoor、DNS、代理。桌面预览是独立交付，并未发布新的 Windows 安装包。

公开访问使用标准 443，不要求学校网络放行非标准端口。登录回调、登录完成和退出都使用 `CENTRAL_PUBLIC_URL`；代理位于 `/Control/` 时，该值也必须包含 `/Control`。公网匿名检查可重复执行：`py tools/check_public_central.py <集控发布目录>`。
客户端默认集控地址为 `https://classnex.tech/Control`。旧版保存的 `7010` 直接 IP
地址会在启动时迁移到该地址，避免部分校园网或移动网络阻断美国源站 IP 与非标准端口。

Nginx 的域名根路径 `/` 必须继续代理 Casdoor，不能返回 `302 /Control/`。
STCN 等第三方提供商会在登录后回到站点根路径 `/callback`，再回到 `/` 继续生成 Casdoor 授权码；
如果根路径被集控网页占用，Casdoor 的 OAuth 流程会被截断，浏览器虽然显示登录成功，
集控端仍会收到 `401 Unauthorized`。集控网页只挂载在 `/Control/`。
Casdoor 的 OAuth 回调脚本必须优先返回已经声明的 `response_type`。Casdoor 默认逻辑会把
同域下的 `/Control/auth/callback` 误判为普通站内登录并跳回 `/`；补丁见
`deploy/casdoor/AuthCallbackHandler.patch`。

国内网络需要经 `deploy/classnex-relay/nginx.conf` 中的腾讯云反向代理访问，
入口证书与 `classnex.tech` 一致，代理再通过 TLS 连接美国业务源站。
登录后可创建班级、选择班级。“应用设置”和“档案设置”是两个独立页面，班级选择器位于各页顶部，
不再用右侧窄栏挤压编辑区。客户端设置使用自适应分组卡片、开关、下拉框、滑动条和文本框编辑，
不必直接编写 JSON；档案设置使用全宽“科目池 + 星期 × 节次”网格，支持每周、单周、双周、
直接修改节次时间、增删节次和点击格子排课，七天课表无需手动加宽窗口。
网页按窗口宽度使用侧栏或紧凑导航，并适配深浅色。重开已登录的网页会回到管理总览，而不是跳到账户管理页。

## API

| 方法 | 路径 | 鉴权 | 用途 |
| --- | --- | --- | --- |
| GET | `/auth/login` | 无 | 跳转 ClassNEX ID 登录 |
| GET | `/auth/callback` | 无 | 建立集控管理端会话 |
| GET | `/api/v1/auth/me` | 会话或 Bearer | 当前 ClassNEX ID |
| POST | `/api/v1/pairing/start` | 无，限流 | 教室请求一次性配对码及私有设备凭据 |
| POST | `/api/v1/pairing/claim` | 浏览器会话，限流 | 用码绑定至自己的班级；省略 `classId` 时新建班级 |
| GET | `/api/v1/pairing/status?clientId=…` | 本机设备凭据 | 查询待认领或已绑定状态 |
| DELETE | `/api/v1/pairing/device?clientId=…` | 本机设备凭据 | 取消待认领配对或撤销本机绑定 |
| DELETE | `/api/v1/devices/{id}` | 所有者浏览器会话 | 从网页解除设备绑定，保留班级 |
| POST | `/api/v1/client/device` | 会话或账户 Bearer | 注册或更新旧版客户端 |
| GET | `/api/v1/client/assignment` | 设备凭据或旧版账户鉴权 | 拉取管理端为本设备分配的班级配置 |
| POST | `/api/v1/client/report` | 设备凭据或旧版账户鉴权 | 上报客户端状态与课堂数据 |
| POST | `/api/v1/client/publish` | 设备凭据或旧版账户鉴权 | 客户端保存后发布新的设置和课程表修订 |
| GET | `/api/v1/classes` | 会话或 Bearer | 当前账户的班级列表 |
| POST | `/api/v1/classes` | 会话或 Bearer | 创建班级 |
| GET | `/api/v1/classes/{id}` | 会话或 Bearer | 读取班级完整数据 |
| PUT | `/api/v1/classes/{id}` | 会话或 Bearer | 发布设置和课程表 |
| PUT | `/api/v1/classes/{id}/settings` | 会话或 Bearer | 保存可视化客户端设置 |
| GET | `/api/v1/classes/{id}/timetable` | 会话或 Bearer | 读取结构化课程表 |
| PUT | `/api/v1/classes/{id}/timetable` | 会话或 Bearer | 保存可视化课程表并转换为 CSES |
| DELETE | `/api/v1/classes/{id}` | 会话或 Bearer | 删除班级 |
| GET | `/api/v1/devices` | 会话或 Bearer | 当前账户已登记设备 |
| PUT | `/api/v1/devices/{id}` | 会话或 Bearer | 选择设备并指定或断开班级 |

配对设备发送 `Authorization: ClassNexDevice <设备凭据>`，不是页面上显示的八位配对码。该设备的同步接口不接受账户 Bearer 代替设备凭据；旧设备标识继续沿用账户鉴权。班级和设备按账户主体隔离。

## 本地验收

从仓库根目录运行。OAuth 测试只在回环地址启动临时身份提供商和集控，使用临时数据目录。`--paired-desktop` 运行真正的 Windows 桌面同步服务，但不打开教室窗口；它使用前面生成的 Release 程序。

```powershell
dotnet test tests/ClassNex.Central.Tests
dotnet test tests/ClassNex.Core.Tests --filter FullyQualifiedName~ManagementPayloadTests
dotnet build src/ClassNex.Central -c Debug
dotnet build tools/ClassNex.PairingReview -c Release
py -3 tools/test_central_oauth.py --paired-desktop tools/ClassNex.PairingReview/ClassNex.PairingReview.csproj
```

验证包括：原账户登录与旧设备同步、不同所有者的数据隔离、一次性配对、设置与课表并发保存、首次空表保护、设备凭据的 Windows 用户保护、服务重启以及从任一端解除绑定后仍能本地编辑。浏览器控件另可用 `node tools/test_control_web.mjs` 在本机临时 Edge 配置中检查；该脚本使用模拟接口，不代替上述真实集控协议验收。
