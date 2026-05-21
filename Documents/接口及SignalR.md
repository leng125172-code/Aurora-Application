# AuroraStruct3D 接口及 SignalR 文档

## 1. 文档说明

- 本文档以以下代码为准整理：
    - 前端接口封装：`Sources\AuroraStruct3D\AuroraStruct3D.Frontend\src\api\*.ts`
    - 前端实时通信：`Sources\AuroraStruct3D\AuroraStruct3D.Frontend\src\stores\deviceState.ts`
    - 后端设备状态应用服务：`Sources\AuroraStruct3D\AuroraStruct3D.Application\DeviceState\DeviceStateAppService.cs`
    - 后端 SignalR Hub：`Sources\AuroraStruct3D\AuroraStruct3D.HttpApi.Host\Hubs\DeviceStateHub.cs`
- `Documents\swagger.json` 已过期，**不作为本文档依据**。
- 本文档覆盖当前前端已接入或已明确声明的 REST 接口与设备状态 SignalR 推送。

## 2. 通用约定

### 2.1 基础地址

- 前端 `axios` 基础地址为 `/`
- 开发环境下由 Vite 代理转发到 `VITE_ABP_BACKEND_URL`，默认值为 `http://localhost:44315`
- 因此前端调用路径均可按“同源相对路径”理解

### 2.2 通用请求头

| 请求头                          | 说明                                     |
| ------------------------------- | ---------------------------------------- |
| `Authorization: Bearer {token}` | 登录后自动注入                           |
| `__tenant: {tenantId}`          | 选择租户后自动注入；登录接口也可单独传入 |
| `Accept-Language: zh-CN`        | 未显式指定时，默认取本地缓存或 `zh-CN`   |

### 2.3 统一错误结构

后端接口失败时通常返回 ABP 标准错误结构：

```json
{
    "error": {
        "code": "string",
        "message": "string",
        "details": "string",
        "validationErrors": [
            {
                "message": "string",
                "members": ["string"]
            }
        ]
    }
}
```

前端在收到 `401` 时会清空登录态并跳转登录页。

## 3. REST 接口

### 3.1 ABP 应用配置

#### GET `/api/abp/application-configuration`

- **鉴权**：可匿名访问；如已登录则会返回当前用户与权限信息
- **说明**：获取当前用户、当前租户、授权策略、本地化配置

返回关键字段：

```json
{
    "currentUser": {
        "isAuthenticated": true,
        "id": "string",
        "tenantId": "string",
        "userName": "string",
        "email": "string",
        "roles": ["admin"]
    },
    "currentTenant": {
        "id": "string",
        "name": "string",
        "isAvailable": true
    },
    "auth": {
        "grantedPolicies": {
            "Policy.Name": true
        }
    },
    "localization": {
        "currentCulture": {
            "cultureName": "zh-CN",
            "name": "Chinese"
        },
        "languages": []
    }
}
```

### 3.2 认证接口

前端认证封装以 `Sources\AuroraStruct3D\AuroraStruct3D.Frontend\src\api\auth.ts` 为准：

- 后端实际对接的是 `Lion.AbpPro.BasicManagement` 中的 `IAccountAppService`
- 当前前端走的是**自定义 JWT 登录**，**不是** `/connect/token`
- 当前 `auth.ts` 中与认证相关的方法说明如下：

| 前端方法      | HTTP 方法 | 路径                     | 说明                               |
| ------------- | --------- | ------------------------ | ---------------------------------- |
| `loginAsync`  | `POST`    | `/api/app/account/login` | 用户名密码登录                     |
| `logoutAsync` | 无        | 无                       | 仅清理本地登录态，不发起 HTTP 请求 |

> 文件注释中还提到了 `POST /api/app/account/refresh-token`，但当前 `auth.ts` 未封装对应调用方法。

#### POST `/api/app/account/login`

- **鉴权**：匿名
- **说明**：用户名密码登录
- **额外请求头**：可选 `__tenant`

请求体：

```json
{
    "name": "admin",
    "password": "123qwe"
}
```

