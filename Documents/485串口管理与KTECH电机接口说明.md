# 485 串口管理 & 瓴控 KTECH 电机接口说明

> 本文档面向前端开发人员，描述 485 串口管理和瓴控 KTECH 电机第三方对接的 REST API 与 SignalR 实时推送接口。
> 后端路由均基于 ABP 动态 API 约定（`/api/app/...`）。

---

## 一、485 串口管理

### 1.1 REST API — 基础路径 `/api/app/serial-port`

| 方法   | 路由                                     | 说明                               |
| ------ | ---------------------------------------- | ---------------------------------- |
| `GET`  | `/api/app/serial-port`                   | 获取串口配置分页列表               |
| `GET`  | `/api/app/serial-port/{id}`              | 获取单个串口配置                   |
| `POST` | `/api/app/serial-port/scan-system-ports` | 扫描系统物理串口并同步到数据库     |
| `PUT`  | `/api/app/serial-port/{id}`              | 更新串口配置（不可修改系统串口号） |
| `POST` | `/api/app/serial-port/{id}/connect`      | 打开串口（可临时覆盖波特率）       |
| `POST` | `/api/app/serial-port/{id}/disconnect`   | 关闭串口                           |
| `POST` | `/api/app/serial-port/{id}/send-raw`     | 发送原始帧并读取响应               |

#### 查询参数（GET 列表）

```ts
interface GetSerialPortListDto {
    filter?: string; // 按名称或串口号模糊过滤
    isEnabled?: boolean; // 按启用状态过滤
    skipCount?: number;
    maxResultCount?: number;
}
```

#### 响应 — SerialPortConfigDto

```ts
interface SerialPortConfigDto {
    id: string; // 串口配置 UUID
    displayName: string; // 显示名称（如"485总线-轴控"）
    description: string | null;
    isEnabled: boolean;
    portName: string; // 系统串口名（如 /dev/ttyS3, COM3）
    baudRate: number; // 波特率（0 表示未配置）
    dataBits: number; // 数据位（5-8，通常为 8）
    parity: SerialPortParity; // 校验位
    stopBits: SerialPortStopBits; // 停止位
    handshake: SerialPortHandshake; // 流控
    isOpen: boolean; // 当前是否已打开（运行态实时值）
    supportedBaudRates: number[]; // 可选波特率枚举
}
```

#### 枚举

```ts
enum SerialPortParity {
    None = 0,
    Odd = 1,
    Even = 2,
    Mark = 3,
    Space = 4,
}
enum SerialPortStopBits {
    None = 0,
    One = 1,
    Two = 2,
    OnePointFive = 3,
}
enum SerialPortHandshake {
    None = 0,
    XOnXOff = 1,
    RequestToSend = 2,
    RequestToSendXOnXOff = 3,
}
```

#### 打开串口 — ConnectSerialPortDto

```ts
interface ConnectSerialPortDto {
    baudRate?: number; // 不填则使用数据库中配置的波特率
}
```

#### 发送原始帧

请求体：

```ts
interface SerialPortRawSendDto {
    payload: string; // 十六进制字符串（如 "01 03 00 00 00 01"）或文本
    isHex: boolean; // payload 是否为十六进制格式
    appendNewLine: boolean; // 是否追加换行（仅文本模式有效）
    expectedResponseLength: number; // 期望接收的字节数
    timeoutMs: number; // 超时毫秒
}
```

响应体：

```ts
interface SerialPortRawResponseDto {
    sentHex: string; // 实际发送的十六进制内容
    responseHex: string; // 响应原始十六进制
    responseText: string; // 尝试 UTF-8 解码的响应文本
    responseLength: number; // 响应字节数
}
```

### 1.2 前端 API 封装 — `src/api/serial-ports.ts`

```ts
// 列表 & 详情
getSerialPortList(params)       → PagedResultDto<SerialPortConfigDto>
getSerialPort(id)               → SerialPortConfigDto
scanSystemPorts()               → SerialPortScanResultDto

// 配置修改
updateSerialPort(id, dto)       → SerialPortConfigDto

// 连接控制
connectSerialPort(id, dto)      → SerialPortConfigDto
disconnectSerialPort(id)        → SerialPortConfigDto

// 原始帧收发
sendSerialPortRaw(id, dto)      → SerialPortRawResponseDto
```

---

## 二、瓴控 KTECH 电机第三方对接

### 2.1 REST API — 基础路径 `/api/app/ktech-motor`

