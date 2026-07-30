# 相机实时推流与评分 SignalR 接口文档

> 适用版本：AuroraStruct3D
> 协议：ASP.NET Core SignalR + **MessagePack**（二进制，非 JSON）
> Hub 路径：`/signalr-hubs/camera`

---

## 1. 连接说明

### 1.1 Hub 地址

```
ws(s)://<host>/signalr-hubs/camera
```

### 1.2 协议要求

连接时**必须使用 MessagePack 子协议**，否则帧数据（`byte[]`）无法正确反序列化。

| 参数              | 值                                        |
| ----------------- | ----------------------------------------- |
| 协议              | `@microsoft/signalr-protocol-msgpack`     |
| `skipNegotiation` | `false`（走协商握手）                     |
| 鉴权              | 无需（Hub 标记 `[AllowAnonymous]`）       |
| 自动重连          | 建议开启，延迟序列：`0ms → 2s → 5s → 10s` |

### 1.3 JavaScript / TypeScript 示例

```ts
import * as signalR from "@microsoft/signalr";
import { MessagePackHubProtocol } from "@microsoft/signalr-protocol-msgpack";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/signalr-hubs/camera", { skipNegotiation: false })
    .withHubProtocol(new MessagePackHubProtocol())
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();

await connection.start();
```

### 1.4 连接时自动推送

客户端连接成功后，服务端会**立即推送一次所有相机的当前状态快照**（通过 `ReceiveCameraStateAsync`），无需客户端主动拉取。

---

## 2. 服务端 → 客户端推送方法

### 2.1 `ReceiveCameraFrameAsync` — 视频预览帧

**推送频率**：最高 **15 FPS**，服务端限速
**分辨率**：宽度上限 960 px（等比缩放），原始分辨率快照走独立接口
**编码**：JPEG，质量系数 75

#### 参数

| 参数       | 类型                    | 说明                       |
| ---------- | ----------------------- | -------------------------- |
| `cameraId` | `string`                | 相机设备 ID（UUID 字符串） |
| `frame`    | `byte[]` / `Uint8Array` | JPEG 编码的图像字节数组    |

#### 接收示例（TypeScript）

```ts
connection.on(
    "ReceiveCameraFrameAsync",
    (cameraId: string, frame: Uint8Array) => {
        // 释放旧的 ObjectURL，防止内存泄漏
        const oldUrl = previewUrls.get(cameraId);
        if (oldUrl) URL.revokeObjectURL(oldUrl);

        const blob = new Blob([frame.buffer as ArrayBuffer], {
            type: "image/jpeg",
        });
        const url = URL.createObjectURL(blob);
        previewUrls.set(cameraId, url);

        // 更新 <img> 的 src
        document.getElementById("preview")!.setAttribute("src", url);
    },
);
```

> **注意**：MessagePack 反序列化后 `frame` 为 `Uint8Array`，需用 `.buffer` 取底层 `ArrayBuffer` 再构造 `Blob`。

---

### 2.2 `ReceiveLiveMetricsAsync` — 实时运行指标（含对焦/光圈评分）

**推送频率**：每 **500 ms** 推送一次（仅在预览开启时）

#### 参数

| 参数       | 类型                   | 说明                   |
| ---------- | ---------------------- | ---------------------- |
| `cameraId` | `string`               | 相机设备 ID            |
| `metrics`  | `CameraLiveMetricsDto` | 实时指标对象（见下表） |

#### `CameraLiveMetricsDto` 字段

| 字段                | 类型                    | 说明                                                                         |
| ------------------- | ----------------------- | ---------------------------------------------------------------------------- |
| `fpgaTemperature`   | `number`（整数）        | FPGA 温度，单位 ℃                                                            |
| `sensorTemperature` | `number`（浮点）        | 传感器温度，单位 ℃                                                           |
| `frameRate`         | `number`（浮点）        | 当前实际采集帧率，单位 FPS                                                   |
| `aeStatus`          | `number`（整数）        | 自动曝光状态：`0` = 未运行，`1` = 运行中                                     |
| `currentBufFrames`  | `number`（整数）        | USB 缓冲帧数（近似触发计数）                                                 |
| `focusScore`        | `number`（浮点，0–100） | **对焦清晰度评分**，越高越清晰；算法：Laplacian 方差（对角线 5 点核）        |
| `apertureScore`     | `number`（浮点，0–100） | **曝光质量评分**，越高曝光越适中；算法：过曝/欠曝像素比惩罚                  |
| `apertureHint`      | `number`（整数枚举）    | **光圈调节建议**：`0` = 良好，`1` = 缩小光圈（过曝），`2` = 增大光圈（欠曝） |