> `LoginPayload` 中的字段为 `username`、`password`、`tenantId`，提交到后端时会映射为 `name`、`password`，并在有租户时追加 `__tenant` 请求头。

返回体：

```json
{
    "id": "string",
    "name": "string",
    "userName": "string",
    "token": "jwt-token",
    "refreshToken": "refresh-token",
    "roles": ["admin"]
}
```

#### 登出说明

- 当前前端 `logoutAsync()` 仅返回 `Promise.resolve()`，用于配合本地清理登录态
- `auth.ts` 明确标注：当前后端**未提供登出端点**
- 如后续需要让 `refreshToken` 失效，需要再扩展服务端接口与前端封装

#### 刷新 Token 说明

- `auth.ts` 的文件注释中提到了 `POST /api/app/account/refresh-token`
- 但当前文件**没有**对应的 `refreshTokenAsync` 或类似调用封装
- 若后续补齐前端封装，其 HTTP 方法应为 `POST`
- 因此本文档仅记录该路由在线程注释中被提及，**不再把它视为当前前端已接入接口**

### 3.3 设备状态接口

设备状态接口来自 `IDeviceStateAppService`，路由前缀为 `/api/app/device-state`。

#### 设备状态 DTO

```json
{
    "status": 1,
    "runMode": 0,
    "currentFaultId": "string",
    "currentFaultLevel": 1,
    "currentFaultCode": "E1001",
    "isInTransition": false,
    "canAcceptProductionCommand": true,
    "canSwitchMode": true
}
```

字段说明：

| 字段                         | 说明                            |
| ---------------------------- | ------------------------------- |
| `status`                     | 设备状态枚举                    |
| `runMode`                    | 运行模式枚举                    |
| `currentFaultId`             | 当前故障 Id，无故障时为 `null`  |
| `currentFaultLevel`          | 当前故障等级，无故障时为 `null` |
| `currentFaultCode`           | 当前故障编码，无故障时为 `null` |
| `isInTransition`             | 是否处于过渡状态                |
| `canAcceptProductionCommand` | 是否允许接受生产命令            |
| `canSwitchMode`              | 是否允许切换运行模式            |

#### 故障 DTO

```json
{
    "id": "string",
    "occurredAt": "2026-05-19T08:00:00+08:00",
    "faultLevel": 2,
    "faultCode": "E1001",
    "faultMessage": "string",
    "faultReason": "string",
    "isResolved": false,
    "isAutoRecovered": false,
    "resolverId": "string",
    "resolverName": "string",
    "resolvedAt": "2026-05-19T08:10:00+08:00",
    "resolutionDescription": "string",
    "durationMs": 1000,
    "causedModeSwitch": true,
    "switchedToMode": 3,
    "stateLogId": "string",
    "remark": "string",
    "creationTime": "2026-05-19T08:00:00+08:00"
}
```

#### 状态日志 DTO

```json
{
    "id": "string",
    "occurredAt": "2026-05-19T08:00:00+08:00",
    "previousStatus": 1,
    "newStatus": 3,
    "isStatusChange": true,
    "isTransitionState": false,
    "previousMode": 0,
    "newMode": 1,
    "isModeChange": true,
    "trigger": 0,
    "faultId": "string",
    "operatorId": "string",
    "operatorName": "string",
    "reason": "string",
    "remark": "string",
    "durationMs": 1000,
    "isSuccessful": true,
    "errorMessage": "string",
    "creationTime": "2026-05-19T08:00:00+08:00"
}
```

#### GET `/api/app/device-state/current-state`

- **鉴权**：匿名
- **说明**：获取当前设备状态快照
- **返回**：`DeviceStateDto`

#### GET `/api/app/device-state/current-fault`

- **鉴权**：匿名
- **说明**：获取当前活跃故障
- **返回**：`DeviceFaultDto | null`

#### POST `/api/app/device-state/switch-mode`

- **鉴权**：需要登录
- **说明**：切换运行模式
- **后端限制**：接口注释明确仅在 `Standby` 或 `Stopped` 时允许切换；非法切换会返回用户友好错误

请求体：

