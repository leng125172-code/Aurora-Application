# Step6 在线扫描前端对接说明

本文档对应当前 Step6 在线扫描实现，供前端独立对接使用。

## 1. 功能范围

Step6 页面需要提供：

- 启动、停止在线扫描；
- 显示扫描运行状态和当前轮次；
- 显示主、从相机最近抓拍；
- 显示二维深度质量拟合图；
- 显示有效深度点数、总点数和深度范围；
- 显示重建进度和错误；
- SignalR 断线时使用 REST 状态接口临时兜底。

当前 `2目1光` 模式每轮固定采集 16 张结构光图：

| 轮内帧序号 | 内容 |
| --- | --- |
| 0、1 | 横条纹 6 px：原图、互补图 |
| 2、3 | 横条纹 12 px：原图、互补图 |
| 4、5 | 横条纹 24 px：原图、互补图 |
| 6、7 | 横条纹 48 px：原图、互补图 |
| 8、9 | 竖条纹 8 px：原图、互补图 |
| 10、11 | 竖条纹 16 px：原图、互补图 |
| 12、13 | 竖条纹 32 px：原图、互补图 |
| 14、15 | 竖条纹 64 px：原图、互补图 |

互补图为原图逐像素取反，不做偏移。前8张为横条纹，后8张为竖条纹，投影仪配置为 `MD 8`。

16张条纹完成后，后端切换为白屏并由主相机额外采集一张纹理图。纹理图不计入 `patternCount`，不参与结构光解码，只作为质量图底图和点云颜色来源。

## 2. 扫描流程

前端只调用启动/停止接口，不直接控制投影仪和相机。

后端每轮执行：

1. 投影仪发送 `B 0`；
2. 等待2秒；
3. 投影仪发送 `B 2`；
4. 发送 `T` 显示第0帧，等待光机稳定后采集主、从相机；
5. 依次发送15次 `N`，采集第1～15帧；
6. 切换白屏；
7. 主相机采集一张纹理图；
8. 解码、双目匹配并生成二维深度质量拟合图；
9. 当前轮重建结束后进入下一轮。

采集和重建严格串行，上一轮未完成时不会开始下一轮。

## 3. REST API

基础路径：

```text
/api/app/calib-scan
```

### 3.1 启动扫描

```http
POST /api/app/calib-scan/start
Content-Type: application/json
```

请求：

```json
{
  "calibProjectId": "3a225691-7159-a481-261a-da9b405668dc",
  "suppressProjectorControl": false
}
```

`2目1光` 和 `1目1光` 模式会强制使用结构光流程，`suppressProjectorControl` 应固定传 `false`。

返回 `CalibScanStatusDto`：

```json
{
  "calibProjectId": "3a225691-7159-a481-261a-da9b405668dc",
  "state": 1,
  "isRunning": true,
  "startedAt": "2026-07-28T12:00:00Z",
  "lastUpdatedAt": "2026-07-28T12:00:00Z",
  "errorMessage": null,
  "latestMetrics": null
}
```

启动前置条件：

- 项目已绑定所需的主相机、从相机和投影仪；
- 投影仪已连接；
- Step3 已保存当前16张条纹配置；
- 投影仪已下载与当前配置一致的16张图片；
- 双目标定数据完整；
- 相关设备未被其他会话占用。

设备冲突通常返回 HTTP `409`，错误码可能为：

```text
AuroraStruct3D:DeviceOccupied
```

前端应显示服务端返回的错误消息，不要自动重复启动。

### 3.2 停止扫描

```http
POST /api/app/calib-scan/stop
Content-Type: application/json
```

请求：

```json
{
  "calibProjectId": "3a225691-7159-a481-261a-da9b405668dc"
}
```

返回 `CalibScanStatusDto`。

停止操作会等待正在进行的采集/重建安全退出。按钮应在请求完成前保持加载状态，避免重复提交。

### 3.3 查询状态

```http
GET /api/app/calib-scan/status/{calibProjectId}
```

