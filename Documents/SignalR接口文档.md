# AuroraStruct3D SignalR 接口文档

> 协议：**ASP.NET Core SignalR + MessagePack 二进制协议**
> 服务地址：与 HTTP API 同源（同 Host + Port），路径见各 Hub 章节

---

## 目录

1. [通用说明](#1-通用说明)
2. [相机 Hub（CameraHub）](#2-相机-hubcamerahub)
3. [设备状态 Hub（DeviceStateHub）](#3-设备状态-hubdevicestatehub)
4. [投影机 Hub（ProjectorHub）](#4-投影机-hubprojectorhub)
5. [仪表盘 Hub（DashboardHub）](#5-仪表盘-hubdashboardhub)
6. [数据类型定义](#6-数据类型定义)
7. [前端接入示例（TypeScript）](#7-前端接入示例typescript)

---

## 1. 通用说明

### 1.1 协议与依赖

| 项目       | 说明                                                           |
| ---------- | -------------------------------------------------------------- |
| 客户端库   | `@microsoft/signalr` + `@microsoft/signalr-protocol-msgpack`   |
| 序列化格式 | **MessagePack**（二进制，节省带宽；服务端属性名为 PascalCase） |
| 鉴权       | 所有 Hub 均配置为 `[AllowAnonymous]`，无需携带 Token           |
| 重连策略   | 建议使用 `.withAutomaticReconnect([0, 2000, 5000, 10000])`     |

### 1.2 属性名大小写说明

MessagePack 序列化后，**服务端推送的字段名为 C# PascalCase**（如 `CameraId`、`FrameRate`）。
前端接收时应同时兼容 PascalCase 和 camelCase，例如：

```typescript
const value = raw.frameRate ?? raw.FrameRate;
```

### 1.3 连接示例（最小可用）

```typescript
import * as signalR from "@microsoft/signalr";
import { MessagePackHubProtocol } from "@microsoft/signalr-protocol-msgpack";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/signalr-hubs/camera")
    .withHubProtocol(new MessagePackHubProtocol())
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();

await connection.start();
```

---

## 2. 相机 Hub（CameraHub）

**路由：** `/signalr-hubs/camera`
**协议：** MessagePack
**鉴权：** 匿名

### 2.1 连接生命周期

| 事件                         | 服务端行为                                                                                             |
| ---------------------------- | ------------------------------------------------------------------------------------------------------ |
| 连接成功（`OnConnected`）    | 立即向当前客户端推送全部相机当前状态快照（`ReceiveCameraStateAsync`，每台相机一条）                    |
| 断开连接（`OnDisconnected`） | 触发 30 秒宽限期定时器，宽限期内若无客户端 `ReattachPreviewAsync` 续约，则自动停止该连接关联的预览会话 |

### 2.2 服务端 → 客户端推送（Server-to-Client）

#### `ReceiveCameraStateAsync`

推送相机状态变更快照。连接时立即推送全部相机当前状态；后续每次相机状态变化时广播。

```typescript
connection.on("ReceiveCameraStateAsync", (state: CameraStateDto) => {
    // state.cameraId 标识哪台相机
});
```

**参数：**

| 参数    | 类型             | 说明                                                   |
| ------- | ---------------- | ------------------------------------------------------ |
| `state` | `CameraStateDto` | 相机完整状态快照，见 [CameraStateDto](#camerastatedto) |

---

#### `ReceiveCameraFrameAsync`

推送一帧 JPEG 压缩图像字节数据，用于实时预览。推送频率由后台服务控制（软件预览默认约 10 FPS）。

```typescript
connection.on(
    "ReceiveCameraFrameAsync",
    (cameraId: string, frame: Uint8Array) => {
        const blob = new Blob([frame], { type: "image/jpeg" });
        const url = URL.createObjectURL(blob);
        imgElement.src = url;
    },
);
```

**参数：**

| 参数       | 类型         | 说明                                                                     |
| ---------- | ------------ | ------------------------------------------------------------------------ |
| `cameraId` | `string`     | 相机设备 ID（UUID）                                                      |
| `frame`    | `Uint8Array` | JPEG 编码的帧字节数组（MessagePack 解码后为 `Uint8Array` 或 `number[]`） |

> **注意：** 使用完毕后应调用 `URL.revokeObjectURL(url)` 释放内存。

---

#### `ReceiveLiveMetricsAsync`

推送相机实时运行指标，周期约 **2 秒**一次（仅在预览运行时推送）。

```typescript
connection.on(
    "ReceiveLiveMetricsAsync",
    (cameraId: string, metrics: CameraLiveMetricsDto) => {
        // metrics.frameRate / metrics.sensorTemperature 等
    },
);
```

**参数：**

| 参数       | 类型                   | 说明                                                       |
| ---------- | ---------------------- | ---------------------------------------------------------- |
| `cameraId` | `string`               | 相机设备 ID                                                |
| `metrics`  | `CameraLiveMetricsDto` | 实时指标，见 [CameraLiveMetricsDto](#cameralivemetricsdto) |

---

#### `OnGenICamNodesChangedAsync`

推送 GenICam 节点**增量变更**列表。触发场景：

- 客户端写入某个 GenICam 节点后，服务端读取并推送该节点及受其影响的依赖节点的最新值/访问模式
- Selector 节点切换导致被影响节点的可用性变化

前端收到后应**原地 patch 本地 NodeMap**，无需重新拉取整张表。

```typescript
connection.on(
    "OnGenICamNodesChangedAsync",
    (cameraId: string, changes: GenICamNodeChangeDto[]) => {
        for (const change of changes) {
            const node = localNodeMap.allNodes.find(
                (n) => n.nodeName === change.nodeName,
            );
            if (!node) continue;
            if (change.value !== null) node.currentValue = change.value;
            if (change.access !== null) node.access = change.access;
            node.isLocked = change.isLocked;
        }
    },
);
```

**参数：**

| 参数       | 类型                     | 说明                                                           |
| ---------- | ------------------------ | -------------------------------------------------------------- |
| `cameraId` | `string`                 | 相机设备 ID                                                    |
| `changes`  | `GenICamNodeChangeDto[]` | 变更节点列表，见 [GenICamNodeChangeDto](#genicamnodechangedto) |

---

#### `OnGenICamNodeMapReloadedAsync`

推送 GenICam NodeMap **整体重新枚举**通知。触发场景：

- 调用后端 `RefreshNodeMap` API 后
- 相机重新打开后自动枚举完成

前端收到后应**重新拉取完整 NodeMap**（调用 `GET /api/app/camera-device/{id}/node-map`）。

```typescript
connection.on(
    "OnGenICamNodeMapReloadedAsync",
    (cameraId: string, enumeratedAt: string) => {
        void fetchNodeMap(cameraId);
    },
);
```

**参数：**

| 参数           | 类型     | 说明                                                                |
| -------------- | -------- | ------------------------------------------------------------------- |
| `cameraId`     | `string` | 相机设备 ID                                                         |
| `enumeratedAt` | `string` | NodeMap 枚举完成时间（ISO 8601 UTC，经 MessagePack 序列化为字符串） |

---

### 2.3 客户端 → 服务端调用（Client-to-Server）

#### `ReattachPreviewAsync`

页面刷新、路由返回或断线重连后调用。在 30 秒宽限期内续约指定相机的预览会话，将会话关联的 `ConnectionId` 更新为当前连接，防止预览被定时回收。

```typescript
const canceled = await connection.invoke<boolean>(
    "ReattachPreviewAsync",
    cameraId,
);
// canceled = true  → 成功续约（宽限期内），预览将继续
// canceled = false → 相机当前不在宽限期内（无需续约或会话不存在）
```

**参数：**

| 参数       | 类型     | 说明                       |
| ---------- | -------- | -------------------------- |
| `cameraId` | `string` | 相机设备 ID（UUID 字符串） |

**返回值：** `boolean`

---

## 3. 设备状态 Hub（DeviceStateHub）

**路由：** `/signalr-hubs/device-state`
**协议：** JSON（默认，不使用 MessagePack）
**鉴权：** 匿名

### 3.1 连接生命周期

| 事件     | 服务端行为                                                                                   |
| -------- | -------------------------------------------------------------------------------------------- |
| 连接成功 | 立即推送当前设备状态（`ReceiveDeviceStateAsync`）和当前活跃故障（`ReceiveDeviceFaultAsync`） |

### 3.2 服务端 → 客户端推送

#### `ReceiveDeviceStateAsync`

推送整机设备状态快照。连接时立即推送；后续每次状态变化时广播给所有客户端。

```typescript
connection.on("ReceiveDeviceStateAsync", (state: DeviceStateDto) => {
    console.log("设备状态：", state.status, state.runMode);
});
```

**参数：**

| 参数    | 类型             | 说明                                               |
| ------- | ---------------- | -------------------------------------------------- |
| `state` | `DeviceStateDto` | 设备完整状态，见 [DeviceStateDto](#devicestatedto) |

---

#### `ReceiveDeviceFaultAsync`

推送当前活跃故障。无故障时值为 `null`；故障恢复后再次推送 `null`。

```typescript
connection.on("ReceiveDeviceFaultAsync", (fault: DeviceFaultDto | null) => {
    if (fault) {
        console.warn("当前故障：", fault.faultCode, fault.faultMessage);
    }
});
```

**参数：**

| 参数    | 类型                     | 说明                                                                |
| ------- | ------------------------ | ------------------------------------------------------------------- |
| `fault` | `DeviceFaultDto \| null` | 当前活跃故障，见 [DeviceFaultDto](#devicefaultdto)；无故障为 `null` |

### 3.3 客户端 → 服务端调用

此 Hub 无客户端可调用方法。所有操作通过 REST API 完成（见 `/api/app/device-state/*`）。

---

## 4. 投影机 Hub（ProjectorHub）

**路由：** `/signalr-hubs/projector`
**协议：** JSON（默认，不使用 MessagePack）
**鉴权：** 匿名

### 4.1 连接生命周期

连接成功后无自动初始推送，需通过 REST API 获取初始列表（`GET /api/projectors`）。

### 4.2 服务端 → 客户端推送

#### `ReceiveProjectorStateAsync`

推送单台投影机的完整状态快照（含连接状态、LED 状态、所有配置参数）。

```typescript
connection.on("ReceiveProjectorStateAsync", (projector: ProjectorDeviceDto) => {
    // projector.id 标识哪台设备
    updateLocalList(projector);
});
```

**参数：**

| 参数        | 类型                 | 说明                                                         |
| ----------- | -------------------- | ------------------------------------------------------------ |
| `projector` | `ProjectorDeviceDto` | 投影机完整状态，见 [ProjectorDeviceDto](#projectordevicedto) |

---

#### `ReceiveProjectorConnectionChangedAsync`

推送投影机连接状态变更事件。

```typescript
connection.on(
    "ReceiveProjectorConnectionChangedAsync",
    (id: string, status: string) => {
        // id: 投影机设备 ID
        // status: 如 "Connected" / "Disconnected" / "ConnectionFailed"
    },
);
```

**参数：**

| 参数     | 类型     | 说明                  |
| -------- | -------- | --------------------- |
| `id`     | `string` | 投影机设备 ID（UUID） |
| `status` | `string` | 连接状态文字描述      |

---

#### `ReceiveProjectorLedChangedAsync`

推送投影机 LED 状态变更事件。

```typescript
connection.on(
    "ReceiveProjectorLedChangedAsync",
    (id: string, ledStatus: string) => {
        // id: 投影机设备 ID
        // ledStatus: "On" / "Off" / "Unknown"
    },
);
```

**参数：**

| 参数        | 类型     | 说明                  |
| ----------- | -------- | --------------------- |
| `id`        | `string` | 投影机设备 ID（UUID） |
| `ledStatus` | `string` | LED 状态文字描述      |

### 4.3 客户端 → 服务端调用

此 Hub 无客户端可调用方法。

---

## 5. 仪表盘 Hub（DashboardHub）

**路由：** `/signalr-hubs/dashboard`
**协议：** JSON（默认）
**鉴权：** 匿名

> 仪表盘 Hub 当前为空实现，无任何推送或可调用方法。后台服务（CAP/Hangfire 统计）的推送机制预留于此 Hub，具体消息格式待扩展。

---

## 6. 数据类型定义

### CameraStateDto

```typescript
interface CameraStateDto {
    cameraId: string; // 相机设备 ID（UUID）
    status: number; // CameraStatus 枚举值（见下）
    statusText: string; // 状态文字描述
    isCapturing: boolean; // 是否正在采集
    isXmlLoaded: boolean; // GenICam XML 是否已加载
    sensorTemperature: number | null; // 传感器温度（℃）
    fpgaTemperature: number | null; // FPGA 温度（℃）
    lastErrorMessage: string | null; // 最近一次错误信息
    changedAt: string; // 状态变更时间（ISO 8601 UTC）
}

// CameraStatus 枚举
enum CameraStatus {
    Unknown = 0,
    Ready = 1, // 已打开、空闲
    Capturing = 2, // 采集中（预览或触发）
    Error = 3,
    Closed = 4,
}
```

### CameraLiveMetricsDto

```typescript
interface CameraLiveMetricsDto {
    fpgaTemperature: number; // FPGA 温度（℃）
    sensorTemperature: number; // 传感器温度（℃）
    frameRate: number; // 当前帧率（FPS）
    aeStatus: number; // 自动曝光状态（0=Off/1=Once/2=Continuous）
    currentBufFrames: number; // 当前内部缓冲帧数
}
```

> MessagePack 推送时字段名为 PascalCase（`FpgaTemperature` 等），前端需做兼容处理：
>
> ```typescript
> const fps = raw.frameRate ?? raw.FrameRate ?? 0;
> ```

### GenICamNodeChangeDto

```typescript
interface GenICamNodeChangeDto {
    nodeName: string; // GenICam 节点名（如 "ExposureTime"）
    value: string | null; // 节点当前值（字符串表示）；null 表示值未变
    access: string | null; // 节点访问模式（"ReadWrite"/"ReadOnly"/"WriteOnly"/"NotAvailable"）；null 表示访问模式未变
    isLocked: boolean; // 节点是否被锁定（不可交互）
}
```

### DeviceStateDto

```typescript
interface DeviceStateDto {
    status: DeviceStatus; // 设备运行状态
    runMode: DeviceRunMode; // 运行模式
    currentFaultId: string | null; // 当前活跃故障 ID
    currentFaultLevel: DeviceFaultLevel | null; // 当前故障等级
    currentFaultCode: string | null; // 当前故障码
    isInTransition: boolean; // 是否处于状态切换过渡中
    canAcceptProductionCommand: boolean; // 是否可接受生产指令
    canSwitchMode: boolean; // 是否可切换运行模式
}

enum DeviceStatus {
    Standby = 0,
    Starting = 1,
    Running = 2,
    Paused = 3,
    Stopping = 4,
    Stopped = 5,
    Resetting = 6,
    FaultAcknowledging = 7,
    Fault = 8,
    EmergencyStop = 9,
    Initializing = 10,
}

enum DeviceRunMode {
    Online = 0,
    Auto = 1,
    Manual = 2,
    Maintenance = 3,
}

enum DeviceFaultLevel {
    Warning = 0,
    GeneralFault = 1,
    SevereFault = 2,
    SafetyFault = 3,
}
```

### DeviceFaultDto

```typescript
interface DeviceFaultDto {
    id: string;
    occurredAt: string; // ISO 8601 UTC
    faultLevel: DeviceFaultLevel;
    faultCode: string | null;
    faultMessage: string | null;
    faultReason: string | null;
    isResolved: boolean;
    isAutoRecovered: boolean;
    resolverId: string | null;
    resolverName: string | null;
    resolvedAt: string | null;
    resolutionDescription: string | null;
    durationMs: number | null; // 故障持续时长（毫秒）
    causedModeSwitch: boolean; // 是否导致模式切换
    switchedToMode: DeviceRunMode | null;
    stateLogId: string | null;
    remark: string | null;
    creationTime: string;
}
```

### ProjectorDeviceDto

```typescript
interface ProjectorDeviceDto {
    id: string;
    name: string;
    deviceIndex: number;
    description: string | null;
    isEnabled: boolean;

    connectionType: ProjectorConnectionType; // Tcp=0 / UsbHid=1
    ipAddress: string | null;
    tcpPort: number;
    hidDeviceIndex: number;
    connectTimeoutMs: number;

    firmwareVersion: string | null;
    deviceHardwareId: number;

    connectionStatus: ProjectorConnectionStatus; // Unknown=0/Disconnected=1/Connected=2/ConnectionFailed=3
    connectionStatusText: string;
    ledStatus: ProjectorLedStatus; // Unknown=0/Off=1/On=2
    ledStatusText: string;
    lastLightValue: number;
    lastDisplayMode: number;
    lastColor: ProjectorColor; // Red=0/Green=1/Blue=2/White=3/AuraSync=4
    checkerboardPixelSize: number;
    flipMode: ProjectorFlipMode; // None=0/FlipX=1/FlipY=2/FlipXY=3
    triggerMode: ProjectorTriggerMode; // Normal=0/Loop=1/SingleFrame=2
    bootImage: ProjectorBootImage;
    ledRgbR: number;
    ledRgbG: number;
    ledRgbB: number;
    lastCommunicationAt: string | null;
    lastConnectedAt: string | null;
    lastDisconnectedAt: string | null;
}
```

---

## 7. 前端接入示例（TypeScript）

### 7.1 相机 Hub 完整接入示例

```typescript
import * as signalR from "@microsoft/signalr";
import { MessagePackHubProtocol } from "@microsoft/signalr-protocol-msgpack";

// ─── 类型定义 ────────────────────────────────────────────────────────────────

interface CameraStateDto {
    cameraId: string;
    status: number;
    statusText: string;
    isCapturing: boolean;
    isXmlLoaded: boolean;
    sensorTemperature: number | null;
    fpgaTemperature: number | null;
    lastErrorMessage: string | null;
    changedAt: string;
}

interface CameraLiveMetricsDto {
    fpgaTemperature: number;
    sensorTemperature: number;
    frameRate: number;
    aeStatus: number;
    currentBufFrames: number;
}

interface GenICamNodeChangeDto {
    nodeName: string;
    value: string | null;
    access: string | null;
    isLocked: boolean;
}

// ─── 连接建立 ────────────────────────────────────────────────────────────────

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/signalr-hubs/camera")
    .withHubProtocol(new MessagePackHubProtocol())
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();

// ─── 注册事件处理（必须在 start() 前注册）────────────────────────────────────

// 相机状态推送
connection.on("ReceiveCameraStateAsync", (state: CameraStateDto) => {
    console.log("相机状态变更", state.cameraId, state.statusText);
});

// 实时预览帧（JPEG 字节流）
const previewUrls = new Map<string, string>();
connection.on(
    "ReceiveCameraFrameAsync",
    (cameraId: string, frame: Uint8Array) => {
        const oldUrl = previewUrls.get(cameraId);
        const blob = new Blob(
            [
                frame instanceof Uint8Array
                    ? frame
                    : Uint8Array.from(frame as number[]),
            ],
            { type: "image/jpeg" },
        );
        const url = URL.createObjectURL(blob);
        previewUrls.set(cameraId, url);
        if (oldUrl) setTimeout(() => URL.revokeObjectURL(oldUrl), 1000);
    },
);

// 实时运行指标（注意兼容 PascalCase）
connection.on(
    "ReceiveLiveMetricsAsync",
    (cameraId: string, raw: Record<string, unknown>) => {
        const metrics: CameraLiveMetricsDto = {
            frameRate: Number(raw.frameRate ?? raw.FrameRate ?? 0),
            sensorTemperature: Number(
                raw.sensorTemperature ?? raw.SensorTemperature ?? 0,
            ),
            fpgaTemperature: Number(
                raw.fpgaTemperature ?? raw.FpgaTemperature ?? 0,
            ),
            aeStatus: Number(raw.aeStatus ?? raw.AeStatus ?? 0),
            currentBufFrames: Number(
                raw.currentBufFrames ?? raw.CurrentBufFrames ?? 0,
            ),
        };
        console.log("指标", cameraId, metrics);
    },
);

// GenICam 节点增量变更
connection.on(
    "OnGenICamNodesChangedAsync",
    (cameraId: string, changes: GenICamNodeChangeDto[]) => {
        console.log("节点变更", cameraId, changes);
    },
);

// NodeMap 整体重新枚举通知
connection.on(
    "OnGenICamNodeMapReloadedAsync",
    (cameraId: string, enumeratedAt: string) => {
        console.log("NodeMap 已重新枚举", cameraId, enumeratedAt);
        // 重新拉取：GET /api/app/camera-device/{cameraId}/node-map
    },
);

// 重连事件
connection.onreconnected(async () => {
    console.log("已重连，续约预览中...");
    // 对曾经在预览的相机发送续约
    const myCameraId = "...";
    const reattached = await connection.invoke<boolean>(
        "ReattachPreviewAsync",
        myCameraId,
    );
    console.log("续约结果：", reattached);
});

connection.onclose(() => console.log("连接已断开"));

// ─── 启动连接 ────────────────────────────────────────────────────────────────

await connection.start();
console.log("相机 Hub 已连接，ConnectionId：", connection.connectionId);

// ─── 开始预览（需通过 REST API 发送开始预览请求，携带 ConnectionId）────────

// POST /api/app/camera-device/{cameraId}/start-preview
// Body: { "connectionId": connection.connectionId, "enableRtp": false }

// ─── 重连/刷新后续约 ─────────────────────────────────────────────────────────

const reattached = await connection.invoke<boolean>(
    "ReattachPreviewAsync",
    cameraId,
);
```

### 7.2 设备状态 Hub 接入示例

```typescript
// 设备状态 Hub 使用默认 JSON 协议，无需 MessagePack
const devConn = new signalR.HubConnectionBuilder()
    .withUrl("/signalr-hubs/device-state")
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();

devConn.on("ReceiveDeviceStateAsync", (state) => {
    console.log("设备状态", state.status, state.runMode);
});

devConn.on("ReceiveDeviceFaultAsync", (fault) => {
    if (fault) console.warn("活跃故障", fault.faultCode, fault.faultMessage);
    else console.log("故障已恢复");
});

await devConn.start();
```

### 7.3 投影机 Hub 接入示例

```typescript
const projConn = new signalR.HubConnectionBuilder()
    .withUrl("/signalr-hubs/projector")
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();

projConn.on("ReceiveProjectorStateAsync", (projector) => {
    console.log(
        "投影机状态",
        projector.id,
        projector.connectionStatus,
        projector.ledStatus,
    );
});

projConn.on("ReceiveProjectorConnectionChangedAsync", (id, status) => {
    console.log("连接状态变更", id, status);
});

projConn.on("ReceiveProjectorLedChangedAsync", (id, ledStatus) => {
    console.log("LED 状态变更", id, ledStatus);
});

await projConn.start();
```

---

## 附录：Hub 路由汇总

| Hub            | 路由                         | 协议        | 鉴权 | 客户端可调用方法                                  |
| -------------- | ---------------------------- | ----------- | ---- | ------------------------------------------------- |
| CameraHub      | `/signalr-hubs/camera`       | MessagePack | 匿名 | `ReattachPreviewAsync(cameraId: string): boolean` |
| DeviceStateHub | `/signalr-hubs/device-state` | JSON        | 匿名 | 无                                                |
| ProjectorHub   | `/signalr-hubs/projector`    | JSON        | 匿名 | 无                                                |
| DashboardHub   | `/signalr-hubs/dashboard`    | JSON        | 匿名 | 无（预留）                                        |