所有接口均以 `/{id}/` 定位具体电机轴（UUID），对应数据库中 `MotorAxis` 实体。

#### 产品信息 Tab

| 方法   | 路由                 | 说明                         | 协议命令 |
| ------ | -------------------- | ---------------------------- | -------- |
| `GET`  | `/{id}/device-type`  | 读取设备类型代码             | CMD 0x1F |
| `GET`  | `/{id}/product-info` | 读取产品信息（同步回写实体） | CMD 0x12 |
| `POST` | `/{id}/connect`      | 连接设备                     | CMD 0x10 |
| `POST` | `/{id}/disconnect`   | 断开设备                     | CMD 0x11 |
| `POST` | `/{id}/reboot`       | 重启设备                     | CMD 0x07 |

响应 — `KtechProductInfoDto`：

```ts
interface KtechProductInfoDto {
    deviceTypeCode: number; // 设备类型码（如 MS=8209）
    deviceTypeName: string; // 类型名称（"MS"/"MF"/"MG"/"MGE"/"MH"）
    driverName: string; // 驱动器名称（≤20字节 ASCII）
    motorName: string; // 电机名称
    chipId: string; // 芯片 ID（十六进制）
    hardwareVersion: string; // 硬件版本（如 "V1.2"）
    motorVersion: string; // 电机固件版本
    firmwareVersion: string; // 驱动器固件版本
}
```

#### 标定 Tab

| 方法   | 路由                        | 说明           | 协议命令 |
| ------ | --------------------------- | -------------- | -------- |
| `GET`  | `/{id}/calib`               | 读取标定参数   | CMD 0x16 |
| `PUT`  | `/{id}/calib`               | 写入标定参数   | CMD 0x17 |
| `POST` | `/{id}/align-motor-encoder` | 对齐电机编码器 | CMD 0x18 |
| `POST` | `/{id}/set-encoder-zero`    | 设置编码器零点 | CMD 0x19 |
| `POST` | `/{id}/reset-calib`         | 重置标定参数   | CMD 0x4A |

响应 / 请求体 — `KtechCalibDto`：

```ts
interface KtechCalibDto {
    motorPoles: number; // 电机极对数
    encoderType: number; // 编码器类型索引
    encoderPos: number; // 编码器位置（0=Normal, 1=Reverse）
    motorPhaseSequence: number; // 电机相序（0=Normal, 1=Reverse）
    motorEncoderAlignBias: number; // 对齐偏置（u32）
    motorEncoderAlignRatio: number; // 对齐比率（u16）
    motorEncoderAlignVoltage: number; // 对齐电压（0.01V/LSB）
    motorEncoderAlignFlag: number;
    encoderOffset: number; // 编码器零点偏置（u32）
    encoderOffsetFlag: number;
}
```

#### 设置 Tab

| 方法   | 路由                    | 说明                    | 协议命令 |
| ------ | ----------------------- | ----------------------- | -------- |
| `GET`  | `/{id}/setting`         | 读取设置参数            | CMD 0x14 |
| `PUT`  | `/{id}/setting`         | 写入设置参数到 RAM      | CMD 0x15 |
| `POST` | `/{id}/persist-setting` | 将 RAM 参数持久化到 ROM | CMD 0x44 |
| `POST` | `/{id}/reset-setting`   | 重置设置参数            | CMD 0x48 |
| `POST` | `/{id}/write-pid-ram`   | 实时写入 PID 到 RAM     | CMD 0x31 |

`KtechSettingDto` 主要字段（完整定义见源码）：

```ts
interface KtechSettingDto {
    driverId: number; // 驱动 ID（从机地址）
    busType: number; // 0=无, 1=RS485, 2=CAN
    rs485BaudRate: number; // RS485 波特率索引
    spinDirection: number; // 旋转方向（0=Normal, 1=Reverse）
    // 保护参数（欠/过压、温度、过流、短路、失速、失控）
    protectUnderVoltageEnable: number;
    protectUnderVoltage: number; // 0.01V/LSB
    protectOverVoltageEnable: number;
    protectOverVoltage: number;
    protectMotorTempEnable: number;
    protectMotorTemp: number; // ℃
    protectOverCurrentEnable: number;
    protectOverCurrent: number; // mA
    // PID 参数（角度/速度/电流 各 Kp/Ki/Kd）
    anglePidKp: number;
    anglePidKi: number;
    anglePidKd: number;
    speedPidKp: number;
    speedPidKi: number;
    speedPidKd: number;
    currentPidKp: number;
    currentPidKi: number;
    currentPidKd: number;
    // 限制参数
    maxTorque: number; // mA（MS 型为功率 W）
    maxSpeed: number; // 0.01dps/LSB
    maxAngle: number; // 0.01°/LSB
    speedRamp: number; // dps/s
}
```