```json
{
    "newMode": 1,
    "reason": "切换到调试模式"
}
```

#### GET `/api/app/device-state/fault-paged-list`

- **鉴权**：需要登录
- **说明**：分页查询历史故障

查询参数：

| 参数             | 类型      | 说明         |
| ---------------- | --------- | ------------ |
| `skipCount`      | `number`  | 跳过条数     |
| `maxResultCount` | `number`  | 每页条数     |
| `sorting`        | `string`  | 排序字段     |
| `faultLevel`     | `number`  | 故障等级枚举 |
| `isResolved`     | `boolean` | 是否已解决   |
| `startTime`      | `string`  | 开始时间     |
| `endTime`        | `string`  | 结束时间     |

返回体：

```json
{
    "totalCount": 100,
    "items": []
}
```

#### GET `/api/app/device-state/state-log-paged-list`

- **鉴权**：需要登录
- **说明**：分页查询状态日志

查询参数：

| 参数             | 类型      | 说明                                                          |
| ---------------- | --------- | ------------------------------------------------------------- |
| `skipCount`      | `number`  | 跳过条数                                                      |
| `maxResultCount` | `number`  | 每页条数                                                      |
| `sorting`        | `string`  | 排序字段                                                      |
| `trigger`        | `number`  | 状态变更触发来源                                              |
| `status`         | `number`  | 前端已定义该参数；当前后端实现主要按 `trigger` 与时间范围过滤 |
| `startTime`      | `string`  | 开始时间                                                      |
| `endTime`        | `string`  | 结束时间                                                      |
| `isSuccessful`   | `boolean` | 前端已定义该参数；当前后端实现未看到对应过滤逻辑              |

返回体：

```json
{
    "totalCount": 100,
    "items": []
}
```

#### 关键枚举

设备状态 `DeviceStatus`：

| 值   | 名称                 | 中文       |
| ---- | -------------------- | ---------- |
| `0`  | `Initializing`       | 初始化中   |
| `1`  | `Standby`            | 待机       |
| `2`  | `Starting`           | 启动中     |
| `3`  | `Running`            | 运行中     |
| `4`  | `Paused`             | 已暂停     |
| `5`  | `Stopping`           | 停止中     |
| `6`  | `Stopped`            | 已停止     |
| `7`  | `Resetting`          | 复位中     |
| `8`  | `FaultAcknowledging` | 故障确认中 |
| `9`  | `Fault`              | 故障       |
| `10` | `EmergencyStop`      | 急停       |
| `11` | `Maintenance`        | 维护模式   |

运行模式 `DeviceRunMode`：

| 值  | 名称          | 中文 |
| --- | ------------- | ---- |
| `0` | `Production`  | 生产 |
| `1` | `Debug`       | 调试 |
| `2` | `Calibration` | 标定 |
| `3` | `Maintenance` | 维护 |

故障等级 `DeviceFaultLevel`：

| 值  | 名称          | 中文     |
| --- | ------------- | -------- |
| `0` | `Warning`     | 警告     |
| `1` | `Error`       | 错误     |
| `2` | `Critical`    | 严重     |
| `3` | `SafetyFault` | 安全故障 |

状态触发来源 `StateChangeTrigger`：

| 值  | 名称              |
| --- | ----------------- |
| `0` | `UserManual`      |
| `1` | `SystemAuto`      |
| `2` | `DeviceFault`     |
| `3` | `EmergencyButton` |
| `4` | `RemoteCommand`   |
| `5` | `SystemInit`      |

### 3.4 系统管理接口

系统管理接口当前由前端 `src\api\management.ts` 封装，使用同源根路径调用，**没有 `/api` 前缀**。

#### 通用分页请求体

```json
{
    "pageIndex": 1,
    "pageSize": 20,
    "filter": "string",
    "sorting": "CreationTime desc"
}
```

#### 用户管理