> `focusScore` 和 `apertureScore` 仅在预览采集过程中计算，预览未启动时值为 `0`。

#### 接收示例（TypeScript）

```ts
interface CameraLiveMetricsDto {
    fpgaTemperature: number;
    sensorTemperature: number;
    frameRate: number;
    aeStatus: number;
    currentBufFrames: number;
    focusScore: number;
    apertureScore: number;
    apertureHint: number; // 0=Good, 1=Decrease, 2=Increase
}

const ApertureHintLabel: Record<number, string> = {
    0: "曝光良好",
    1: "缩小光圈（过曝）",
    2: "增大光圈（欠曝）",
};

connection.on(
    "ReceiveLiveMetricsAsync",
    (cameraId: string, metrics: CameraLiveMetricsDto) => {
        console.log(`[${cameraId}] 对焦评分: ${metrics.focusScore.toFixed(1)}`);
        console.log(
            `[${cameraId}] 光圈评分: ${metrics.apertureScore.toFixed(1)}`,
        );
        console.log(
            `[${cameraId}] 建议: ${ApertureHintLabel[metrics.apertureHint]}`,
        );
    },
);
```

---

### 2.3 `ReceiveCameraStateAsync` — 相机状态变更

当相机连接、断开、开始/停止采集、发生错误时触发。

#### 参数

| 参数    | 类型             | 说明                       |
| ------- | ---------------- | -------------------------- |
| `state` | `CameraStateDto` | 相机完整状态快照（见下表） |

#### `CameraStateDto` 字段

| 字段                | 类型             | 说明                                                                       |
| ------------------- | ---------------- | -------------------------------------------------------------------------- |
| `cameraId`          | `string`         | 相机设备 ID（UUID）                                                        |
| `status`            | `number`         | 状态枚举值（见下方枚举表）                                                 |
| `statusText`        | `string`         | 状态文本（`Disconnected` / `Connected` / `Ready` / `Capturing` / `Error`） |
| `isCapturing`       | `boolean`        | 是否正在采集                                                               |
| `isXmlLoaded`       | `boolean`        | GenICam NodeMap 是否已加载完成                                             |
| `sensorTemperature` | `number \| null` | 传感器温度（℃），无法获取时为 `null`                                       |
| `fpgaTemperature`   | `number \| null` | FPGA 温度（℃），无法获取时为 `null`                                        |
| `lastErrorMessage`  | `string \| null` | 最近错误信息，无错误时为 `null`                                            |
| `changedAt`         | `string`         | 状态变更时间（ISO 8601 UTC）                                               |

#### `status` 枚举值

| 值  | 含义                |
| --- | ------------------- |
| `0` | Unknown             |
| `1` | Ready（已连接就绪） |
| `2` | Capturing（采集中） |
| `3` | Error               |
| `4` | Closed（已关闭）    |

---

### 2.4 `OnGenICamNodesChangedAsync` — GenICam 节点增量变更

参数配置变化后推送受影响节点的最新值/访问权限，前端增量更新本地缓存，无需重新枚举整张 NodeMap。

> 此推送与推流/评分无关，仅在调用 GenICam 参数 Set 时触发，第三方如不接入 GenICam 配置功能可忽略。

---

### 2.5 `OnGenICamNodeMapReloadedAsync` — NodeMap 整体重载通知

> 同上，可忽略。

---

## 3. 启动预览（REST API）

SignalR 只负责推送，**启动/停止预览需通过 REST API** 调用：

### 3.1 开启预览

```
POST /api/app/camera-device/{id}/start-preview
Content-Type: application/json

{
    "connectionId": "<当前 SignalR 连接 ID>",
    "enableRtp": false
}
```