`KtechWritePidRamInputDto`：

```ts
interface KtechWritePidRamInputDto {
    angleKp: number;
    angleKi: number;
    angleKd: number;
    speedKp: number;
    speedKi: number;
    speedKd: number;
    currentKp: number;
    currentKi: number;
    currentKd: number;
}
```

#### 基础控制 Tab

| 方法   | 路由                         | 说明                          | 协议命令 |
| ------ | ---------------------------- | ----------------------------- | -------- |
| `POST` | `/{id}/motor-on`             | 电机开启                      | CMD 0x88 |
| `POST` | `/{id}/motor-off`            | 电机关闭                      | CMD 0x80 |
| `POST` | `/{id}/motor-stop`           | 电机停止                      | CMD 0x81 |
| `POST` | `/{id}/motor-restore`        | 电机恢复                      | CMD 0x89 |
| `POST` | `/{id}/brake?release={bool}` | 制动控制（release=true 释放） | CMD 0x8C |
| `POST` | `/{id}/clear-loops`          | 清除电机圈数                  | CMD 0x93 |
| `POST` | `/{id}/set-motor-zero-ram`   | 设置电机零点到 RAM            | CMD 0x95 |
| `POST` | `/{id}/clear-error`          | 清除错误标志                  | CMD 0x9B |

#### 运动控制

`POST /{id}/motion`，请求体 `KtechMotionInputDto`：

```ts
enum KtechMotionMode {
    Open = 0, // 开环（CMD 0xA0），s16 电压
    TorqueOrPower = 1, // 力矩/功率（CMD 0xA1）
    Speed = 2, // 速度（CMD 0xA2），s32，0.01dps/LSB
    MultiAngle = 3, // 多圈绝对角度（CMD 0xA3），s64，0.01°/LSB
    MultiAngleWithSpeed = 4, // 多圈绝对角度+速度（CMD 0xA4）
    SingleAngle = 5, // 单圈角度（CMD 0xA5）
    SingleAngleWithSpeed = 6, // 单圈角度+速度（CMD 0xA6）
    IncrementAngle = 7, // 增量角度（CMD 0xA7），s32
    IncrementAngleWithSpeed = 8, // 增量角度+速度（CMD 0xA8）
}

interface KtechMotionInputDto {
    mode: KtechMotionMode;
    voltage: number; // Mode=Open 时有效（s16）
    torqueOrPower: number; // Mode=TorqueOrPower 时有效（s16，mA 或 W）
    speedCentidps: number; // 速度限制（0.01dps/LSB，u32）
    speedSignedCentidps: number; // Mode=Speed 时使用（有符号）
    angleCentideg: number; // 多圈/增量角度（0.01°/LSB，s64/s32）
    singleAngleCentideg: number; // 单圈角度（0.01°/LSB，u32）
    direction: number; // 单圈方向（0/1）
}
```

响应 `KtechMotionResponseDto`（结构同 State2）：

```ts
interface KtechMotionResponseDto {
    motorTemperature: number; // ℃
    torqueOrPower: number; // mA 或 W
    speed: number; // dps
    encoderValue: number; // 编码器原始值（u16）
}
```

#### 状态单次读取（REST，按需触发）

| 方法  | 路由                 | 说明                               | 协议命令 |
| ----- | -------------------- | ---------------------------------- | -------- |
| `GET` | `/{id}/state-once`   | 读取状态1（温度/电压/电流/错误位） | CMD 0x9A |
| `GET` | `/{id}/state3`       | 读取状态3（三相电流）              | CMD 0x9D |
| `GET` | `/{id}/multi-angle`  | 读取多圈角度（0.01°）              | CMD 0x92 |
| `GET` | `/{id}/single-angle` | 读取单圈角度（0.01°）              | CMD 0x94 |

#### 固件升级

`POST /{id}/upload-firmware`，Content-Type: `multipart/form-data`，文件字段名 `file`。
流程：后端先发 CMD 0xBB BeginIap，再走 Ymodem 协议传输；升级进度通过 SignalR 实时推送。

---

### 2.2 SignalR — KTECH 电机实时推送