| 方法   | 路径                   | 说明             |
| ------ | ---------------------- | ---------------- |
| `POST` | `/Users/page`          | 分页获取用户列表 |
| `POST` | `/Users/create`        | 创建用户         |
| `POST` | `/Users/update`        | 更新用户         |
| `POST` | `/Users/delete`        | 删除用户         |
| `POST` | `/Users/resetPassword` | 重置密码         |
| `POST` | `/Users/lock`          | 锁定用户         |
| `POST` | `/Users/role`          | 获取用户角色     |

用户关键字段：

```json
{
    "id": "string",
    "userName": "string",
    "name": "string",
    "surname": "string",
    "email": "string",
    "phoneNumber": "string",
    "isActive": true,
    "twoFactorEnabled": false,
    "creationTime": "2026-05-19T08:00:00+08:00",
    "lockoutEnabled": false,
    "lockoutEnd": null
}
```

创建用户请求体：

```json
{
    "userName": "admin",
    "name": "系统",
    "surname": "管理员",
    "email": "admin@example.com",
    "phoneNumber": "13800000000",
    "isActive": true,
    "lockoutEnabled": false,
    "roleNames": ["admin"],
    "password": "123qwe"
}
```

更新用户请求体：

```json
{
    "id": "string",
    "userName": "admin",
    "name": "系统",
    "surname": "管理员",
    "email": "admin@example.com",
    "phoneNumber": "13800000000",
    "isActive": true,
    "lockoutEnabled": false,
    "roleNames": ["admin"],
    "concurrencyStamp": "string"
}
```

重置密码请求体：

```json
{
    "id": "string",
    "password": "new-password"
}
```

锁定用户请求体：

```json
{
    "id": "string",
    "seconds": 600
}
```

获取用户角色请求体：

```json
{
    "id": "string"
}
```

#### 角色管理

| 方法   | 路径            | 说明             |
| ------ | --------------- | ---------------- |
| `POST` | `/Roles/page`   | 分页获取角色列表 |
| `POST` | `/Roles/all`    | 获取全部角色     |
| `POST` | `/Roles/create` | 创建角色         |
| `POST` | `/Roles/update` | 更新角色         |
| `POST` | `/Roles/delete` | 删除角色         |

角色关键字段：

```json
{
    "id": "string",
    "name": "admin",
    "isDefault": false,
    "isStatic": false,
    "isPublic": true,
    "creationTime": "2026-05-19T08:00:00+08:00"
}
```

创建角色请求体：

```json
{
    "name": "operator",
    "isDefault": false,
    "isPublic": true
}
```

更新角色请求体：

```json
{
    "id": "string",
    "name": "operator",
    "isDefault": false,
    "isPublic": true,
    "concurrencyStamp": "string"
}
```

#### 租户管理

| 方法   | 路径              | 说明             |
| ------ | ----------------- | ---------------- |
| `POST` | `/Tenants/page`   | 分页获取租户列表 |
| `POST` | `/Tenants/create` | 创建租户         |
| `POST` | `/Tenants/update` | 更新租户         |
| `POST` | `/Tenants/delete` | 删除租户         |

租户关键字段：

```json
{
    "id": "string",
    "name": "tenant-a",
    "concurrencyStamp": "string"
}
```

创建租户请求体：

```json
{
    "name": "tenant-a",
    "adminEmailAddress": "admin@example.com",
    "adminPassword": "123qwe"
}
```

更新租户请求体：

```json
{
    "id": "string",
    "name": "tenant-a",
    "concurrencyStamp": "string"
}
```

### 3.5 结构光投影机接口

结构光投影机接口由前端 `src/api/projectors.ts` 封装，路由前缀为 `/api/projectors`，对应后端 `IProjectorDeviceAppService`。

> **VID/PID 说明**：USB HID 连接时，厂商 ID（VID）固定为 `0x0483`，产品 ID（PID）固定为 `0x5750`（腾聚 TJ 系列 STM32 HID 芯片，硬件固定，无法修改），不作为配置项存储。

#### 3.5.1 设备 CRUD

