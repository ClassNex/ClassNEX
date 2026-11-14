# ClassNEX 集控管理

## 范围

集控中心可以读取客户端完整设置、完整课表，并向指定客户端发布新版本。
客户端每 30 秒检查一次修订号，也可以立即同步。

## 启动服务

```powershell
dotnet run --project src\ClassNex.Central\ClassNex.Central.csproj --urls http://0.0.0.0:7010
```

生产环境应放在 HTTPS 反向代理后，并通过环境变量固定令牌：

```powershell
$env:CLASSNEX_CENTRAL_ADMIN_TOKEN="<管理员令牌>"
$env:CLASSNEX_CENTRAL_ENROLL_TOKEN="<客户端注册令牌>"
```

未设置时服务首次启动会生成令牌并显示在控制台。
运行数据保存在程序目录的 `data\central-state.json`。

## 管理端

打开 `http://<服务器>:7010/`，输入管理员令牌。
选择客户端即可查看并编辑完整的 `Settings.json` 与 `timetable.yaml`。
保存后修订号自动增加，客户端下一次轮询时应用。

## 客户端接入

进入“应用设置 → 基本 → 集中管理”，填写：

1. 集控服务地址
2. 客户端注册令牌
3. 点击“注册并连接”

注册成功后，集控中心会显示机器名、版本、最后在线时间、完整设置和课程表。
客户端会保留本机的集控连接参数，远端设置不能覆盖这些参数。

## API

| 方法 | 路径 | 鉴权 | 用途 |
| --- | --- | --- | --- |
| POST | `/api/v1/client/register` | 注册令牌 | 注册或重新签发客户端令牌 |
| GET | `/api/v1/client/config` | 客户端令牌 | 拉取设置与课程表 |
| POST | `/api/v1/client/report` | 客户端令牌 | 上报状态、设置和课程表 |
| GET | `/api/v1/admin/clients` | 管理员令牌 | 客户端列表 |
| GET | `/api/v1/admin/clients/{id}` | 管理员令牌 | 完整客户端数据 |
| PUT | `/api/v1/admin/clients/{id}` | 管理员令牌 | 发布设置和课程表 |
| DELETE | `/api/v1/admin/clients/{id}` | 管理员令牌 | 移除客户端 |

API 使用 `Authorization: Bearer <token>`。
管理员令牌和注册令牌不能写入公开仓库，生产环境必须使用 HTTPS。