**Hub 地址：** `ws(s)://{host}/signalr-hubs/ktech-motor`
**认证：** 允许匿名（`[AllowAnonymous]`），无需携带 Token。
**协议：** JSON（WebSocket + LongPolling 自动协商）。
**连接方向：** 仅服务端→客户端推送，客户端无需调用任何方法。

#### 第三方接入前必须先明确的规则

1. **当前 Hub 是广播模型，不是按轴订阅模型。** 服务端会把所有已采样轴的状态广播给所有已连接客户端，客户端必须自己按 `axisId` 过滤。
2. **同一条 485 总线上串两个电机是允许的。** 只要两个设备的 `slaveId` 不同，服务端就会把它们识别为两条独立 `MotorAxis`。
3. **第三方业务主键请使用 `axisId`。** `axisId` 是系统内唯一标识，适合作为前端缓存键、页面路由参数、升级任务归属键。
4. **总线层设备键使用 `serialPortConfigId + slaveId` 或 `portName + slaveId`。** 仅用 `portName` 无法区分同总线的两个电机，仅用 `slaveId` 也无法区分不同总线上的同地址设备。
5. **不要按消息到达顺序推断“当前是哪台电机”。** 两台电机的状态推送会交错出现，必须读取消息体内的 `axisId` 或 `slaveId`。
6. **固件升级进度同样是广播。** 收到 `ReceiveKtechUpgradeProgressAsync` 后，也必须按 `axisId` 归属到对应升级任务。

#### 推荐接入步骤

1. 先调用 `GET /api/app/motor-device` 或 `GET /api/app/motor-device/{id}` 拉取轴列表，拿到 `id`、`serialPortConfigId`、`portName`、`slaveId`、`brand`。
2. 在本地建立 `Map<axisId, AxisState>`，把 `axisId` 作为唯一缓存键。
3. 再连接 `/signalr-hubs/ktech-motor`，收到状态后按 `axisId` 更新对应缓存。
4. 页面如果只展示一台电机，进入页面时先明确当前 `axisId`，收到其他轴的广播消息直接忽略。
5. 如需执行基础控制、回零、使能等操作，调用 MotorDevice REST；如需产品信息、标定、设置、升级，调用 KTECH REST。

#### 事件 1 — 实时状态快照

**事件名：** `ReceiveKtechMotorStateAsync`
**推送频率：** 约 4 Hz（250 ms/次），由后台 Sampler 服务周期采集。

```ts
interface KtechStateSnapshotDto {
    axisId: string; // 电机轴 UUID（用于前端按轴分发）
    slaveId: number; // RS485 从机地址
    timestampMs: number; // 采样时间戳（UTC，ms）
    elapsedMs: number; // 本次采样耗时（ms）
    isSuccess: boolean; // 采样是否成功
    failureReason: string | null;

    // State1（CMD 0x9A）
    motorTemperature: number; // 电机温度（℃，sbyte）
    busVoltage: number; // 母线电压（V）
    busCurrent: number; // 母线电流（A）
    errorFlags: KtechErrorFlagsDto;

    // State2（CMD 0x9C）
    torqueOrPower: number; // 力矩 mA 或功率 W（s16）
    speed: number; // 速度（dps，s16）
    encoderValue: number; // 编码器原始值（u16）

    // 多圈角度（CMD 0x92）
    multiTurnAngle: number; // 多圈角度，单位 °
    singleTurnAngleCentideg: number; // 单圈角度，0.01°单位，范围 0..35999
}

interface KtechErrorFlagsDto {
    underVoltage: boolean;
    overVoltage: boolean;
    driverOverTemp: boolean;
    motorOverTemp: boolean;
    overCurrent: boolean;
    shortCircuit: boolean;
    stall: boolean;
    lostInput: boolean;
    raw: number; // 原始字节（bit 位图）
}
```

> 说明：
>
> - `axisId` 是 Aurora 系统分配的轴 UUID，第三方对接时应优先使用它。
> - `slaveId` 是 485 总线上的从机地址，适合和硬件布线、串口抓包对应。
> - `multiTurnAngle` 已是角度值，不是原始寄存器字。
> - `singleTurnAngleCentideg` 是单圈角度，单位为 `0.01°`，例如 `12345` 表示 `123.45°`。
> - 当 `isSuccess=false` 时，本条状态仍可能被推送，第三方应根据 `failureReason` 标记该轴本次采样失败，而不是把消息当成另一台电机的数据。

