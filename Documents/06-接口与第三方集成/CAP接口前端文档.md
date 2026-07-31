# CAP 消息总线 — 前端接口文档

> 对应页面：`/embed/cap`（Vue 组件：`CapPage.vue`）
> 生成日期：2026-05-26

---

## 目录

1. [页面路由与 Tab 切换](#一页面路由与-tab-切换)
2. [认证说明](#二认证说明)
3. [REST API](#三rest-api)
    - [获取统计数据](#1-获取统计数据)
    - [获取消息列表（分页）](#2-获取消息列表分页)
    - [消息重新入队](#3-消息重新入队)
    - [获取订阅者列表](#4-获取订阅者列表)
    - [获取节点列表](#5-获取节点列表)
4. [SignalR 实时推送](#四signalr-实时推送)
5. [TypeScript 类型定义](#五typescript-类型定义)

---

## 一、页面路由与 Tab 切换

| 路由         | 路由名称   | 说明               |
| ------------ | ---------- | ------------------ |
| `/embed/cap` | `EmbedCap` | CAP 消息仪表盘主页 |

通过 Query 参数 `tab` 切换子页：

| `?tab=`       | 说明                                  |
| ------------- | ------------------------------------- |
| `dashboard`   | 仪表盘（统计数字 + 柱形图），**默认** |
| `published`   | 发布消息列表                          |
| `received`    | 接收消息列表                          |
| `subscribers` | 订阅者列表                            |
| `nodes`       | 节点列表                              |

**示例**：`/embed/cap?tab=published`

---

## 二、认证说明

所有 REST 请求均需携带 Bearer Token（由 `httpClient` 拦截器自动注入）：

```
Authorization: Bearer <token>
```

同时携带以下标准请求头：

| 请求头                | 说明                              |
| --------------------- | --------------------------------- |
| `__tenant`            | ABP 多租户 ID（有租户时携带）     |
| `X-Client-Session-Id` | 标签页唯一会话 ID（per-tab UUID） |
| `Accept-Language`     | 当前语言（如 `zh-CN`）            |

---

## 三、REST API

基础路径：`/cap/api`

---

### 1. 获取统计数据

```
GET /cap/api/stats
```

**功能**：获取 CAP 消息的四项统计计数，用于仪表盘展示。

**响应体** `CapStats`：

| 字段               | 类型      | 颜色提示 | 说明         |
| ------------------ | --------- | -------- | ------------ |
| `publishSucceeded` | `number?` | 绿色     | 发布成功总数 |
| `publishFailed`    | `number?` | 红色     | 发布失败总数 |
| `consumeSucceeded` | `number?` | 蓝色     | 消费成功总数 |
| `consumeFailed`    | `number?` | 橙色     | 消费失败总数 |

**响应示例**：

```json
{
    "publishSucceeded": 1024,
    "publishFailed": 3,
    "consumeSucceeded": 1021,
    "consumeFailed": 3
}
```

---

### 2. 获取消息列表（分页）

```
GET /cap/api/{type}/{status}?currentPage={page}&pageSize=20
```

**功能**：分页查询发布或接收的消息列表，支持按状态筛选。

**路径参数**：

| 参数     | 可选值                                            | 说明     |
| -------- | ------------------------------------------------- | -------- |
| `type`   | `published` / `received`                          | 消息类型 |
| `status` | `succeeded` / `failed` / `delayed` / `processing` | 消息状态 |

**Query 参数**：

| 参数          | 类型     | 说明                  |
| ------------- | -------- | --------------------- |
| `currentPage` | `number` | 当前页码，从 **1** 起 |
| `pageSize`    | `number` | 每页条数，固定 **20** |

**响应体** `CapPageResult`：

| 字段        | 类型            | 说明           |
| ----------- | --------------- | -------------- |
| `pageCount` | `number?`       | 总页数         |
| `data`      | `CapMessage[]?` | 当前页消息列表 |

**`CapMessage` 字段说明**：

| 字段         | 类型                | 说明                                                        |
| ------------ | ------------------- | ----------------------------------------------------------- |
| `id`         | `number \| string?` | 消息 ID                                                     |
| `name`       | `string?`           | 消息/事件名称（如 `order.created`）                         |
| `group`      | `string?`           | 消费者组名                                                  |
| `content`    | `string?`           | 消息内容（JSON 字符串）                                     |
| `added`      | `string?`           | 入库时间（ISO 8601）                                        |
| `statusName` | `string?`           | 状态文本：`Succeeded` / `Failed` / `Delayed` / `Processing` |
| `expiresAt`  | `string?`           | 消息过期时间（ISO 8601）                                    |

**请求示例**：

```
GET /cap/api/published/failed?currentPage=1&pageSize=20
```

**响应示例**：

```json
{
    "pageCount": 2,
    "data": [
        {
            "id": 42,
            "name": "order.created",
            "group": "AuroraStruct3D",
            "content": "{\"orderId\":\"abc\"}",
            "added": "2026-05-26T08:30:00.000Z",
            "statusName": "Failed",
            "expiresAt": "2026-06-25T08:30:00.000Z"
        }
    ]
}
```

---

### 3. 消息重新入队

```
POST /cap/api/{type}/requeue
Content-Type: application/json

["<messageId>"]
```

**功能**：将指定消息重新入队（重试）。仅适用于 `statusName === 'Failed'` 的消息，UI 侧已做过滤。

**路径参数**：

| 参数   | 可选值                   | 说明     |
| ------ | ------------------------ | -------- |
| `type` | `published` / `received` | 消息类型 |

**请求体**：消息 ID 的 JSON 数组，字符串或数字均可：

```json
[42]
```

**响应**：`2xx` 表示成功，无响应体。

---

### 4. 获取订阅者列表

```
GET /cap/api/subscriber
```

**功能**：获取所有已注册的 CAP 事件订阅者信息。

**响应体** `CapSubscriber[]`：

| 字段             | 类型      | 说明                   |
| ---------------- | --------- | ---------------------- |
| `group`          | `string?` | 消费者组名             |
| `name`           | `string?` | 订阅的事件名           |
| `methodInfo`     | `string?` | 处理方法签名（含类名） |
| `implName`       | `string?` | 实现类名               |
| `serviceAddress` | `string?` | 所在服务地址           |

**响应示例**：

```json
[
    {
        "group": "AuroraStruct3D",
        "name": "order.created",
        "methodInfo": "HandleOrderCreatedAsync(OrderCreatedEvent)",
        "implName": "OrderEventHandler",
        "serviceAddress": "http://localhost:44380"
    }
]
```

---

### 5. 获取节点列表

```
GET /cap/api/nodes
```

**功能**：获取当前 CAP 集群中的节点信息。

**响应体** `CapNode[]`：

| 字段      | 类型      | 说明                |
| --------- | --------- | ------------------- |
| `name`    | `string?` | 节点名称            |
| `address` | `string?` | 节点地址（IP:端口） |

**响应示例**：

```json
[
    {
        "name": "AuroraStruct3D-HttpApi-Host",
        "address": "http://10.127.135.143:44380"
    }
]
```

---

## 四、SignalR 实时推送

### Hub 连接信息

| 项目         | 值                                                |
| ------------ | ------------------------------------------------- |
| **Hub 地址** | `/signalr-hubs/dashboard`                         |
| **认证方式** | `accessTokenFactory: () => token`（Bearer Token） |
| **自动重连** | 已启用（`withAutomaticReconnect`）                |
| **日志级别** | `Warning`                                         |

### 前端连接代码

```typescript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/signalr-hubs/dashboard", {
        accessTokenFactory: () => authStore.token ?? "",
    })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();
```

---

### 事件：`ReceiveCapStats`

**方向**：服务端 → 前端（周期广播）
**触发时机**：后端 `DashboardBroadcastService` 定时推送，所有连接到 Dashboard Hub 的客户端均会收到。

**参数**：`CapStats`（与 REST `GET /cap/api/stats` 响应结构相同）

```typescript
connection.on("ReceiveCapStats", (data: CapStats) => {
    // 实时更新仪表盘统计数字与柱形图
});
```

> **注意**：Dashboard Hub 同时广播 `ReceiveHangfireStats` 和 `ReceiveSystemMetrics` 事件。
> CAP 页已注册这两个事件的空处理器，避免 SignalR 客户端产生 `No handler registered` 警告。

---

## 五、TypeScript 类型定义

```typescript
/** CAP 消息统计 */
interface CapStats {
    publishSucceeded?: number;
    publishFailed?: number;
    consumeSucceeded?: number;
    consumeFailed?: number;
}

/** 单条 CAP 消息 */
interface CapMessage {
    id?: number | string;
    name?: string; // 事件名
    group?: string; // 消费者组
    content?: string; // 消息体（JSON 字符串）
    added?: string; // ISO 8601 入库时间
    statusName?: string; // 'Succeeded' | 'Failed' | 'Delayed' | 'Processing'
    expiresAt?: string; // ISO 8601 过期时间
}

/** 分页消息列表 */
interface CapPageResult {
    pageCount?: number;
    data?: CapMessage[];
}

/** 订阅者信息 */
interface CapSubscriber {
    group?: string;
    name?: string; // 订阅的事件名
    methodInfo?: string; // 处理方法签名
    implName?: string; // 实现类名
    serviceAddress?: string;
}

/** CAP 集群节点 */
interface CapNode {
    name?: string;
    address?: string;
}

/** 消息类型 */
type MessageType = "published" | "received";

/** 消息状态筛选值 */
type MessageStatus = "succeeded" | "failed" | "delayed" | "processing";
```

---

## 附：接口速查表

| 功能             | 方法   | 路径                                                    |
| ---------------- | ------ | ------------------------------------------------------- |
| 获取统计数据     | `GET`  | `/cap/api/stats`                                        |
| 获取发布消息列表 | `GET`  | `/cap/api/published/{status}?currentPage=N&pageSize=20` |
| 获取接收消息列表 | `GET`  | `/cap/api/received/{status}?currentPage=N&pageSize=20`  |
| 发布消息重新入队 | `POST` | `/cap/api/published/requeue`                            |
| 接收消息重新入队 | `POST` | `/cap/api/received/requeue`                             |
| 获取订阅者列表   | `GET`  | `/cap/api/subscriber`                                   |
| 获取节点列表     | `GET`  | `/cap/api/nodes`                                        |
| **SignalR 推送** | Hub    | `/signalr-hubs/dashboard` → `ReceiveCapStats`           |