该接口用于：

- 页面首次进入时获取一次状态；
- SignalR 重连完成后同步一次状态；
- 扫描处于活动状态且 SignalR 不可用时，每1秒兜底查询。

SignalR 正常连接时不要持续轮询此接口。

## 4. 状态枚举

```ts
export enum CalibScanRunState {
  Idle = 0,
  Starting = 1,
  Running = 2,
  Stopping = 3,
  Failed = 4,
}
```

建议显示：

| 值 | 文案 | 颜色 |
| --- | --- | --- |
| 0 | 未运行 | 灰色 |
| 1 | 启动中 | 蓝色 |
| 2 | 运行中 | 绿色 |
| 3 | 停止中 | 蓝色 |
| 4 | 运行失败 | 红色 |

## 5. 状态和指标类型

```ts
export interface CalibScanStatusDto {
  calibProjectId: string
  state: CalibScanRunState
  isRunning: boolean
  startedAt: string | null
  lastUpdatedAt: string
  errorMessage: string | null
  latestMetrics: CalibScanMetricsDto | null
}

export interface CalibScanMetricsDto {
  fps: number
  depthValidRate: number
  confidence: number
  depthMapDataUri: string | null
  frameIndex: number
  timestamp: string
  roundIndex: number
  frameIndexInRound: number
  patternCount: number
  isCrosshairDetected: boolean
}
```

字段说明：

- `roundIndex`：当前轮次，从1开始；
- `frameIndexInRound`：当前轮内条纹帧序号，范围0～15；
- `patternCount`：当前固定为16；
- `frameIndex`：累计条纹帧序号；
- `depthValidRate`、`confidence`、`depthMapDataUri`：旧字段，结构光流程中固定为 `0`、`0`、`null`；
- `isCrosshairDetected`：兼容字段，当前结构光扫描不用于完成判断。

## 6. SignalR

### 6.1 连接

Hub 地址：

```text
/signalr-hubs/camera
```

使用 MessagePack：

```ts
import * as signalR from '@microsoft/signalr'
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack'

const connection = new signalR.HubConnectionBuilder()
  .withUrl('/signalr-hubs/camera', { skipNegotiation: false })
  .withHubProtocol(new MessagePackHubProtocol())
  .withAutomaticReconnect([0, 2000, 5000, 10000])
  .build()
```

连接成功后加入项目分组：

```ts
await connection.invoke('JoinCalibScanGroupAsync', calibProjectId)
```

离开页面时：

```ts
await connection.invoke('LeaveCalibScanGroupAsync', calibProjectId)
await connection.stop()
```

重连成功后必须重新调用 `JoinCalibScanGroupAsync`，再调用一次状态查询接口进行同步。

### 6.2 扫描状态事件

事件：

```text
ReceiveCalibScanStateAsync
```

参数：

```ts
(status: CalibScanStatusDto) => void
```

收到后先校验：

```ts
status.calibProjectId === 当前项目ID
```

### 6.3 扫描指标事件

事件：

```text
ReceiveCalibScanMetricsAsync
```

参数：

```ts
(projectId: string, metrics: CalibScanMetricsDto) => void
```

### 6.4 二维深度质量图事件

事件：

```text
ReceiveDepthQualityMapAsync
```

参数顺序：

```ts
(
  projectId: string,
  pngBytes: Uint8Array,
  validPointCount: number,
  totalPointCount: number,
  minimumDepthMm: number,
  maximumDepthMm: number
) => void
```

生成浏览器图片：

```ts
const blob = new Blob(
  [Uint8Array.from(pngBytes).buffer],
  { type: 'image/png' }
)
const imageUrl = URL.createObjectURL(blob)
```

每次更新前必须执行：

```ts
URL.revokeObjectURL(previousImageUrl)
```

质量图含义：