#### 事件 2 — 固件升级进度

**事件名：** `ReceiveKtechUpgradeProgressAsync`
**触发时机：** 调用 `POST /{id}/upload-firmware` 后，升级过程中持续推送。

```ts
enum KtechUpgradeStage {
    Started = 0,
    WaitingHandshake = 1,
    HeaderSent = 2,
    Transferring = 3,
    Finalizing = 4,
    Completed = 5,
    Failed = 99,
}

interface KtechUpgradeProgressDto {
    axisId: string; // 电机轴 UUID
    stage: KtechUpgradeStage;
    bytesSent: number; // 已发送字节数
    totalBytes: number; // 固件总字节数
    percent: number; // 进度百分比（0-100）
    message: string | null; // 提示信息或失败原因
    errorStep: number | null; // 仅 stage=Failed 时有效（errorType 1-10）
}
```

#### 双电机同总线时的消息分流示例

假设现在 `COM3` 上串了两台瓴控电机：

| 业务轴 | 串口 | 从机地址 | axisId    |
| ------ | ---- | -------- | --------- |
| 转台轴 | COM3 | 1        | `8b7d...` |
| 升降轴 | COM3 | 2        | `3f21...` |

那么同一个 `/signalr-hubs/ktech-motor` 连接里，客户端会交替收到两种状态：

```json
{ "axisId": "8b7d...", "slaveId": 1, "speed": 120, "isSuccess": true }
{ "axisId": "3f21...", "slaveId": 2, "speed": 0, "isSuccess": true }
```

正确做法：

- 页面只看转台轴时，只处理 `axisId === "8b7d..."` 的消息。
- 做总览页时，按 `axisId` 分别更新两张状态卡片。
- 做硬件诊断时，再结合 `portName + slaveId` 反查实际是哪一台物理设备。

错误做法：

- 只按 `portName=COM3` 判断消息归属。
- 假设“最近一条消息就是当前选中的电机”。
- 升级 A 轴时未按 `axisId` 过滤，结果把 B 轴的升级日志也显示到 A 轴页面。

#### 前端订阅示例（第三方最小可用版）

```ts
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/signalr-hubs/ktech-motor", { skipNegotiation: false })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();

const stateByAxis = new Map<string, KtechStateSnapshotDto>();
const upgradeByAxis = new Map<string, KtechUpgradeProgressDto[]>();
const currentAxisId = "8b7d..."; // 当前页面绑定的业务轴 ID

// 订阅实时状态
connection.on("ReceiveKtechMotorStateAsync", (state: KtechStateSnapshotDto) => {
    stateByAxis.set(state.axisId, state);

    // 如果页面只关心某一台电机，务必按 axisId 过滤
    if (state.axisId !== currentAxisId) {
        return;
    }

    console.log("当前轴状态", state);
});

// 订阅升级进度
connection.on(
    "ReceiveKtechUpgradeProgressAsync",
    (progress: KtechUpgradeProgressDto) => {
        const list = upgradeByAxis.get(progress.axisId) ?? [];
        list.push(progress);
        upgradeByAxis.set(progress.axisId, list);

        if (progress.axisId === currentAxisId) {
            console.log("当前轴升级进度", progress.percent);
        }
    },
);

await connection.start();
```

#### 是否需要携带 `clientSessionId`

- KTECH SignalR Hub 当前**不要求**在连接 URL 上附带 `clientSessionId`。
- 但 KTECH / MotorDevice 的 REST 控制接口会通过请求头 `X-Client-Session-Id` 做设备独占会话检查，避免多个标签页同时操作同一台电机。
- 第三方如果自己封装 HTTP 客户端，建议为每个浏览器标签页生成一个固定 UUID，并在所有 REST 请求头里带上：

```http
X-Client-Session-Id: 2d8fa5c8-2a39-4f30-8d8f-8cb7bb0f4a31
```

- 如果不带这个头，实时订阅通常不受影响，但并发控制能力会变弱，后续也不利于排查“谁占用了电机”。

> 封装好的 Pinia Store 见 `src/stores/ktech.ts`，已实现引用计数 `acquireHub` / `releaseHub`，多个组件共享同一连接。

---

## 三、485 电机设备管理（MotorDevice）

> 此章节描述 485 总线上**通用**电机设备管理接口（同时支持瓴控 KTECH 与雷赛 iCL-RS），适用于设备扫描、列表展示、基础控制与 Modbus 调试。
> 对瓴控品牌的**专属调试**（产品信息 / 标定 / 设置 / PID / 固件升级）请使用第二章 KTECH 接口。