| 前端方法             | HTTP 方法 | 路径                   | 说明                        |
| -------------------- | --------- | ---------------------- | --------------------------- |
| `getProjectorList`   | `GET`     | `/api/projectors`      | 分页查询投影机列表          |
| `getProjector`       | `GET`     | `/api/projectors/{id}` | 获取单台投影机详情          |
| `createTcpProjector` | `POST`    | `/api/projectors/tcp`  | 创建 TCP 连接方式投影机     |
| `createHidProjector` | `POST`    | `/api/projectors/hid`  | 创建 USB HID 连接方式投影机 |
| `updateProjector`    | `PUT`     | `/api/projectors/{id}` | 更新投影机基本信息          |
| `deleteProjector`    | `DELETE`  | `/api/projectors/{id}` | 删除投影机                  |

##### 投影机设备 DTO（`ProjectorDeviceDto`）

```json
{
    "id": "string",
    "creationTime": "2026-05-20T08:00:00+08:00",
    "creatorId": "string",
    "lastModificationTime": null,
    "lastModifierId": null,
    "isDeleted": false,
    "deletionTime": null,
    "deleterId": null,

    "name": "主投影机",
    "deviceIndex": 0,
    "description": null,
    "isEnabled": true,

    "connectionType": 0,
    "ipAddress": "192.168.1.100",
    "tcpPort": 1234,
    "hidDeviceIndex": 0,
    "connectTimeoutMs": 5000,

    "firmwareVersion": "V1.0.0",
    "deviceHardwareId": 1,

    "connectionStatus": 2,
    "connectionStatusText": "已连接",
    "ledStatus": 2,
    "ledStatusText": "亮灯",
    "lastLightValue": 100,
    "lastDisplayMode": 0,
    "lastColor": 3,
    "checkerboardPixelSize": 30,
    "flipMode": 0,
    "triggerMode": 0,
    "bootImage": 0,
    "ledRgbR": 0,
    "ledRgbG": 0,
    "ledRgbB": 0,
    "lastCommunicationAt": "2026-05-20T08:00:00+08:00",
    "lastConnectedAt": "2026-05-20T07:55:00+08:00",
    "lastDisconnectedAt": null
}
```

##### GET `/api/projectors` — 分页查询投影机列表

查询参数：

| 参数             | 类型      | 说明           |
| ---------------- | --------- | -------------- |
| `filter`         | `string`  | 名称关键字过滤 |
| `isEnabled`      | `boolean` | 仅查询启用设备 |
| `skipCount`      | `number`  | 跳过条数       |
| `maxResultCount` | `number`  | 每页条数       |

返回体：`PagedResultDto<ProjectorDeviceDto>`

##### POST `/api/projectors/tcp` — 创建 TCP 投影机

请求体：

```json
{
    "name": "主投影机",
    "deviceIndex": 0,
    "description": null,
    "isEnabled": true,
    "ipAddress": "192.168.1.100",
    "tcpPort": 1234,
    "connectTimeoutMs": 5000
}
```

| 字段               | 必填 | 约束                 | 说明             |
| ------------------ | ---- | -------------------- | ---------------- |
| `name`             | ✓    | 最长 128             | 设备名称         |
| `deviceIndex`      |      | 0~63                 | 显示序号         |
| `description`      |      | 最长 512             | 描述             |
| `isEnabled`        |      | 默认 `true`          | 是否启用         |
| `ipAddress`        | ✓    | 最长 64              | IP 地址          |
| `tcpPort`          |      | 1~65535，默认 1234   | TCP 端口         |
| `connectTimeoutMs` |      | 500~30000，默认 5000 | 连接超时（毫秒） |

##### POST `/api/projectors/hid` — 创建 USB HID 投影机

请求体：

```json
{
    "name": "HID 投影机",
    "deviceIndex": 0,
    "description": null,
    "isEnabled": true,
    "hidDeviceIndex": 0,
    "connectTimeoutMs": 5000
}
```

| 字段               | 必填 | 约束                 | 说明                             |
| ------------------ | ---- | -------------------- | -------------------------------- |
| `name`             | ✓    | 最长 128             | 设备名称                         |
| `deviceIndex`      |      | 0~63                 | 显示序号                         |
| `description`      |      | 最长 512             | 描述                             |
| `isEnabled`        |      | 默认 `true`          | 是否启用                         |
| `hidDeviceIndex`   |      | 0~15，默认 0         | HID 设备索引（多台同型号时区分） |
| `connectTimeoutMs` |      | 500~30000，默认 5000 | 连接超时（毫秒）                 |