- 底图：整平后的主相机白光纹理图；
- 质量颜色：80%不透明度，保留20%纹理底图；
- 红色：没有有效深度；
- 黄色到绿色：有效像素占比由低到高；
- 图像可能裁剪为投影仪实际覆盖区域，不保证与完整相机画幅尺寸一致。

有效率：

```ts
const validRate =
  totalPointCount > 0 ? validPointCount / totalPointCount : 0
```

### 6.5 重建状态事件

事件：

```text
ReceivePointCloudStatusAsync
```

Step6只需使用：

```ts
interface PointCloudStatus {
  calibProjectId: string
  progressMessage?: string | null
  errorMessage?: string | null
}
```

`progressMessage` 显示在质量图上方，`errorMessage` 使用红色提示。单轮重建失败不应导致前端清除上一张有效质量图。

## 7. 主从相机图像流

相机图像不通过 SignalR 传输，而是通过 HTTP Multipart 流：

```http
GET /api/streaming/calibration/{projectId}/cameras/{cameraRole}/preview
```

`cameraRole`：

```ts
enum CalibScanCameraRole {
  Main = 0,
  Secondary = 1,
}
```

示例：

```text
/api/streaming/calibration/3a225691-7159-a481-261a-da9b405668dc/cameras/0/preview
/api/streaming/calibration/3a225691-7159-a481-261a-da9b405668dc/cameras/1/preview
```

响应为持续的 Multipart 数据。前端需要：

1. 从响应 `Content-Type` 解析 `boundary`；
2. 按分段头中的 `Content-Length` 读取完整图片；
3. 图片 MIME 类型优先读取响应头 `X-Frame-Content-Type`；
4. 用 `Blob` 和 `URL.createObjectURL` 显示；
5. 新帧到达时释放旧 URL；
6. 页面卸载时通过 `AbortController.abort()` 终止两路流。

主相机在每轮结束时会更新为白光纹理帧，从相机保持最近一张条纹抓拍。

## 8. 推荐页面状态

页面进入：

1. 建立 SignalR；
2. 加入项目分组；
3. 查询一次 REST 状态；
4. 启动主、从相机 Multipart 流；
5. 根据服务端状态恢复按钮和运行标记。

点击开始：

1. 禁用开始、停止按钮；
2. 调用启动接口；
3. 清空旧质量图、旧点数和旧错误；
4. 保存返回状态；
5. 恢复按钮状态。

扫描运行中：

- SignalR正常：完全依赖推送；
- SignalR断开：仅活动状态下每1秒查询状态；
- SignalR重连：重新入组并立即同步一次状态。

点击停止：

1. 禁用按钮；
2. 调用停止接口并等待完成；
3. 停止状态轮询；
4. 相机图像流可继续保留，用于显示最后抓拍。

页面卸载：

- 停止轮询；
- 终止Multipart流；
- 离开SignalR项目分组；
- 停止SignalR连接；
- 释放主图、从图、质量图的所有Blob URL。

## 9. 常见错误处理

| 场景 | 前端处理 |
| --- | --- |
| HTTP 409 / `DeviceOccupied` | 显示设备被占用，不自动重试启动 |
| 投影仪未连接 | 显示服务端消息，引导到设备管理页 |
| Step3条纹数量不是16 | 提示重新保存Step3并重新下载条纹 |
| SignalR断线 | 显示“重连中”，活动状态下开启1秒REST轮询 |
| Multipart流断开 | 保留最后一帧，可延迟后重新建立该路流 |
| 单轮重建失败 | 显示重建错误，保留上一张质量图 |
| 扫描状态Failed | 显示 `errorMessage`，允许用户重新启动 |

## 10. 当前参考代码

- 前端API：`src/api/calib-scan.ts`
- Step6页面：`src/views/calibration/CalibStep6OnlineScan.vue`
- 后端服务：`Calibration/CalibScanAppService.cs`
- REST DTO：`Calibration/Dtos/CalibScanDtos.cs`
- SignalR Hub：`Hubs/CameraHub.cs`
- HTTP图像流：`Endpoints/CameraStreamingEndpoints.cs`

