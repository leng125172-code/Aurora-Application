# Step6 在线扫描 SignalR 接口文档

**更新日期**：2026-06-10
**Hub 地址**：`/signalr-hubs/camera`
**协议**：MessagePack over WebSocket（`@microsoft/signalr-protocol-msgpack`）

---

## 目录

1. [连接与鉴权](#1-连接与鉴权)
2. [分组管理（客户端 → 服务端）](#2-分组管理客户端--服务端)
3. [REST API 概览](#3-rest-api-概览)
4. [服务端推送事件（服务端 → 客户端）](#4-服务端推送事件服务端--客户端)
5. [数据结构](#5-数据结构)
6. [枚举定义](#6-枚举定义)
7. [完整前端接入示例](#7-完整前端接入示例)
8. [运行时序图](#8-运行时序图)
9. [注意事项](#9-注意事项)

---

## 1. 连接与鉴权

```typescript
import * as signalR from '@microsoft/signalr'
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack'

const hubConnection = new signalR.HubConnectionBuilder()
    .withUrl('/signalr-hubs/camera', { skipNegotiation: false })
    .withHubProtocol(new MessagePackHubProtocol())
    .withAutomaticReconnect([0, 2000, 5000, 10000])  // 自动重连间隔(ms)
    .configureLogging(signalR.LogLevel.Warning)
    .build()

await hubConnection.start()
```

- 需要携带有效的认证 Cookie/Token，连接前确保已登录。
- 自动重连配置：首次断线立即重连，之后分别等待 2s、5s、10s。

---

## 2. 分组管理（客户端 → 服务端）

在线扫描使用**项目级分组**隔离推送，每个标定项目对应独立分组。

### 2.1 加入扫描分组

连接成功后调用，订阅指定项目的扫描推送。

```typescript
// 方法名（服务端 Hub 方法）
await hubConnection.invoke('JoinCalibScanGroupAsync', calibProjectId /* string (GUID) */)
```

| 参数 | 类型 | 说明 |
|---|---|---|
| `calibProjectId` | `string` (GUID) | 标定项目 ID |

### 2.2 退出扫描分组

页面卸载或不再需要推送时调用，避免收到无关通知。

```typescript
await hubConnection.invoke('LeaveCalibScanGroupAsync', calibProjectId /* string (GUID) */)
```

> **最佳实践**：在 `onMounted` 后调用 `JoinCalibScanGroupAsync`，在 `onUnmounted` 前调用 `LeaveCalibScanGroupAsync` 并 `stop()` 连接。

### 2.3 重连后恢复分组

`onreconnected` 回调中需重新加入分组：

```typescript
hubConnection.onreconnected(async () => {
    await hubConnection.invoke('JoinCalibScanGroupAsync', calibProjectId)
    await refreshStatus()  // 重新拉取最新状态
})
```

---

## 3. REST API 概览

所有 HTTP 接口由 ABP 自动生成，基础路径：`/api/app/calib-scan`

| 方法 | 路径 | 说明 |
|---|---|---|
| `POST` | `/api/app/calib-scan/start` | 启动在线扫描会话 |
| `POST` | `/api/app/calib-scan/stop` | 停止在线扫描会话 |
| `GET` | `/api/app/calib-scan/status/{calibProjectId}` | 查询当前扫描状态 |

> HTTP 接口仅用于触发操作和初始状态查询，后续状态变更通过 SignalR 实时推送。

---

## 4. 服务端推送事件（服务端 → 客户端）

所有事件仅推送给**已加入对应项目分组**的客户端。

### 4.1 `ReceiveCalibScanStateAsync`

**触发时机**：扫描状态发生变更时（启动、停止、失败等）

**参数**：

| 参数 | 类型 | 说明 |
|---|---|---|
| `status` | `CalibScanStatusDto` | 完整扫描状态快照 |

**前端监听**：

```typescript
hubConnection.on('ReceiveCalibScanStateAsync', (status: CalibScanStatusDto) => {
    if (!status || status.calibProjectId !== currentProjectId) return
    // 更新本地状态
    localStatus.value = status
})
```

**推送场景**：

| 场景 | state 值 |
|---|---|
| 调用 Start 接口成功 | `Running (2)` |
| 调用 Stop 接口成功 | `Idle (0)` |
| 后台发生异常 | `Failed (4)` |

---

### 4.2 `ReceiveCalibScanMetricsAsync`

**触发时机**：后台指标循环每次采集完成后推送（约每秒一次）

**参数**：

| 参数 | 类型 | 说明 |
|---|---|---|
| `calibProjectId` | `string` (GUID) | 标定项目 ID，用于过滤 |
| `metrics` | `CalibScanMetricsDto` | 实时指标数据 |

**前端监听**：

```typescript
hubConnection.on(
    'ReceiveCalibScanMetricsAsync',
    (projectId: string, metrics: CalibScanMetricsDto) => {
        if (projectId !== currentProjectId || !metrics) return
        // 增量更新指标，不覆盖其余状态字段
        localStatus.value = {
            ...localStatus.value,
            latestMetrics: metrics,
            lastUpdatedAt: metrics.timestamp,
        }
    }
)
```

**推送频率**：约 1 次/秒（由 `CalibScanStateStore.StartMetricLoop` 中的 `Task.Delay(1000)` 控制）

---

## 5. 数据结构

### 5.1 CalibScanStatusDto

```typescript
interface CalibScanStatusDto {
    /** 标定项目 ID (GUID 字符串) */
    calibProjectId: string
    /** 扫描模式，见 CalibScanMode 枚举 */
    scanMode: CalibScanMode
    /** 当前运行状态，见 CalibScanRunState 枚举 */
    state: CalibScanRunState
    /** 是否正在运行 */
    isRunning: boolean
    /** 启动时间 (ISO 8601 UTC)，未启动时为 null */
    startedAt: string | null
    /** 最后状态更新时间 (ISO 8601 UTC) */
    lastUpdatedAt: string
    /** 错误信息（state 为 Failed 时填充） */
    errorMessage: string | null
    /** 最新一帧实时指标（未启动时为 null） */
    latestMetrics: CalibScanMetricsDto | null
}
```

### 5.2 CalibScanMetricsDto

```typescript
interface CalibScanMetricsDto {
    /** 当前预览帧率 (FPS) */
    fps: number
    /** 深度有效率 (0~1)，双目模式下有效 */
    depthValidRate: number
    /** 重建置信度 (0~1) */
    confidence: number
    /** 深度图 JPEG DataUri（格式：`data:image/jpeg;base64,...`），无数据时为 null */
    depthMapDataUri: string | null
    /** 当前帧序号（从 1 开始递增） */
    frameIndex: number
    /** 指标采集时间戳 (ISO 8601 UTC) */
    timestamp: string
}
```

---

## 6. 枚举定义

### 6.1 CalibScanMode（扫描模式）

| 值 | 名称 | 说明 |
|---|---|---|
| `0` | `TwoCamera0Light` | 双目无光（纯双目视差） |
| `1` | `OneCamera1Light` | 单目一光（单相机 + 结构光） |
| `2` | `TwoCamera1Light` | 双目一光（双相机 + 结构光） |

> 根据项目的 `deviceType` 字段自动限定可用模式：
>
> - `TwoCamera0Light` 项目 → 只能选 `TwoCamera0Light`
> - `OneCamera1Light` 项目 → 只能选 `OneCamera1Light`
> - `TwoCamera1Light` 项目 → 只能选 `TwoCamera1Light`

### 6.2 CalibScanRunState（运行状态）

| 值 | 名称 | 说明 | 对应 UI 表现 |
|---|---|---|---|
| `0` | `Idle` | 空闲（未运行） | Tag: secondary "未运行" |
| `1` | `Starting` | 启动中 | Tag: info "启动中" |
| `2` | `Running` | 运行中 | Tag: success "运行中" |
| `3` | `Stopping` | 停止中 | Tag: info "停止中" |
| `4` | `Failed` | 运行失败 | Tag: danger "运行失败" |

---

## 7. 完整前端接入示例

```typescript
import * as signalR from '@microsoft/signalr'
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack'
import { getCalibScanStatus, startCalibScan, stopCalibScan } from '@/api/calib-scan'

const calibProjectId = 'your-project-guid'
let hubConnection: signalR.HubConnection | null = null

// ── 1. 建立 Hub 连接 ──────────────────────────────────────────────────────

async function startHub() {
    hubConnection = new signalR.HubConnectionBuilder()
        .withUrl('/signalr-hubs/camera', { skipNegotiation: false })
        .withHubProtocol(new MessagePackHubProtocol())
        .withAutomaticReconnect([0, 2000, 5000, 10000])
        .build()

    // 重连后恢复分组
    hubConnection.onreconnected(async () => {
        await hubConnection!.invoke('JoinCalibScanGroupAsync', calibProjectId)
        await refreshStatus()
    })

    // 监听状态变更推送
    hubConnection.on('ReceiveCalibScanStateAsync', (status) => {
        if (status?.calibProjectId !== calibProjectId) return
        console.log('状态变更:', status.state, status.isRunning)
    })

    // 监听实时指标推送
    hubConnection.on('ReceiveCalibScanMetricsAsync', (projectId, metrics) => {
        if (projectId !== calibProjectId) return
        console.log(`FPS: ${metrics.fps}, 深度有效率: ${(metrics.depthValidRate * 100).toFixed(1)}%`)
        if (metrics.depthMapDataUri) {
            // 显示深度图
            document.querySelector<HTMLImageElement>('#depth-map')!.src = metrics.depthMapDataUri
        }
    })

    await hubConnection.start()
    await hubConnection.invoke('JoinCalibScanGroupAsync', calibProjectId)
}

// ── 2. 拉取初始状态 ────────────────────────────────────────────────────────

async function refreshStatus() {
    const status = await getCalibScanStatus(calibProjectId)
    console.log('当前状态:', status)
}

// ── 3. 启动 / 停止 ─────────────────────────────────────────────────────────

async function startScan() {
    const status = await startCalibScan({
        calibProjectId,
        scanMode: 2, // TwoCamera1Light
    })
    console.log('已启动:', status.state)
}

async function stopScan() {
    const status = await stopCalibScan({ calibProjectId })
    console.log('已停止:', status.state)
}

// ── 4. 清理 ────────────────────────────────────────────────────────────────

async function cleanup() {
    if (hubConnection) {
        await hubConnection.invoke('LeaveCalibScanGroupAsync', calibProjectId)
        await hubConnection.stop()
        hubConnection = null
    }
}
```

---

## 8. 运行时序图

```
前端                     后端 REST              后端 SignalR Hub         指标循环
 │                           │                       │                      │
 │── start() ──────────────→ │                       │                      │
 │                           │── 验证项目 & 模式 ──→ │                      │
 │                           │── 启动 MetricLoop ──→ │                      │
 │                           │── NotifyState ──────→ │                      │
 │← ReceiveCalibScanStateAsync(Running) ────────────  │                      │
 │← HTTP 200 CalibScanStatusDto ─────────────────── │                      │
 │                           │                       │                      │
 │                           │                       │←── 每秒采集 ─────────│
 │                           │                       │    抓拍相机           │
 │                           │                       │    计算深度图         │
 │                           │                       │── NotifyMetrics ──→  │
 │← ReceiveCalibScanMetricsAsync(fps,depthMap...) ─── │                      │
 │                           │                       │                      │
 │── stop() ───────────────→ │                       │                      │
 │                           │── 取消 MetricLoop ──→ │                      │
 │                           │── NotifyState ──────→ │                      │
 │← ReceiveCalibScanStateAsync(Idle) ─────────────── │                      │
 │← HTTP 200 CalibScanStatusDto ─────────────────── │                      │
```

---

## 9. 注意事项

### 9.1 多标签页冲突

同一项目同时被多个浏览器标签页操作时，后启动的会话会覆盖前一个。后端收到 `409 Conflict` 时错误码为 `AuroraStruct3D:DeviceOccupied`，前端应友好提示：

```typescript
const code = error?.response?.data?.error?.code
if (code === 'AuroraStruct3D:DeviceOccupied') {
    // 显示冲突提示，不再抛出异常
}
```

### 9.2 双 USB 相机限制

RK3588 USB 控制器对两路高带宽 USB 相机是串行/独占的，Step6 采用**后台轮询抓拍**（每秒依次抓主/从相机），而非实时视频流，因此深度图更新频率约为 1 次/秒。

### 9.3 深度图 DataUri 大小

`depthMapDataUri` 为 JPEG 编码的 Base64 DataUri，单帧约 50~200 KB（视分辨率而定）。每秒通过 SignalR MessagePack 推送，注意：

- 不要在 WebSocket 传输大于 1MB 的单帧，应考虑降分辨率后推送
- 前端接收后直接赋值给 `<img src="">` 即可，无需手动解码

### 9.4 连接断开后的状态恢复

SignalR 重连后**分组成员资格会丢失**，必须在 `onreconnected` 回调中重新调用 `JoinCalibScanGroupAsync`，并调用 REST 接口 `getCalibScanStatus` 拉取最新状态，避免页面显示过期数据。

### 9.5 页面卸载清理

组件卸载时必须：

1. 调用 `LeaveCalibScanGroupAsync` 退出分组
2. 调用 `hubConnection.stop()` 关闭连接

否则服务端仍会持续推送，浪费带宽。