### 3.1 REST API — 基础路径 `/api/app/motor-device`

| 方法   | 路由                                                               | 说明                                                   |
| ------ | ------------------------------------------------------------------ | ------------------------------------------------------ |
| `GET`  | `/api/app/motor-device`                                            | 获取电机轴分页列表                                     |
| `GET`  | `/api/app/motor-device/{id}`                                       | 获取单个电机轴详情                                     |
| `POST` | `/api/app/motor-device/scan-devices`                               | 扫描串口与从机地址段，同步发现并入库（进度走 SignalR） |
| `PUT`  | `/api/app/motor-device/{id}`                                       | 更新电机轴基础信息（名称/描述/启用/型号）              |
| `POST` | `/api/app/motor-device/{id}/set-rotation-angle-range`              | 设置旋转角度最小/最大值（瓴控单圈角度，0.01°）         |
| `POST` | `/api/app/motor-device/{id}/set-rotation-angle-limit-from-current` | 读取当前单圈角度并写入指定边界                         |
| `POST` | `/api/app/motor-device/{id}/refresh-status`                        | 从设备实时刷新状态并入库                               |
| `POST` | `/api/app/motor-device/{id}/enable`                                | 使能电机                                               |
| `POST` | `/api/app/motor-device/{id}/disable`                               | 去使能电机                                             |
| `POST` | `/api/app/motor-device/{id}/move-absolute`                         | 绝对位置运动                                           |
| `POST` | `/api/app/motor-device/{id}/move-relative`                         | 相对位置运动                                           |
| `POST` | `/api/app/motor-device/{id}/stop`                                  | 减速停止                                               |
| `POST` | `/api/app/motor-device/{id}/emergency-stop`                        | 急停                                                   |
| `POST` | `/api/app/motor-device/{id}/home`                                  | 回零                                                   |
| `POST` | `/api/app/motor-device/{id}/clear-fault`                           | 清除故障                                               |
| `POST` | `/api/app/motor-device/{id}/read-holding-registers`                | 读保持寄存器（雷赛 Modbus RTU 调试）                   |
| `POST` | `/api/app/motor-device/{id}/write-single-register`                 | 写单个保持寄存器（雷赛 Modbus RTU 调试）               |

### 3.2 DTO 定义

#### 列表查询 — GetMotorAxisListDto

```ts
interface GetMotorAxisListDto {
    filter?: string; // 按名称、串口或从机地址模糊过滤
    isEnabled?: boolean; // 按启用状态过滤
    refreshHardware?: boolean; // 是否同步从硬件刷新状态，默认 true
    skipCount?: number;
    maxResultCount?: number;
    sorting?: string;
}
```

#### 响应 — MotorAxisDto

```ts
interface MotorAxisDto {
    id: string; // 电机轴 UUID
    name: string; // 轴名称
    axisIndex: number; // 轴序号（排序用）
    description: string | null;
    isEnabled: boolean; // 是否启用

    serialPortConfigId: string; // 关联的串口配置 UUID
    serialPortDisplayName: string; // 串口显示名
    portName: string; // 系统串口号（如 /dev/ttyS3）
    baudRate: number; // 串口当前波特率
    isSerialPortOpen: boolean; // 串口运行态是否已打开

    slaveId: number; // 从机地址
    brand: MotorBrand; // 品牌协议
    brandText: string; // 品牌显示文本
    model: string | null; // 型号

    status: MotorDeviceStatus; // 运行状态
    statusText: string; // 状态显示文本
    lastKnownPosition: number; // 当前位置（品牌相关单位）
    lastKnownSpeed: number; // 当前速度
    isHomed: boolean; // 是否已回零
    lastStatusUpdateAt: string | null; // 最后状态更新时间（ISO）

    minRotationAngle: number | null; // 旋转角度最小值（瓴控单圈，0.01°）
    maxRotationAngle: number | null; // 旋转角度最大值（瓴控单圈，0.01°）
}
```

#### 更新 — UpdateMotorAxisDto

```ts
interface UpdateMotorAxisDto {
    name: string; // 必填，<=64 字符
    description?: string | null; // <=256 字符
    isEnabled: boolean;
    model?: string | null; // <=64 字符
}
```

#### 扫描 — ScanMotorDevicesInput / ResultDto