> VID=`0x0483` / PID=`0x5750` 由后端 `ProjectorConsts` 固定，不在请求体中传入。

##### PUT `/api/projectors/{id}` — 更新投影机基本信息

请求体：

```json
{
    "name": "主投影机",
    "description": "更新说明",
    "isEnabled": true,
    "connectTimeoutMs": 5000
}
```

#### 3.5.2 连接管理

| 前端方法              | HTTP 方法 | 路径                              | 说明                              |
| --------------------- | --------- | --------------------------------- | --------------------------------- |
| `connectProjector`    | `POST`    | `/api/projectors/{id}/connect`    | 连接投影机（自动按 TCP/HID 路由） |
| `disconnectProjector` | `POST`    | `/api/projectors/{id}/disconnect` | 断开投影机连接                    |

#### 3.5.3 LED 控制

| 前端方法            | HTTP 方法 | 路径                           | 请求体                         | 说明               |
| ------------------- | --------- | ------------------------------ | ------------------------------ | ------------------ |
| `projectorLedOn`    | `POST`    | `/api/projectors/{id}/led-on`  | 无                             | 开灯               |
| `projectorLedOff`   | `POST`    | `/api/projectors/{id}/led-off` | 无                             | 关灯               |
| `setProjectorLight` | `POST`    | `/api/projectors/set-light`    | `{ projectorDeviceId, light }` | 设置亮度（10~200） |

#### 3.5.4 显示控制

| 前端方法                   | HTTP 方法 | 路径                               | 说明                          |
| -------------------------- | --------- | ---------------------------------- | ----------------------------- |
| `setProjectorDisplayMode`  | `POST`    | `/api/projectors/set-display-mode` | 设置显示模式                  |
| `setProjectorColor`        | `POST`    | `/api/projectors/set-color`        | 设置颜色（多光谱模式）        |
| `setProjectorFlip`         | `POST`    | `/api/projectors/set-flip`         | 设置图像翻转模式              |
| `setProjectorTriggerMode`  | `POST`    | `/api/projectors/set-trigger-mode` | 设置触发模式                  |
| `setProjectorBootImage`    | `POST`    | `/api/projectors/set-boot-image`   | 设置开机默认图案              |
| `setProjectorCheckerboard` | `POST`    | `/api/projectors/set-checkerboard` | 设置棋盘格像素尺寸            |
| `setProjectorRgb`          | `POST`    | `/api/projectors/set-rgb`          | 设置 RGB 分量亮度（彩光模式） |

所有显示控制接口的请求体均包含公共字段 `projectorDeviceId: string`，再附加各自的参数字段。

#### 3.5.5 条纹触发

##### POST `/api/projectors/trigger-once`

- **说明**：触发一次条纹投影序列
- **返回**：`boolean`（成功返回 `true`）

请求体：

```json
{
    "projectorDeviceId": "string",
    "endGray": 255
}
```

| 字段      | 约束            | 说明                         |
| --------- | --------------- | ---------------------------- |
| `endGray` | 0~255，默认 255 | 末尾帧灰度值（0=黑，255=白） |

#### 3.5.6 高级操作

| 前端方法                 | HTTP 方法 | 路径                                      | 说明                                |
| ------------------------ | --------- | ----------------------------------------- | ----------------------------------- |
| `projectorSoftReset`     | `POST`    | `/api/projectors/{id}/soft-reset`         | 软复位投影机                        |
| `projectorSaveParams`    | `POST`    | `/api/projectors/{id}/save-params`        | 保存参数到设备 NVM                  |
| `readProjectorRegister`  | `GET`     | `/api/projectors/{id}/register/{address}` | 读取寄存器（返回 `string \| null`） |
| `writeProjectorRegister` | `POST`    | `/api/projectors/write-register`          | 写入寄存器                          |

