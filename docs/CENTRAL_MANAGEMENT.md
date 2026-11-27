# ClassNEX 集控管理

## 连接一台教室设备（26w48b 预览）

配对需要桌面和集控服务都支持配对协议。**只在管理网页登录 ClassNEX ID**；教室电脑不用登录账户、输入服务器地址、填写令牌或选择班级。下文标为预览分支的功能尚未部署到现网。

1. 在教室端打开「应用设置 → 集控与自动化」，点击「获取配对码」。只把屏幕上的 `XXXX-XXXX` 交给获准管理这台设备的人；配对码五分钟内有效，只能使用一次。
2. 在集控网页用自己的 ClassNEX ID 登录，打开「设备连接」，输入配对码。默认新建班级并沿用教室现有课表；也可以在配对前选一个已有班级，直接使用那个班的设置和课表。点击「配对设备」即可，之后仍可在网页更改分配。
3. 在网页的「应用设置」或「档案设置」选择该班级后直接编辑。新版集控会将设置和课表分别保存，成功时显示修订号；失败时保留当前页的修改，可重试。已连接客户端约每两秒检查新修订。教室端保存后能否立即上传取决于服务版本，见下表，不需要再按同步按钮。

预览网页刷新班级列表后若发现所选班级已被删除，会保留配对码并停用提交按钮。请重新选班，或明确选择「新建班级，沿用教室课表」；网页不会擅自新建班级。见[失效班级提示](../artifacts/26w48b-classroom-review/pair-class-unavailable.png)。

新班级的空表单不会清掉教室原有课表。收到第一份有内容的课表后，主动清空并保存才会同步为空。更改部分设置不会重置客户端未指定的偏好。共享的是课堂设置和基础课表，不是整份用户目录：账户、设备凭据、NEX 密钥和系统提示词、本机路径及本地铃声文件不随班级同步。

网页「断开班级」保留设备绑定，可稍后分配其他班级。「解除配对」或客户端的「解除绑定」会停止这台设备的同步，不会删除班级或本机课表，本机仍可继续编辑。预览客户端会先归档待传修改、清掉旧设备凭据，再通知界面已断开；随后立即重新配对也不会被旧回调清除。见[本地解绑与账户验收](../artifacts/26w48b-classroom-review/central-revocation-account.json)。如果教室电脑离线，本机解除只保证本地已断开；管理员可在网页撤销服务端绑定。

### 同时修改与旧服务器（预览分支，尚未部署）

| 情况 | 结果 |
| --- | --- |
| 两边同时改不同内容 | 设置和基础课表各自校验读取时的修订。网页改课表、教室改外观，两项都可生效。 |
| 两边都改设置，或都改课表 | 按资源检查，不按单个字段自动合并。后提交的版本不会静默覆盖对方。网页可选「采用服务器版本」或「保留本页版本」；保留本页会再次校验修订。 |
| 客户端本地发生冲突 | 先保留班级版本，本机修改另存 `data/CentralDrafts/`。在设置「集控与自动化」可查看草稿；仍绑定原班级时，可明确确认恢复最近一份。恢复也按新修订提交，原草稿不删除。 |
| 新客户端连接已上线的旧集控 | 配对、接收和本地编辑仍可用。旧服务不能保护分项写入，所以**本地修改暂存，不会向旧服务自动上传**。本地设置待传时仍接收网页课表，反之亦然；重启保留待传项，服务升级后再补传。旧客户端的行为不因此改变。 |

桌面的待传记录和归档仅保存班级可同步的内容，关联服务地址、设备和班级。不同班级的草稿不能直接恢复到当前绑定；不要把草稿目录当成跨设备配置包。网页草稿只存在于当前标签页内：可以切换班级、在断网后重试，但强制刷新或关闭标签后不能找回。网页不提供 NEX 密钥、接口地址、模型、工具权限及系统提示词输入，也不会把旧设置里的这些值重新发送。日常调课仍在档案里操作，无须手写 JSON。

新桌面配对到旧服务上的空班级时，可用教室原有数据初始化一次。初始化不同于日常保存：有本机修改待传时，常规心跳只报在线状态，不带设置或课表。重启后本机恢复待传项，既不重复初始化，也不向旧服务整份覆盖。