| 字段           | 类型              | 说明                                                                          |
| -------------- | ----------------- | ----------------------------------------------------------------------------- |
| `connectionId` | `string`（可选）  | 传入 `connection.connectionId` 后，帧数据仅推送给该连接；不传则广播给所有连接 |
| `enableRtp`    | `boolean`（可选） | 是否同时开启 RTP/MJPEG UDP 副流，默认 `true`                                  |

获取 `connectionId`：

```ts
const connectionId = connection.connectionId; // HubConnection 建立后可读
```

### 3.2 停止预览

```
POST /api/app/camera-device/{id}/stop-preview
```

---

## 4. 评分算法说明

### 4.1 对焦清晰度评分（`focusScore`）

- **算法**：Laplacian 方差（对角线 5 点核：中心权重 4，四邻 -1）
- **计算区域**：中心 ROI（完整图像的中心 1/3 区域）
- **归一化**：方差经 Sigmoid 拉伸映射到 0–100
- **典型参考值**：`< 20` 严重虚焦，`20–60` 中等，`> 80` 清晰

### 4.2 曝光质量评分（`apertureScore`）

- **算法**：基于像素亮度分布的过曝/欠曝比例惩罚
    - 过曝：亮度 `> 250` 的像素占比
    - 欠曝：亮度 `< 10` 的像素占比 + 均值过低惩罚
- **归一化**：0–100，100 表示曝光完美
- **典型参考值**：`< 40` 曝光异常，`40–70` 可用，`> 70` 良好

### 4.3 光圈调节建议（`apertureHint`）

| 值  | 触发条件                    | 建议操作 |
| --- | --------------------------- | -------- |
| `0` | 曝光正常                    | 无需调整 |
| `1` | 过曝像素比 > 3%             | 缩小光圈 |
| `2` | 欠曝像素比 > 6% 或均值 < 35 | 增大光圈 |

---

## 5. 完整接入示例（TypeScript）

```ts
import * as signalR from "@microsoft/signalr";
import { MessagePackHubProtocol } from "@microsoft/signalr-protocol-msgpack";

async function startCameraPreview(
    cameraId: string,
    imgElement: HTMLImageElement,
) {
    // 1. 建立 SignalR 连接
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/signalr-hubs/camera", { skipNegotiation: false })
        .withHubProtocol(new MessagePackHubProtocol())
        .withAutomaticReconnect([0, 2000, 5000, 10000])
        .build();

    // 2. 注册帧接收
    let prevUrl: string | null = null;
    connection.on(
        "ReceiveCameraFrameAsync",
        (id: string, frame: Uint8Array) => {
            if (id !== cameraId) return;
            if (prevUrl) URL.revokeObjectURL(prevUrl);
            const blob = new Blob([frame.buffer as ArrayBuffer], {
                type: "image/jpeg",
            });
            prevUrl = URL.createObjectURL(blob);
            imgElement.src = prevUrl;
        },
    );

    // 3. 注册指标（对焦/光圈评分）接收
    connection.on("ReceiveLiveMetricsAsync", (id: string, m: any) => {
        if (id !== cameraId) return;
        console.log(
            `对焦: ${m.focusScore.toFixed(1)}  光圈: ${m.apertureScore.toFixed(1)}  建议: ${m.apertureHint}`,
        );
    });

    // 4. 启动连接
    await connection.start();

    // 5. 通知服务端开始推流（精准推给本连接）
    await fetch(`/api/app/camera-device/${cameraId}/start-preview`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
            connectionId: connection.connectionId,
            enableRtp: false,
        }),
    });

    return connection;
}
```

---

## 6. 注意事项

1. **必须使用 MessagePack 协议**：帧数据为裸字节数组，JSON 协议无法传输二进制。
2. **ObjectURL 必须释放**：每次接收到新帧后调用 `URL.revokeObjectURL(oldUrl)` 避免内存泄漏。
3. **评分仅在预览开启时有效**：预览未启动时 `focusScore` / `apertureScore` 为 `0`。
4. **帧率说明**：SignalR 推帧限速 15 FPS；若需更高帧率（如 120+ FPS 高速拍摄）须启用 RTP UDP 副流（`enableRtp: true`）。
5. **多相机支持**：所有相机共用同一个 Hub 连接，通过 `cameraId` 区分来源。