写寄存器请求体：

```json
{
    "projectorDeviceId": "string",
    "address": 0,
    "value": 0
}
```

#### 3.5.7 操作日志

##### GET `/api/projectors/logs`

查询参数：

| 参数                | 类型      | 说明                          |
| ------------------- | --------- | ----------------------------- |
| `projectorDeviceId` | `string`  | 指定投影机 ID（不传则查全部） |
| `operationType`     | `number`  | 操作类型枚举                  |
| `isFailedOnly`      | `boolean` | 仅查失败记录                  |
| `skipCount`         | `number`  | 跳过条数                      |
| `maxResultCount`    | `number`  | 每页条数                      |

返回体：`PagedResultDto<ProjectorOperationLogDto>`

```json
{
    "totalCount": 100,
    "items": [
        {
            "id": "string",
            "projectorDeviceId": "string",
            "operationType": 7,
            "isSuccess": true,
            "rawCommand": "T255",
            "parameterSummary": "EndGray=255",
            "errorMessage": null,
            "roundTripMs": 12,
            "occurredAt": "2026-05-20T08:00:00+08:00"
        }
    ]
}
```

#### 3.5.8 关键枚举

`ProjectorConnectionType` — 连接方式：

| 值  | 名称     | 说明                                   |
| --- | -------- | -------------------------------------- |
| `0` | `Tcp`    | TCP/IP 网络连接（默认端口 1234）       |
| `1` | `UsbHid` | USB HID 连接（VID=0x0483，PID=0x5750） |

`ProjectorConnectionStatus` — 连接状态：

| 值  | 名称               | 说明            |
| --- | ------------------ | --------------- |
| `0` | `Unknown`          | 未知 / 从未连接 |
| `1` | `Disconnected`     | 已断开          |
| `2` | `Connected`        | 已连接          |
| `3` | `ConnectionFailed` | 连接失败        |

`ProjectorLedStatus` — LED 灯状态：

| 值  | 名称      | 说明 |
| --- | --------- | ---- |
| `0` | `Unknown` | 未知 |
| `1` | `Off`     | 关闭 |
| `2` | `On`      | 开启 |

`ProjectorDisplayMode` — 显示模式：

| 值  | 名称           | 说明             |
| --- | -------------- | ---------------- |
| `0` | `Black`        | 黑屏             |
| `1` | `White`        | 白屏             |
| `2` | `Cross`        | 十字线           |
| `3` | `Checkerboard` | 棋盘格           |
| `6` | `Internal1`    | 内部图像 1（S6） |
| `7` | `Internal2`    | 内部图像 2（S7） |

`ProjectorColor` — 投影颜色（多光谱模式）：

| 值  | 名称       | 说明             |
| --- | ---------- | ---------------- |
| `0` | `Red`      | 红色             |
| `1` | `Green`    | 绿色             |
| `2` | `Blue`     | 蓝色             |
| `3` | `White`    | 白色（全色）     |
| `4` | `AuraSync` | 彩光（RGB 模式） |

`ProjectorFlipMode` — 翻转模式：

| 值  | 名称     | 说明     |
| --- | -------- | -------- |
| `0` | `None`   | 不翻转   |
| `1` | `FlipX`  | X 轴翻转 |
| `2` | `FlipY`  | Y 轴翻转 |
| `3` | `FlipXY` | XY 翻转  |

`ProjectorTriggerMode` — 触发模式：

| 值  | 名称          | 说明                                           |
| --- | ------------- | ---------------------------------------------- |
| `0` | `Normal`      | 普通触发（收到 T 指令后投射一组条纹）          |
| `1` | `Loop`        | 循环触发（收到 T 后循环投射）                  |
| `2` | `SingleFrame` | 单帧触发（收 T 投射一张保持，收 N 切换下一张） |

`ProjectorBootImage` — 开机默认图案：

| 值  | 名称           | 说明       |
| --- | -------------- | ---------- |
| `0` | `Black`        | 黑图像     |
| `1` | `White`        | 白图像     |
| `2` | `Cross`        | 十字图像   |
| `3` | `Checkerboard` | 棋盘格图像 |
| `6` | `Internal1`    | 内部图像 1 |
| `7` | `Internal2`    | 内部图像 2 |