使用归档的旧服务 DLL、旧网页、无头浏览器和新桌面进程在本机回环验收。[双向及重启](../artifacts/26w48b-classroom-review/central-legacy-compatibility.json)覆盖「待传字号 + 网页改课程」和「待传课表 + 网页改主题」；[首次配对确认一](../artifacts/26w48b-classroom-review/central-legacy-first-ack-1.json)与[确认二](../artifacts/26w48b-classroom-review/central-legacy-first-ack-2.json)都在教室端刚显示已同步时修改字号。四组场景中，4 个空班级各初始化 1 次，重启没有再次初始化，8 个待传期心跳均不带设置或课表。[新协议的配对验收](../artifacts/26w48b-classroom-review/central-current-paired.json)另覆盖分项上传、冲突、恢复和撤销。

### 网页会话过期（预览分支）

会话过期时，原标签页会隐藏编辑内容、暂停同步，尚未保存的修改仍留在原页。点「在新标签页登录」，用原账号登录后回到原页；也可点「检查登录状态」。只有身份服务、账号主体和组织都与原会话一致，才会恢复编辑和重试同步。

升级前建立的会话可能未记录组织。如果重新登录后身份服务返回了组织，原标签页无法确认是同一身份，也不会自动恢复或上传那份草稿。

换账号或组织后，原草稿不会显示或上传。若确实要切换，可点「放弃草稿并重新打开」并确认。草稿只保存在原标签页的内存里，关闭或刷新原页会丢失；续登链接不携带草稿或凭据。见[原标签页续登提示](../artifacts/26w48b-classroom-review/session-expired-draft.png)和[账号不一致提示](../artifacts/26w48b-classroom-review/session-changed-account.png)。

## 认证

集控中心使用 Casdoor OAuth 授权码登录，并在服务端建立 HttpOnly 会话。
会话保存在持久化数据目录的 `central-sessions.json`，有效期 7 天；服务或容器重启后仍保持登录。
已确认配对的桌面使用单独的设备凭据，只能查询本机绑定和同步分配给本机的配置，不能管理账户、列出其他设备或班级。客户端的 `data/central-device.bin` 由当前 Windows 用户保护，服务端只保存凭据的哈希。不要把该文件复制给其他用户或作为配置分发。

在本预览分支，管理端将每次登录与发起它的浏览器绑定：短期 HttpOnly、SameSite=Lax 的 Cookie 仅发送到公开回调路径；缺少证明、证明错误、过期或已用过的回调都不能建立会话。用户信息接口只通过 `Authorization` 请求头接收令牌，不把它加到 URL，也不自动跟随身份服务的重定向。在 Windows 客户端，`data/account.json` 仍保存姓名和邮箱，但访问令牌由当前 Windows 用户的 DPAPI 保护；读取有效旧登录时自动迁移。换了 Windows 用户或电脑，应重新授权，不要拷贝账户文件。这些是分支的改动，不代表线上集控已升级。

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