```ts
interface ScanMotorDevicesInput {
    startSlaveId: number; // 起始从机地址，[1,32]，默认 1
    endSlaveId: number; // 结束从机地址，[1,32]，默认 32
    baudRates: number[]; // 本次扫描波特率列表；为空使用协议推荐值
    probeTimeoutMs: number; // 单次探测超时（ms），[20,2000]，默认 80
}

interface DiscoveredMotorDeviceDto {
    serialPortConfigId: string;
    portName: string;
    baudRate: number;
    slaveId: number;
    brand: MotorBrand;
    brandText: string;
    motorAxisId: string; // 数据库电机轴 ID（新建或已存在）
}

interface ScanMotorDevicesResultDto {
    triedCount: number; // 累计探测次数
    foundCount: number; // 累计发现设备数
    elapsedMs: number; // 总耗时（ms）
    items: DiscoveredMotorDeviceDto[];
}
```

> 扫描是**长耗时**接口，前端建议把 axios `timeout` 调到 10 分钟以上，并通过 SignalR 实时展示进度（见 3.5）。

#### 旋转角度限位

```ts
enum MotorRotationAngleLimitKind {
    Minimum = 0,
    Maximum = 1,
}

interface SetMotorRotationAngleRangeDto {
    minRotationAngle?: number | null; // 0.01°/LSB
    maxRotationAngle?: number | null;
}

interface SetMotorRotationAngleLimitFromCurrentDto {
    limitKind: MotorRotationAngleLimitKind;
}
```

#### 运动控制 — MoveMotorInput

```ts
interface MoveMotorInput {
    position: number; // 位置或位移量（品牌相关单位；瓴控：0.01°；雷赛：脉冲）
    speedRpm: number; // 速度 RPM，0 表示使用设备默认值
}
```

#### Modbus 寄存器调试

```ts
interface ReadHoldingRegistersInput {
    startAddress: number; // 起始寄存器地址（u16）
    quantity: number; // 读取数量，[1,64]，默认 1
}

interface WriteSingleRegisterInput {
    address: number; // 寄存器地址（u16）
    value: number; // 寄存器值（u16）
}

interface RegisterReadResultDto {
    requestHex: string; // 请求帧十六进制
    responseHex: string; // 响应帧十六进制
    values: number[]; // 寄存器值列表（u16）
}
```

### 3.3 枚举

```ts
enum MotorBrand {
    KtechKtech = 0, // 瓴控 KTECH（私有协议 CMD 0x9A，上电自动使能）
    LeisaiIclRs = 1, // 雷赛 iCL-RS（Modbus RTU，需软件使能）
}

enum MotorDeviceStatus {
    Unknown = 0, // 未知/未连接
    Offline = 1, // 离线
    Online = 2, // 在线但未使能
    Enabled = 3, // 已使能，待机中
    Moving = 4, // 运动中
    Faulted = 5, // 故障报警
}
```

### 3.4 前端 API 封装 — `src/api/motors.ts`

```ts
// 列表 & 详情
getMotorAxisList(params)                  → PagedResultDto<MotorAxisDto>
getMotorAxis(id)                          → MotorAxisDto
scanMotorDevices(dto)                     → ScanMotorDevicesResultDto

// 配置修改
updateMotorAxis(id, dto)                  → MotorAxisDto
setMotorRotationAngleRange(id, dto)       → MotorAxisDto
setMotorRotationAngleLimitFromCurrent(id, dto) → MotorAxisDto

// 状态
refreshMotorStatus(id)                    → MotorAxisDto

// 基础控制
enableMotor(id)                           → MotorAxisDto
disableMotor(id)                          → MotorAxisDto
moveMotorAbsolute(id, dto)                → MotorAxisDto
moveMotorRelative(id, dto)                → MotorAxisDto
stopMotor(id) / emergencyStopMotor(id)    → MotorAxisDto
homeMotor(id) / clearMotorFault(id)       → MotorAxisDto

// Modbus 调试
readMotorHoldingRegisters(id, dto)        → RegisterReadResultDto
writeMotorSingleRegister(id, dto)         → boolean
```

### 3.5 SignalR — 电机扫描进度推送

**Hub 地址：** `ws(s)://{host}/signalr-hubs/motor-scan`
**认证：** 允许匿名（`[AllowAnonymous]`），无需 Token。
**协议：** JSON（WebSocket + LongPolling 自动协商）。
**连接方向：** 仅服务端→客户端推送。

#### 事件 — 扫描进度

