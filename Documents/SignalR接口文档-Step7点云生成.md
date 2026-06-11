# Step7 点云生成 接口文档（REST + SignalR）

**更新日期**：2026-06-10  
**Hub 地址**：`/signalr-hubs/camera`  
**协议**：MessagePack over WebSocket（`@microsoft/signalr-protocol-msgpack`）

---

## 目录

1. [概述](#1-概述)
2. [连接与鉴权](#2-连接与鉴权)
3. [分组管理（客户端 → 服务端）](#3-分组管理客户端--服务端)
4. [REST API](#4-rest-api)
5. [服务端推送事件（服务端 → 客户端）](#5-服务端推送事件服务端--客户端)
6. [数据结构](#6-数据结构)
7. [枚举定义](#7-枚举定义)
8. [完整前端接入示例](#8-完整前端接入示例)
9. [运行时序图](#9-运行时序图)
10. [注意事项](#10-注意事项)

---

## 1. 概述

Step7 点云生成与 Step6 在线扫描**共用同一个 CameraHub**（`/signalr-hubs/camera`），通过不同的**项目分组**隔离推送，无需建立额外连接。

**交互模式**：按钮触发式（非持续运行）

```
用户点击"生成点云"按钮
    → POST /api/app/calib-point-cloud/generate
    → 后台异步执行（相机采集 → 相位解包 → 深度计算 → PLY 输出）
    → SignalR 实时推送各阶段进度（0→100%）
    → 完成后推送 PLY 文件下载 URL
```

---

## 2. 连接与鉴权

Step7 与 Step6 共用同一连接实例，无需重复建立。若页面单独使用 Step7：

```typescript
import * as signalR from '@microsoft/signalr'
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack'

const hubConnection = new signalR.HubConnectionBuilder()
    .withUrl('/signalr-hubs/camera', { skipNegotiation: false })
    .withHubProtocol(new MessagePackHubProtocol())
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(signalR.LogLevel.Warning)
    .build()

await hubConnection.start()
```

---

## 3. 分组管理（客户端 → 服务端）

Step7 使用独立的点云分组，与 Step6 扫描分组**互相隔离**。

### 3.1 加入点云分组

连接成功后调用，订阅指定项目的点云生成推送。

```typescript
await hubConnection.invoke('JoinPointCloudGroupAsync', calibProjectId /* string (GUID) */)
```

| 参数 | 类型 | 说明 |
|---|---|---|
| `calibProjectId` | `string` (GUID) | 标定项目 ID |

**服务端分组名**（内部实现）：`calib-point-cloud:{calibProjectId:N}`

### 3.2 退出点云分组

```typescript
await hubConnection.invoke('LeavePointCloudGroupAsync', calibProjectId /* string (GUID) */)
```

### 3.3 重连后恢复分组

```typescript
hubConnection.onreconnected(async () => {
    await hubConnection.invoke('JoinPointCloudGroupAsync', calibProjectId)
    await refreshPointCloudStatus()  // 重新拉取最新状态
})
```

---

## 4. REST API

基础路径：`/api/app/calib-point-cloud`（由 ABP 自动生成）

### 4.1 触发点云生成

```
POST /api/app/calib-point-cloud/generate
```

**请求体**（JSON）：

```json
{
    "calibProjectId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**响应**：`PointCloudStatusDto`（见第 6 节）

**说明**：
- 若当前有任务正在运行，返回 `400 Bad Request`，消息为 `"点云生成任务正在进行中，请等待完成或先取消。"`
- 接口立即返回（后台异步执行），实际进度通过 SignalR 推送

---

### 4.2 取消点云生成

```
POST /api/app/calib-point-cloud/cancel?calibProjectId={calibProjectId}
```

**参数**：

| 参数 | 位置 | 类型 | 说明 |
|---|---|---|---|
| `calibProjectId` | Query | `string` (GUID) | 标定项目 ID |

**响应**：`PointCloudStatusDto`，`state` 为 `Idle (0)`

---

### 4.3 查询点云生成状态

```
GET /api/app/calib-point-cloud/status/{calibProjectId}
```

**路径参数**：

| 参数 | 类型 | 说明 |
|---|---|---|
| `calibProjectId` | `string` (GUID) | 标定项目 ID |

**响应**：`PointCloudStatusDto`

> 适用于页面初始化时拉取历史状态（如上次已完成的生成结果）。

---

## 5. 服务端推送事件（服务端 → 客户端）

仅推送给**已加入对应项目点云分组**的客户端。

### 5.1 `ReceivePointCloudStatusAsync`

**触发时机**：点云生成过程中任何状态或进度变化时推送

**参数**：

| 参数 | 类型 | 说明 |
|---|---|---|
| `status` | `PointCloudStatusDto` | 完整状态快照（含进度） |

**前端监听**：

```typescript
hubConnection.on('ReceivePointCloudStatusAsync', (status: PointCloudStatusDto) => {
    if (!status || status.calibProjectId !== currentProjectId) return
    localStatus.value = status
})
```

**推送时机表**：

| 时机 | `state` | `progress` | `plyDownloadUrl` |
|---|---|---|---|
| 调用 Generate 接口成功 | `Running (1)` | `0` | `null` |
| 相机采集阶段 | `Running (1)` | `5` ~ `30` | `null` |
| 相位解包 & 深度计算 | `Running (1)` | `30` ~ `70` | `null` |
| PLY 文件输出 | `Running (1)` | `70` ~ `99` | `null` |
| 生成完成 | `Completed (2)` | `100` | 有效 URL |
| 生成失败 | `Failed (3)` | 停留在失败点 | `null` |
| 取消 | `Idle (0)` | `0` | `null` |

---

## 6. 数据结构

### 6.1 GeneratePointCloudInput（触发输入）

```typescript
interface GeneratePointCloudInput {
    /** 标定项目 ID (GUID 字符串) */
    calibProjectId: string
}
```

### 6.2 PointCloudStatusDto（状态 DTO）

```typescript
interface PointCloudStatusDto {
    /** 标定项目 ID (GUID 字符串) */
    calibProjectId: string
    /** 当前运行状态，见 PointCloudRunState 枚举 */
    state: PointCloudRunState
    /** 是否正在生成 */
    isRunning: boolean
    /** 生成进度（0~100） */
    progress: number
    /** 进度阶段描述文字（如"正在采集结构光图像..."） */
    progressMessage: string | null
    /** 生成开始时间 (ISO 8601 UTC)，未启动时为 null */
    startedAt: string | null
    /** 最后状态更新时间 (ISO 8601 UTC) */
    lastUpdatedAt: string
    /** 错误信息（state 为 Failed 时填充） */
    errorMessage: string | null
    /** 生成完成后的 PLY 文件下载 URL（未完成或失败时为 null） */
    plyDownloadUrl: string | null
    /** PLY 文件大小（字节，未完成时为 null） */
    plyFileSizeBytes: number | null
}
```

---

## 7. 枚举定义

### 7.1 PointCloudRunState（运行状态）

| 值 | 名称 | 说明 | 对应 UI 表现 |
|---|---|---|---|
| `0` | `Idle` | 空闲（就绪） | Tag: secondary "就绪" |
| `1` | `Running` | 生成中 | Tag: info "生成中" + ProgressBar |
| `2` | `Completed` | 已完成 | Tag: success "已完成" + 下载按钮 |
| `3` | `Failed` | 生成失败 | Tag: danger "生成失败" + 错误文本 |

---

## 8. 完整前端接入示例

```typescript
import * as signalR from '@microsoft/signalr'
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack'
import {
    generatePointCloud,
    cancelPointCloud,
    getPointCloudStatus,
    PointCloudRunState,
    type PointCloudStatusDto,
} from '@/api/calib-point-cloud'

const calibProjectId = 'your-project-guid'
let hubConnection: signalR.HubConnection | null = null
let status: PointCloudStatusDto | null = null

// ── 1. 建立 Hub 连接并监听推送 ──────────────────────────────────────────────

async function startHub() {
    hubConnection = new signalR.HubConnectionBuilder()
        .withUrl('/signalr-hubs/camera', { skipNegotiation: false })
        .withHubProtocol(new MessagePackHubProtocol())
        .withAutomaticReconnect([0, 2000, 5000, 10000])
        .build()

    // 重连后恢复分组
    hubConnection.onreconnected(async () => {
        await hubConnection!.invoke('JoinPointCloudGroupAsync', calibProjectId)
        await refreshStatus()
    })

    // 监听点云生成状态推送
    hubConnection.on('ReceivePointCloudStatusAsync', (next: PointCloudStatusDto) => {
        if (!next || next.calibProjectId !== calibProjectId) return

        status = next
        console.log(`[点云] state=${next.state} progress=${next.progress}% ${next.progressMessage ?? ''}`)

        switch (next.state) {
            case PointCloudRunState.Running:
                // 更新进度条
                updateProgressBar(next.progress, next.progressMessage)
                break

            case PointCloudRunState.Completed:
                // 显示下载按钮
                console.log('生成完成！下载地址:', next.plyDownloadUrl)
                console.log('文件大小:', formatBytes(next.plyFileSizeBytes))
                showDownloadButton(next.plyDownloadUrl!)
                break

            case PointCloudRunState.Failed:
                console.error('生成失败:', next.errorMessage)
                showErrorMessage(next.errorMessage)
                break
        }
    })

    await hubConnection.start()
    await hubConnection.invoke('JoinPointCloudGroupAsync', calibProjectId)
}

// ── 2. 查询初始状态 ────────────────────────────────────────────────────────

async function refreshStatus() {
    status = await getPointCloudStatus(calibProjectId)
    console.log('当前状态:', status.state, 'progress:', status.progress)

    // 已完成时直接显示下载
    if (status.state === PointCloudRunState.Completed && status.plyDownloadUrl) {
        showDownloadButton(status.plyDownloadUrl)
    }
}

// ── 3. 触发点云生成 ─────────────────────────────────────────────────────────

async function onGenerate() {
    try {
        status = await generatePointCloud({ calibProjectId })
        console.log('已触发生成，等待 SignalR 推送进度...')
    } catch (e) {
        // 任务正在运行时后端返回 400
        console.error('触发失败:', e)
    }
}

// ── 4. 取消生成 ────────────────────────────────────────────────────────────

async function onCancel() {
    status = await cancelPointCloud(calibProjectId)
    console.log('已取消')
}

// ── 5. 下载 PLY 文件 ────────────────────────────────────────────────────────

function onDownload() {
    if (!status?.plyDownloadUrl) return
    const a = document.createElement('a')
    a.href = status.plyDownloadUrl
    a.download = `point-cloud-${calibProjectId}.ply`
    a.click()
}

// ── 6. 清理 ────────────────────────────────────────────────────────────────

async function cleanup() {
    if (hubConnection) {
        await hubConnection.invoke('LeavePointCloudGroupAsync', calibProjectId)
        await hubConnection.stop()
        hubConnection = null
    }
}

// ── 辅助函数 ────────────────────────────────────────────────────────────────

function formatBytes(bytes: number | null): string {
    if (!bytes) return '-'
    if (bytes < 1024) return `${bytes} B`
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
    return `${(bytes / 1024 / 1024).toFixed(2)} MB`
}

function updateProgressBar(progress: number, message: string | null) { /* ... */ }
function showDownloadButton(url: string) { /* ... */ }
function showErrorMessage(msg: string | null) { /* ... */ }
```

---

## 9. 运行时序图

```
前端                    REST API             后台生成任务          SignalR Hub
 │                          │                     │                    │
 │── POST /generate ───────→│                     │                    │
 │                          │── 校验项目 ────────→│                    │
 │                          │── 启动后台任务 ────→│                    │
 │                          │── NotifyStatus ────────────────────────→│
 │←── ReceivePointCloudStatusAsync(Running, 0%) ──────────────────────│
 │←── HTTP 200 ────────────│                     │                    │
 │                          │                     │                    │
 │                          │         [采集结构光图像 5%]              │
 │                          │                     │── NotifyStatus ──→│
 │←── ReceivePointCloudStatusAsync(Running, 5%, "正在采集...") ────────│
 │                          │                     │                    │
 │                          │         [图像预处理 30%]                 │
 │                          │                     │── NotifyStatus ──→│
 │←── ReceivePointCloudStatusAsync(Running, 30%) ─────────────────────│
 │                          │                     │                    │
 │                          │         [相位解包 55%]                   │
 │                          │                     │── NotifyStatus ──→│
 │←── ReceivePointCloudStatusAsync(Running, 55%) ─────────────────────│
 │                          │                     │                    │
 │                          │         [深度计算 70%]                   │
 │                          │                     │── NotifyStatus ──→│
 │←── ReceivePointCloudStatusAsync(Running, 70%) ─────────────────────│
 │                          │                     │                    │
 │                          │         [PLY 输出 90%]                   │
 │                          │                     │── NotifyStatus ──→│
 │←── ReceivePointCloudStatusAsync(Running, 90%) ─────────────────────│
 │                          │                     │                    │
 │                          │         [完成 100%]                      │
 │                          │                     │── NotifyStatus ──→│
 │←── ReceivePointCloudStatusAsync(Completed, 100%, plyDownloadUrl) ──│
 │                          │                     │                    │
 │── [用户点击下载] ────────────────────────────────────────────────── │
 │── GET {plyDownloadUrl} ──────────────────────────────────────────── │
```

**取消流程**：

```
前端                    REST API             后台生成任务          SignalR Hub
 │                          │                     │                    │
 │── POST /cancel ─────────→│                     │                    │
 │                          │── CancellationToken.Cancel() ──────────→│
 │                          │   (后台任务收到取消信号后安全退出)        │
 │                          │── 更新状态为 Idle ──────────────────────│
 │←── ReceivePointCloudStatusAsync(Idle, 0%) ──────────────────────── │
 │←── HTTP 200 ────────────│                     │                    │
```

---

## 10. 注意事项

### 10.1 与 Step6 共用 Hub

Step7 与 Step6 共用 `/signalr-hubs/camera` Hub，但使用**不同分组名**：

| 功能 | Hub 方法 | 分组名格式 |
|---|---|---|
| Step6 在线扫描 | `JoinCalibScanGroupAsync` | `calib-scan:{projectId:N}` |
| Step7 点云生成 | `JoinPointCloudGroupAsync` | `calib-point-cloud:{projectId:N}` |

两者互不干扰，可同时订阅。

### 10.2 触发前验证

后端在 `GenerateAsync` 中仅校验项目是否存在，**不验证**：
- 是否已完成标定（Step5）
- 相机是否在线

实际算法接入时应在 `RunGenerationAsync` 内部做相机在线检查，失败时通过 `_stateStore.Fail()` 推送错误状态。

### 10.3 幂等性

重复调用 `POST /generate` 时：
- 若当前 `isRunning = true`：返回 `400`，不重置状态
- 若当前 `isRunning = false`（含已完成/失败）：正常启动新任务，**覆盖**上次结果

### 10.4 PLY 下载 URL

当前 `plyDownloadUrl` 格式为：
```
/api/app/calib-point-cloud/download/{calibProjectId}
```

该端点尚未实现（当前版本为模拟占位），正式版应返回 BLOB 存储的预签名 URL 或文件服务路径，并设置适当的 `Content-Disposition: attachment` 响应头。

### 10.5 后台任务异常处理

后台 `RunGenerationAsync` 方法在以下情况自动推送 `Failed` 状态：
- 未捕获的 `Exception`（推送 `state=Failed, errorMessage=异常消息`）
- `OperationCanceledException`（静默退出，不推送 Failed）

前端应始终监听 `state === Failed` 并展示 `errorMessage`，避免进度条永久停留。

### 10.6 页面刷新恢复

页面刷新后 SignalR 连接断开，但后台任务仍在运行。恢复步骤：

1. 重新建立 Hub 连接
2. 调用 `JoinPointCloudGroupAsync` 重新订阅
3. 调用 `GET /status/{calibProjectId}` 拉取当前状态
4. 若 `isRunning = true`，等待后续 SignalR 推送即可（任务仍在运行）
5. 若 `state = Completed`，直接显示下载按钮（`plyDownloadUrl` 在 Status 中）
