# 雷赛 iCL-RS 电机 SignalR 接口文档

> 适用版本：AuroraStruct3D
> Hub 路由：`/signalr-hubs/leisai-motor`
> 协议：**JSON**（标准 ASP.NET Core SignalR，无 MessagePack）
> 鉴权：`[AllowAnonymous]`，无需 Token，连接后被动接收推送，**无客户端主动调用方法**

---

## 目录

1. [架构说明](#1-架构说明)
2. [连接方式](#2-连接方式)
3. [服务端推送事件](#3-服务端推送事件)
    - [ReceiveLeisaiMotorStateAsync](#31-receiveleisaimotorstatedtoasync)
    - [ReceiveLeisaiTraceAsync](#32-receiveleisaitraceasync)
4. [数据类型定义](#4-数据类型定义)
5. [采样开关（REST API）](#5-采样开关rest-api)
6. [前端 Store 使用方式](#6-前端-store-使用方式)
7. [完整使用示例](#7-完整使用示例)
8. [常见问题](#8-常见问题)

---

## 1. 架构说明

```
┌─────────────────────────────────────────────────────┐
│          LeisaiSamplerHostedService（后台服务）        │
│  采样周期：250 ms（约 4 Hz）                          │
│  写库节流：每 12 次成功采样写一次数据库（≈3 秒/次）     │
│  轨迹窗口：每轴最多保留 1200 个点（滑动窗口）           │
└────────────────────┬────────────────────────────────┘
                     │ 每次采样成功后
                     ▼
┌────────────────────────────────────────────────────┐
│          SignalRLeisaiMotorNotifier（推送器）         │
│  推送两个事件：                                      │
│  1. ReceiveLeisaiMotorStateAsync（状态快照）          │
│  2. ReceiveLeisaiTraceAsync（完整轨迹序列）           │
└────────────────────┬───────────────────────────────┘
                     │ WebSocket
                     ▼
           /signalr-hubs/leisai-motor
                     │
          ┌──────────┴──────────┐
          │  前端 useLeisaiMotorStore  │
          │  按 axisId 分发数据         │
          └───────────────────────────┘
```

**关键约束**：

- 采样服务会为所有已注册的 LeisaiIclRs 轴持续采样
- 只有当某轴的 `IsPollingEnabled = true` 时，该轴的数据才会被采集并推送
- 同一 RS485 端口上的多个轴**串行**采样（端口级 Semaphore 保护），不同端口**并行**采样

---

## 2. 连接方式

### 2.1 最小连接示例

```typescript
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/signalr-hubs/leisai-motor")
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();

await connection.start();
```

### 2.2 连接选项说明

| 选项                     | 推荐值          | 说明                             |
| ------------------------ | --------------- | -------------------------------- |
| `skipNegotiation`        | `false`（默认） | 支持 HTTP 长轮询降级，保持兼容性 |
| `withAutomaticReconnect` | 必须设置        | 网络抖动时自动重连               |
| 协议                     | 默认 JSON       | 无需配置 MessagePackHubProtocol  |
| Token                    | 不需要          | Hub 配置为 `[AllowAnonymous]`    |

---

## 3. 服务端推送事件

### 3.1 `ReceiveLeisaiMotorStateAsync`

**触发时机**：每次采样周期（250 ms）完成后，无论成功还是失败都推送一次。
**数据范围**：所有 `IsPollingEnabled = true` 的轴，同时广播给所有已连接的客户端。

```typescript
connection.on(
    "ReceiveLeisaiMotorStateAsync",
    (state: LeisaiStateSnapshotDto) => {
        // 用 axisId 区分是哪个轴的数据
        console.log(state.axisId, state.actualPosition, state.effectiveSpeed);
    },
);
```

> **注意**：后端 C# 使用 PascalCase，经 JSON 序列化后前端收到的是 **camelCase**（如 `AxisId` → `axisId`）。

**字段说明**见 [4.1 LeisaiStateSnapshotDto](#41-leisaistatesnapshot dto)。

---

### 3.2 `ReceiveLeisaiTraceAsync`

**触发时机**：每次采样**成功**后推送（失败采样不追加轨迹）。
**数据内容**：该轴的**完整滑动窗口**（最多 1200 个历史点，从旧到新排列）。

```typescript
connection.on("ReceiveLeisaiTraceAsync", (trace: LeisaiTraceDto) => {
    // trace.actualPositions / commandPositions / speeds 长度始终相等
    // 可直接用于渲染 3D 位置-速度-时间 相位轨迹图
    const n = trace.actualPositions.length;
    console.log(`轴 ${trace.axisId} 轨迹点数：${n}`);
});
```

**字段说明**见 [4.2 LeisaiTraceDto](#42-leisaitracedto)。

---

## 4. 数据类型定义

### 4.1 `LeisaiStateSnapshotDto`

```typescript
interface LeisaiStateSnapshotDto {
    // ── 基础信息 ──────────────────────────────────────────────────────
    /** 电机轴 ID（UUID，用于与其他 API 对接） */
    axisId: string;
    /** RS485 从机地址（1~247） */
    slaveId: number;
    /** 采样时间戳（UTC，毫秒） */
    timestampMs: number;
    /** 本次采样耗时（毫秒） */
    elapsedMs: number;
    /** 本次采样是否成功（任意子读取失败置 false） */
    isSuccess: boolean;
    /** 失败原因（成功时为 null） */
    failureReason: string | null;

    // ── 运行状态（寄存器 0x1003 状态字） ──────────────────────────────
    /** 状态字原值（可用于调试） */
    statusWord: number;
    /** bit0：故障 */
    isFault: boolean;
    /** bit1：已使能 */
    isEnabled: boolean;
    /** bit2：运行中（电机正在执行运动） */
    isRunning: boolean;
    /** bit3：指令完成 */
    isCommandDone: boolean;
    /** bit4：路径完成 */
    isPathDone: boolean;
    /** bit5：回零完成 */
    isHomeDone: boolean;

    // ── 触发状态（寄存器 0x6002 触发字） ─────────────────────────────
    /** 触发字原值 */
    triggerWord: number;
    /** 触发模式描述："Idle" | "Homing" | "PR0".."PR15" | "JOG" | "Unknown" */
    triggerMode: string;

    // ── 位置（寄存器 0x602A~0x602D，32 位有符号脉冲数） ──────────────
    /** 命令位置（脉冲） */
    commandPosition: number;
    /** 实际位置（脉冲） */
    actualPosition: number;

    // ── 速度 ─────────────────────────────────────────────────────────
    /** 当前生效速度（rpm） */
    effectiveSpeed: number;
    /** 速度来源描述："Stop" | "Homing" | "PR0".."PR15" | "JOG" */
    speedSource: string;

    // ── 母线电压 ─────────────────────────────────────────────────────
    /** 母线电压（V），= 寄存器 0x0177 / 10.0 */
    busVoltageVolt: number;

    // ── IO 状态 ──────────────────────────────────────────────────────
    /** 输入 IO 位图（寄存器 0x0179，bit0..bit6 = DI1..DI7） */
    inputIoBitmap: number;
    /** 输出 IO 位图（寄存器 0x017B，bit0..bit2 = DO1..DO3） */
    outputIoBitmap: number;

    // ── 故障码 ───────────────────────────────────────────────────────
    /** 当前故障码（寄存器 0x2203） */
    currentFaultCode: number;
    /** PR 路径警告码（寄存器 0x601D） */
    prWarningCode: number;
}
```

**IO 位图解析示例**：

```typescript
// 读取 DI1 状态（bit0）
const di1Active = (state.inputIoBitmap & 0x01) !== 0;

// 读取 DO2 状态（bit1）
const do2Active = (state.outputIoBitmap & 0x02) !== 0;
```

---

### 4.2 `LeisaiTraceDto`

```typescript
interface LeisaiTraceDto {
    /** 电机轴 ID */
    axisId: string;
    /**
     * 实际位置序列（脉冲），从旧到新排列，最多 1200 点。
     * 与 commandPositions、speeds 等长。
     */
    actualPositions: number[];
    /** 指令位置序列（脉冲），从旧到新排列 */
    commandPositions: number[];
    /** 有效速度序列（rpm），从旧到新排列 */
    speeds: number[];
}
```

**用于 3D 轨迹图的 Plotly 示例**：

```typescript
import Plotly from "plotly.js-dist-min";

function renderTrace(el: HTMLDivElement, trace: LeisaiTraceDto) {
    const n = trace.actualPositions.length;
    const z = Array.from({ length: n }, (_, i) => i); // 时间序号轴

    Plotly.newPlot(
        el,
        [
            {
                type: "scatter3d",
                mode: "lines",
                name: "实际位置",
                x: trace.actualPositions,
                y: trace.speeds,
                z,
                line: { color: "#87c3ff", width: 3 },
            },
            {
                type: "scatter3d",
                mode: "lines",
                name: "指令位置",
                x: trace.commandPositions,
                y: trace.speeds,
                z,
                line: { color: "#f9cc83", width: 3 },
            },
        ],
        {
            scene: {
                xaxis: { title: "位置(脉冲)" },
                yaxis: { title: "速度(rpm)" },
                zaxis: { title: "时间序号" },
            },
        },
    );
}
```

---

## 5. 采样开关（REST API）

SignalR 数据推送依赖后台采样服务，采样服务仅在对应轴的 **采样开关打开** 时才会采集并推送数据。

| 接口                                               | 方法 | 说明                              |
| -------------------------------------------------- | ---- | --------------------------------- |
| `GET /api/app/leisai-motor/{id}/sampling-state`    | GET  | 查询当前采样是否启用              |
| `POST /api/app/leisai-motor/{id}/enable-sampling`  | POST | 开启采样（开始推送 SignalR 数据） |
| `POST /api/app/leisai-motor/{id}/disable-sampling` | POST | 关闭采样（停止推送 SignalR 数据） |

```typescript
import * as leisaiApi from "@/api/leisai";

// 查询采样状态
const { isPollingEnabled } = await leisaiApi.getSamplingState(axisId);

// 开启采样
await leisaiApi.enableSampling(axisId);

// 关闭采样
await leisaiApi.disableSampling(axisId);
```

---

## 6. 前端 Store 使用方式

项目内已封装 `useLeisaiMotorStore`（Pinia Store），推荐直接使用，无需手动管理 SignalR 连接。

### 6.1 Store 提供的能力

```typescript
const store = useLeisaiMotorStore();

// 连接管理（引用计数，多个组件共用同一连接）
await store.acquireHub(); // 组件挂载时调用
await store.releaseHub(); // 组件卸载时调用，引用计数归零时才真正断开

// 响应式数据（按 axisId 区分）
const state = store.getState(axisId); // computed<LeisaiStateSnapshotDto | null>
const trace = store.getTrace(axisId); // computed<LeisaiTraceDto>

// 连接状态
store.hubConnected; // ref<boolean>

// 操作日志（最近 500 条）
store.logs; // ref<{ time, level, text }[]>
```

### 6.2 在 Vue 组件中使用

```vue
<script setup lang="ts">
import { onMounted, onBeforeUnmount, computed, nextTick } from "vue";
import { useLeisaiMotorStore } from "@/stores/leisai";
import Plotly from "plotly.js-dist-min";

const props = defineProps<{ axisId: string }>();
const store = useLeisaiMotorStore();

// 订阅当前轴的实时数据
const state = store.getState(props.axisId); // 状态快照
const trace = store.getTrace(props.axisId); // 轨迹数据

onMounted(async () => {
    await store.acquireHub(); // 建立/共享 SignalR 连接
    await store.api.enableSampling(props.axisId); // 开启采样
});

onBeforeUnmount(async () => {
    await store.api.disableSampling(props.axisId);
    await store.releaseHub(); // 引用计数 -1，降为 0 时断开连接
});
</script>

<template>
    <div>
        <p>实际位置：{{ state?.actualPosition }} 脉冲</p>
        <p>当前速度：{{ state?.effectiveSpeed }} rpm</p>
        <p>母线电压：{{ state?.busVoltageVolt?.toFixed(1) }} V</p>
    </div>
</template>
```

### 6.3 引用计数机制说明

`acquireHub` / `releaseHub` 采用引用计数设计，允许多个页面/组件同时使用同一个 Hub 连接：

```
第 1 个组件 acquireHub  →  refCount = 1，新建连接
第 2 个组件 acquireHub  →  refCount = 2，复用已有连接
第 2 个组件 releaseHub  →  refCount = 1，连接保持
第 1 个组件 releaseHub  →  refCount = 0，连接断开
```

---

## 7. 完整使用示例

以下示例展示了一个完整的电机监控组件，包含状态显示和 3D 轨迹图：

```vue
<script setup lang="ts">
import { onMounted, onBeforeUnmount, ref, nextTick, watch } from "vue";
import { useLeisaiMotorStore } from "@/stores/leisai";
import Plotly from "plotly.js-dist-min";

const props = defineProps<{ axisId: string }>();
const store = useLeisaiMotorStore();
const state = store.getState(props.axisId);
const trace = store.getTrace(props.axisId);

// 3D 图表
const chartEl = ref<HTMLDivElement | null>(null);
let plotInitialized = false;

function buildTraceArrays() {
    const t = trace.value;
    const n = t.actualPositions.length;
    return {
        actualX: [...t.actualPositions],
        commandX: [...t.commandPositions],
        speedsY: [...t.speeds],
        z: Array.from({ length: n }, (_, i) => i),
    };
}

async function initPlot() {
    if (!chartEl.value) return;
    const { actualX, commandX, speedsY, z } = buildTraceArrays();
    await Plotly.newPlot(
        chartEl.value,
        [
            {
                type: "scatter3d",
                mode: "lines",
                name: "实际位置",
                x: actualX,
                y: speedsY,
                z,
                line: { color: "#87c3ff", width: 3 },
            },
            {
                type: "scatter3d",
                mode: "lines",
                name: "指令位置",
                x: commandX,
                y: speedsY,
                z,
                line: { color: "#f9cc83", width: 3 },
            },
        ],
        {
            scene: {
                xaxis: { title: "位置(脉冲)" },
                yaxis: { title: "速度(rpm)" },
                zaxis: { title: "时间序号" },
            },
            margin: { t: 24, b: 0, l: 0, r: 0 },
            paper_bgcolor: "rgba(0,0,0,0)",
        },
        { responsive: true, displayModeBar: true },
    );
    plotInitialized = true;
}

// 轨迹更新：仅替换数据，不重置相机视角
async function updatePlot() {
    if (!chartEl.value || !plotInitialized) return;
    const { actualX, commandX, speedsY, z } = buildTraceArrays();
    await Plotly.restyle(chartEl.value, {
        x: [actualX, commandX],
        y: [speedsY, speedsY],
        z: [z, z],
    });
}

// 监听轨迹变化（约 250ms 触发一次）
watch(trace, async () => {
    if (!plotInitialized) await initPlot();
    else await updatePlot();
});

onMounted(async () => {
    await store.acquireHub();
    await store.api.enableSampling(props.axisId);
    await nextTick();
    await initPlot();
});

onBeforeUnmount(async () => {
    if (chartEl.value && plotInitialized) {
        Plotly.purge(chartEl.value);
        plotInitialized = false;
    }
    await store.api.disableSampling(props.axisId);
    await store.releaseHub();
});
</script>

<template>
    <div class="space-y-4">
        <!-- 状态卡片 -->
        <div v-if="state" class="grid grid-cols-3 gap-2">
            <div>位置：{{ state.actualPosition }} 脉冲</div>
            <div>速度：{{ state.effectiveSpeed }} rpm</div>
            <div>电压：{{ state.busVoltageVolt.toFixed(1) }} V</div>
            <div>状态：{{ state.triggerMode }}</div>
            <div>故障：{{ state.isFault ? "是" : "否" }}</div>
            <div>使能：{{ state.isEnabled ? "是" : "否" }}</div>
        </div>

        <!-- 3D 轨迹图（使用 v-show 保留 DOM，切换 Tab 时不重置相机视角） -->
        <div ref="chartEl" style="height: 360px" />
    </div>
</template>
```

---

## 8. 常见问题

### Q1：连接后没有收到数据推送？

**原因**：该轴的采样开关未打开。
**解决**：先调用 `POST /api/app/leisai-motor/{id}/enable-sampling` 开启采样，或在前端调用 `store.api.enableSampling(axisId)`。

### Q2：`isSuccess = false` 时会推送什么数据？

推送包含 `isSuccess: false` 和 `failureReason` 的快照（通信错误原因）。
位置、速度等字段保持上一次成功采样的值（由 Sampler 在内存中维护），**不会被置为 0**。
**注意**：`isSuccess = false` 时**不会**追加轨迹窗口，`ReceiveLeisaiTraceAsync` 不会触发。

### Q3：`ReceiveLeisaiTraceAsync` 推送的是增量还是全量？

**全量**。每次推送都包含该轴完整的滑动窗口（最多 1200 点）。
前端收到后直接替换本地数据，无需做增量合并。

### Q4：多个浏览器 Tab 同时打开会各自采样吗？

不会。采样由后台 `LeisaiSamplerHostedService` 统一控制，与前端连接数无关。
所有 SignalR 客户端共享同一份采样数据推送（广播）。

### Q5：采样频率可以调整吗？

当前硬编码为 250 ms/次（约 4 Hz）。如需修改，调整 `LeisaiSamplerHostedService` 中的 `SamplePeriodMs` 常量并重新发布。

### Q6：使用 `v-if` 还是 `v-show` 切换含 3D 图表的 Tab？

**必须用 `v-show`**。
`v-if` 切换会销毁并重建 DOM 节点，导致 Plotly 丢失相机状态（旋转/缩放角度被重置）。
`v-show` 保留 DOM，切换 Tab 后仅调用 `Plotly.Plots.resize()` 修正尺寸即可。

### Q7：连接断开后数据还会继续更新吗？

不会。断开期间的推送数据**不会缓存**，重连后将立即收到最新一帧数据。
`withAutomaticReconnect()` 会在网络恢复后自动重连（默认重试间隔：0ms、2s、10s、30s）。