管理端地址为 `https://classnex.tech/Control/`。2026-11-27 的上线记录对应旧配对版（源码 `7dd4ddb`）：公网首页和样式 SHA-256 与当时的预发布产物一致，未登录访问账户、设备状态及班级接口被拒绝，Casdoor 的 S256 授权请求回调为 `https://classnex.tech/Control/auth/callback`。网页续登、账号校验、冲突处理和分项同步仍是预览分支功能。本轮没有用正式 Casdoor 账户联测、认领真实设备，也没有部署预览分支。桌面预览是独立交付，并未发布新的 Windows 安装包。

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
| GET | `/api/v1/auth/me` | 会话或 Bearer | 当前 ClassNEX ID；预览分支返回 `accountKey` |
| POST | `/api/v1/pairing/start` | 无，限流 | 教室请求一次性配对码及私有设备凭据 |
| POST | `/api/v1/pairing/claim` | 浏览器会话，限流 | 用码绑定至自己的班级；省略 `classId` 时新建班级 |
| GET | `/api/v1/pairing/status?clientId=…` | 本机设备凭据 | 查询待认领或已绑定状态 |
| DELETE | `/api/v1/pairing/device?clientId=…` | 本机设备凭据 | 取消待认领配对或撤销本机绑定 |
| DELETE | `/api/v1/devices/{id}` | 所有者浏览器会话 | 从网页解除设备绑定，保留班级 |
| POST | `/api/v1/client/device` | 会话或账户 Bearer | 注册或更新旧版客户端 |
| GET | `/api/v1/client/assignment` | 设备凭据或旧版账户鉴权 | 拉取管理端为本设备分配的班级配置 |
| POST | `/api/v1/client/report` | 设备凭据或旧版账户鉴权 | 上报客户端状态与课堂数据 |
| POST | `/api/v1/client/publish` | 设备凭据或旧版账户鉴权 | 为旧客户端保留的整份设置和课表发布接口 |
| POST | `/api/v1/client/changes` | 设备凭据或旧版账户鉴权 | 预览分支：使用已读修订，只提交指定班级的一项设置或课表资源；同一资源已变更时返回 409 |
| GET | `/api/v1/classes` | 会话或 Bearer | 当前账户的班级列表 |
| POST | `/api/v1/classes` | 会话或 Bearer | 创建班级 |
| GET | `/api/v1/classes/{id}` | 会话或 Bearer | 读取班级完整数据 |
| PUT | `/api/v1/classes/{id}` | 会话或 Bearer | 发布设置和课程表 |
| PUT | `/api/v1/classes/{id}/settings` | 会话或 Bearer | 保存可视化客户端设置；预览分支支持 `If-Match` 修订检查 |
| GET | `/api/v1/classes/{id}/timetable` | 会话或 Bearer | 读取结构化课程表 |
| PUT | `/api/v1/classes/{id}/timetable` | 会话或 Bearer | 保存可视化课程表并转换为 CSES；预览分支支持 `If-Match` 修订检查 |
| DELETE | `/api/v1/classes/{id}` | 会话或 Bearer | 删除班级 |
| GET | `/api/v1/devices` | 会话或 Bearer | 当前账户已登记设备 |
| PUT | `/api/v1/devices/{id}` | 会话或 Bearer | 选择设备并指定或断开班级 |

配对设备发送 `Authorization: ClassNexDevice <设备凭据>`，不是页面上显示的八位配对码。该设备的同步接口不接受账户 Bearer 代替设备凭据；旧设备标识继续沿用账户鉴权。班级和设备按账户主体隔离。

预览网页把 `/api/v1/auth/me` 返回的 `accountKey` 放入 `X-ClassNex-Account` 请求头，配对、读取班级和保存都绑定到打开页面时的账号。服务端以该请求实际使用的登录会话校验；不一致返回 `401`，不消费配对码、不创建班级。不带该头的旧版请求继续走原有归属授权。新版网页遇到不支持此校验的旧服务会提示升级。**网页和 API 必须同包交付，不要只覆盖静态网页。**

## 本地验收

从仓库根目录运行。OAuth 测试只在回环地址启动临时身份提供商和集控，使用临时数据目录。`--paired-desktop` 运行真正的 Windows 桌面同步服务，但不打开教室窗口；它使用前面生成的 Release 程序。

```powershell
dotnet test tests/ClassNex.Central.Tests
dotnet test tests/ClassNex.Core.Tests --filter FullyQualifiedName~ManagementPayloadTests
dotnet build src/ClassNex.Central -c Debug
dotnet build tools/ClassNex.PairingReview -c Release
py -3 tools/run_review_offscreen.py --timeout 240 -- py -3 tools/test_central_oauth.py --desktop src/ClassNex/ClassNex.csproj --paired-desktop tools/ClassNex.PairingReview/ClassNex.PairingReview.csproj
```

本轮 Central 的 12 项单测通过。本机 HTTP 验收覆盖原账户登录、不同所有者的数据隔离、一次性配对、设置与课表并发保存、首次空表保护、设备凭据的 Windows 用户保护、服务重启及解除绑定；也验证了错账号的配对请求被拒后，原配对码仍能由正确账号使用。`node tools/test_control_web.mjs` 另用临时 Edge 配置验收 91 项无障碍名称、19 次条件写入，以及续登、跨账号／组织隔离和失效班级；该脚本使用模拟接口，不代替本机真实集控协议验收。

复验旧服务需要保存的配对版发布目录（含 DLL、PDB 和 `wwwroot`，不随源码分发），不向线上服务发请求。第二条命令专测初次配对后、首个确认边界上的本机编辑：

```powershell
py -3 tools/test_central_legacy_binary.py --old-dir .\build\central-service-26w48b-classroom
py -3 tools/test_central_legacy_binary.py --old-dir .\build\central-service-26w48b-classroom --boundary
```