**事件名：** `ReceiveMotorScanProgressAsync`
**触发时机：** 调用 `POST /api/app/motor-device/scan-devices` 后，扫描过程中按阶段持续推送。

```ts
enum MotorScanProgressKind {
    Started = 0, // 整体扫描开始
    PortStarted = 1, // 某个串口开始扫描
    Probing = 2, // 正在以指定波特率与从机地址探测
    DeviceFound = 3, // 探测发现设备
    PortFinished = 4, // 某个串口扫描结束
    PortError = 5, // 串口打开失败或异常
    Completed = 6, // 整体扫描完成
}

interface MotorScanProgressDto {
    kind: MotorScanProgressKind;
    timestamp: string; // 事件时间戳（ISO）
    message: string; // 简短描述，可直接显示给用户

    serialPortConfigId?: string | null;
    portName?: string | null;
    baudRate?: number | null;
    slaveId?: number | null;
    brand?: string | null; // 当前尝试的电机品牌协议
    found?: boolean | null; // 当前进度对应的串口是否发现设备

    totalPorts: number; // 总串口数
    finishedPorts: number; // 已完成串口数
    triedCount: number; // 累计探测次数
    foundCount: number; // 累计发现设备数
    elapsedMs?: number | null; // 已耗时（ms）
    txHex?: string | null; // 本次探测发送帧（十六进制）
    rxHex?: string | null; // 本次探测接收帧（十六进制）
    errorMessage?: string | null; // 探测失败原因
}
```

#### 扫描结果与 KTECH 实时推送的关系

1. `/signalr-hubs/motor-scan` 只负责“发现过程”的进度广播，不推送持续运行状态。
2. 扫描完成后，服务端会把发现结果写入 `MotorAxis` 表，并生成唯一 `motorAxisId`。
3. 第三方应以扫描结果返回的 `motorAxisId` 作为后续操作入口，再去订阅 `/signalr-hubs/ktech-motor`。
4. 如果一条 485 总线上发现两台电机，扫描进度里会出现相同 `portName`、不同 `slaveId` 的两条 `DeviceFound` 事件，这是正常现象，不是重复数据。

#### 前端订阅示例（Vue 3 + Pinia）

```ts
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/signalr-hubs/motor-scan", { skipNegotiation: false })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();

const MAX_PROGRESS_ITEMS = 500;
const scanProgress: MotorScanProgressDto[] = [];

connection.on(
    "ReceiveMotorScanProgressAsync",
    (progress: MotorScanProgressDto) => {
        scanProgress.push(progress);
        // 控制最大条数，避免内存膨胀
        if (scanProgress.length > MAX_PROGRESS_ITEMS) {
            scanProgress.splice(0, scanProgress.length - MAX_PROGRESS_ITEMS);
        }
        // 整体完成后，前端可以复位 isScanning 状态
        if (progress.kind === MotorScanProgressKind.Completed) {
            // isScanning.value = false;
        }
    },
);

await connection.start();
```

> 现有 Pinia Store 见 `src/stores/motors.ts`，已实现 `startScanHub` / `stopScanHub` 与扫描进度收集、自动裁剪逻辑。

### 3.6 模块关系

```
串口配置（SerialPortConfig） ── 必须先 connect ──┐
                                                 │
                                                 ▼
                           MotorDevice 应用服务（本章）
                                ├── 通用 CRUD / 扫描入库
                                ├── 基础控制（enable/move/stop/home）
                                ├── Modbus 调试（雷赛）
                                └── 旋转角度限位
                                                 │
                  Brand = Ktech 时，可叠加使用 ──▼
                           KTECH 应用服务（第二章）
                                ├── 产品信息 / 标定 / 设置
                                ├── 实时状态 SignalR
                                └── 固件升级 SignalR
```

---

## 四、通用说明

### 错误响应格式

所有 REST 接口异常时返回标准 ABP 错误结构：

```json
{
    "error": {
        "code": "App:...",
        "message": "用户可读的错误描述"
    }
}
```

### ID 参数说明

所有 `{id}` 均为 `Guid`（UUID v4），由系统在设备扫描/注册时自动生成，前端从列表接口取得后持久化即可。

### RS485 总线与 KTECH 轴的关系

```
串口配置（SerialPortConfig）
    └── 1:N MotorAxis（电机轴）
            └── 仅当 MotorBrand = Ktech 时
                    由 KTECH AppService 操作
```

串口需先通过 `POST /serial-port/{id}/connect` 打开，电机 KTECH 接口才能正常通信。