`ProjectorOperationType` — 操作日志类型：

| 值   | 名称                   |
| ---- | ---------------------- |
| `0`  | `Connect`              |
| `1`  | `Disconnect`           |
| `2`  | `LedOn`                |
| `3`  | `LedOff`               |
| `4`  | `SetLight`             |
| `5`  | `SetDisplayMode`       |
| `6`  | `SetColor`             |
| `7`  | `TriggerOnce`          |
| `8`  | `SendRawCommand`       |
| `9`  | `QueryStatus`          |
| `10` | `SetFlip`              |
| `11` | `SetTriggerMode`       |
| `12` | `SetBootImage`         |
| `13` | `SetCheckerboardPixel` |
| `14` | `SetRgbColor`          |
| `15` | `SoftReset`            |
| `16` | `SaveParams`           |
| `17` | `ReadRegister`         |
| `18` | `WriteRegister`        |

---

## 4. SignalR 文档

### 4.1 基本信息

| 项           | 内容                                  |
| ------------ | ------------------------------------- |
| Hub 地址     | `/signalr-hubs/device-state`          |
| 鉴权         | 匿名可连                              |
| 客户端库     | `@microsoft/signalr`                  |
| 连接策略     | 自动重连                              |
| 当前前端角色 | 只接收服务端推送，不主动调用 Hub 方法 |

### 4.2 连接时机

- 前端在 `App.vue` 生命周期中统一建立和释放连接
- 连接成功后，后端会立刻向当前连接推送一次：
    1. 当前设备状态
    2. 当前活跃故障

### 4.3 服务端推送事件

#### `ReceiveDeviceStateAsync`

- **方向**：服务端 -> 客户端
- **载荷**：`DeviceStateDto`
- **触发时机**：
    - 客户端刚连接成功时
    - 设备状态发生变化时
    - 设备运行模式发生变化时

示例载荷：

```json
{
    "status": 3,
    "runMode": 0,
    "currentFaultId": null,
    "currentFaultLevel": null,
    "currentFaultCode": null,
    "isInTransition": false,
    "canAcceptProductionCommand": true,
    "canSwitchMode": false
}
```

#### `ReceiveDeviceFaultAsync`

- **方向**：服务端 -> 客户端
- **载荷**：`DeviceFaultDto | null`
- **触发时机**：
    - 客户端刚连接成功时
    - 设备状态或模式广播时同步发送当前故障
- **说明**：无活跃故障时返回 `null`

示例载荷：

```json
{
    "id": "string",
    "occurredAt": "2026-05-19T08:00:00+08:00",
    "faultLevel": 1,
    "faultCode": "E1001",
    "faultMessage": "string",
    "faultReason": "string",
    "isResolved": false,
    "isAutoRecovered": false,
    "resolverId": null,
    "resolverName": null,
    "resolvedAt": null,
    "resolutionDescription": null,
    "durationMs": null,
    "causedModeSwitch": false,
    "switchedToMode": null,
    "stateLogId": "string",
    "remark": null,
    "creationTime": "2026-05-19T08:00:00+08:00"
}
```

### 4.4 前端接入示例

```ts
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/signalr-hubs/device-state")
    .withAutomaticReconnect()
    .build();

connection.on("ReceiveDeviceStateAsync", (state) => {
    console.log("设备状态", state);
});

connection.on("ReceiveDeviceFaultAsync", (fault) => {
    console.log("当前故障", fault);
});

await connection.start();
```

## 5. 当前实现备注

1. 当前前端已声明 `refresh-token` 接口，但尚未实际接入刷新流程。
2. `state-log-paged-list` 的前端查询参数比当前后端过滤实现更完整，若后续需要按 `status`、`isSuccessful` 精确过滤，建议同步核对后端实现。
3. 系统管理接口采用根路径风格（如 `/Users/page`），与设备状态接口的 `/api/app/...` 风格不同，接入时需注意。
