# ClassNEX 集控管理

## 范围

集控中心使用 ClassNEX ID 登录。管理端和桌面客户端登录同一个 ClassNEX 账户后，会看到同一份班级列表。
集控中心可以读取、编辑并发布完整设置与完整课程表，客户端每 30 秒检查一次修订号，也可以立即同步。

## 认证

集控中心使用 Casdoor OAuth 授权码登录，并在服务端建立 HttpOnly 会话。
桌面客户端使用当前 ClassNEX ID 的访问令牌调用集控 API，不再使用管理员令牌、注册令牌或客户端密钥。

集控服务需要配置以下环境变量：

```powershell
$env:CASDOOR_URL="http://206.187.201.60:8000"
$env:CASDOOR_INTERNAL_URL="http://casdoor:8000"
$env:CASDOOR_CLIENT_ID="<Casdoor 应用 Client ID>"
$env:CASDOOR_CLIENT_SECRET="<可选，公开客户端保持为空>"
$env:CENTRAL_PUBLIC_URL="http://<服务器>:7010"
```

Casdoor 应用的 Redirect URI 必须包含：

```text
http://<服务器>:7010/auth/callback
```

应用必须允许 `authorization_code` 授权类型。启用 PKCE 时由集控服务生成 State 与 S256 Code Challenge。
`stcn` 等上游提供商可能返回较大的 AccessToken，集控容器应加入 Casdoor 的 Docker 网络，
并通过 `CASDOOR_INTERNAL_URL` 在容器网络内读取用户资料，避免公共反向代理的请求头大小限制。

## 启动服务

```powershell
dotnet run --project src\ClassNex.Central\ClassNex.Central.csproj --urls http://0.0.0.0:7010
```

运行数据保存在程序目录的 `data\central-state.json`。

## 管理端

打开 `http://<服务器>:7010/`，点击“登录 ClassNEX ID”。
登录后可创建班级、选择班级，并编辑完整的 `Settings.json` 与 `timetable.yaml`。
保存后修订号自动增加，选择该班级的客户端会在下一次轮询时应用。

## 客户端接入

进入“应用设置 → 基本 → 集中管理”，完成以下操作：

1. 登录 ClassNEX ID
2. 填写集控服务地址
3. 点击“刷新班级”
4. 选择同一账户在集控中心创建的班级
5. 点击“连接集控”

客户端会保留本机的集控连接参数，远端设置不能覆盖这些参数。

## API

| 方法 | 路径 | 鉴权 | 用途 |
| --- | --- | --- | --- |
| GET | `/auth/login` | 无 | 跳转 ClassNEX ID 登录 |
| GET | `/auth/callback` | 无 | 建立集控管理端会话 |
| GET | `/api/v1/auth/me` | 会话或 Bearer | 当前 ClassNEX ID |
| POST | `/api/v1/client/device` | 会话或 Bearer | 注册或更新客户端 |
| GET | `/api/v1/client/classes` | 会话或 Bearer | 当前账户的班级列表 |
| GET | `/api/v1/client/classes/{id}/config` | 会话或 Bearer | 拉取班级设置与课程表 |
| POST | `/api/v1/client/classes/{id}/report` | 会话或 Bearer | 上报客户端状态与数据 |
| POST | `/api/v1/classes` | 会话或 Bearer | 创建班级 |
| GET | `/api/v1/classes/{id}` | 会话或 Bearer | 读取班级完整数据 |
| PUT | `/api/v1/classes/{id}` | 会话或 Bearer | 发布设置和课程表 |
| DELETE | `/api/v1/classes/{id}` | 会话或 Bearer | 删除班级 |

桌面客户端发送 `Authorization: Bearer <ClassNEX ID AccessToken>`。班级按 ClassNEX ID 的 `sub` 隔离，其他账户不能访问或抢占同一班级。
