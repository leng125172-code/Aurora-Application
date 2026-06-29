# 工作流定义 JSON 规范

> 版本：v1.0  
> 适用：3dcamera-web 工作流编辑器 ↔ 后端存储 / 执行引擎  
> 前端实现：`src/views/project/workflow/workflowGraphSerializer.ts`

本文档定义工作流保存、回显、执行的 JSON 数据结构。前后端应严格按此规范对齐。

---

## 1. 顶层结构（WorkflowPayload）

保存与回显使用同一结构：

```json
{
  "id": "wf-001",
  "projectId": "1",
  "projectRecordId": "1",
  "name": "正面检测流程",
  "version": "v1.0.0",
  "category": "检测",
  "description": "流程说明",
  "remark": "备注",
  "updatedAt": "2026-06-27T10:00:00.000Z",
  "graphData": {
    "nodes": [],
    "edges": []
  }
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | 是 | 工作流定义 ID |
| `projectId` | string | 是 | 项目 ID |
| `projectRecordId` | string | 是 | 项目记录 ID |
| `name` | string | 是 | 流程名称 |
| `version` | string | 是 | 版本号，如 `v1.0.0` |
| `category` | string | 否 | 分类 |
| `description` | string | 否 | 描述 |
| `remark` | string | 否 | 备注 |
| `updatedAt` | string | 是 | ISO8601 时间戳 |
| `graphData` | object | 是 | 核心画布数据，见第 2 节 |

---

## 2. graphData 结构

与 LogicFlow 原生画布结构对齐：

```json
{
  "nodes": [],
  "edges": []
}
```

- **顶层 `graphData`**：仅包含画布根层节点与根层连线。
- **容器内 `innerGraphData`**：结构与顶层完全一致，支持无限层递归嵌套。

---

## 3. 节点（Node）通用规范

每个节点固定包含以下字段：

```json
{
  "id": "node_xxx",
  "type": "算子UUID | flow_container | start-node | end-node",
  "x": 120,
  "y": 300,
  "text": { "value": "节点显示名" },
  "properties": {}
}
```

| 字段 | 说明 |
|------|------|
| `id` | 节点实例唯一 ID |
| `type` | 算子类型 ID（来自节点目录）；容器持久化为 `flow_container`；流程起止为 `start-node` / `end-node` |
| `x`, `y` | 坐标。顶层为画布绝对坐标；容器内为**相对父容器中心**的坐标 |
| `text` | 可选，节点显示名称 |
| `properties` | 节点业务数据，见第 4 节 |

> **start-node / end-node 由节点目录下发**：与算子、ForLoop 等一样，起止节点也来自面板接口（见 9.5），
> 其定义项带 `isBoundary: true`，表示**单例边界节点**——全图顶层有且仅一个，编辑器自动放置、不可重复添加、不可删除，且不作为普通可拖项。前端不应再硬编码 `start-node` / `end-node` 字符串，统一以面板下发的 `id` 为准。
> 起止节点的端口绑定还承担**工作流对外 I/O 签名**职责，见第 13 节。

---

## 4. properties 字段规范

所有节点的 `properties` 采用统一结构，按职责拆分，**禁止混放**：

```json
{
  "params": {},
  "inputBindings": {},
  "outputBindings": {},
  "regionList": [],
  "annotateList": [],
  "innerGraphData": { "nodes": [], "edges": [] }
}
```

除下表列出的字段外，其余为可选；无数据时可省略或给空对象/空数组。

### 4.1 params — 算子运行参数

**来源**：后端节点目录的 `configFields`（处理设置）。

**存放内容**：阈值、开关、循环次数、合并模式、曝光等算子自身配置。

```json
"params": {
  "loop_count": 3,
  "threshold": 0.85,
  "exposure": 120,
  "mergeMode": "union",
  "referenceSize": { "width": 1920, "height": 1080 },
  "sourceType": "image",
  "projectionMode": "XY"
}
```

| 规则 | 说明 |
|------|------|
| 只放配置项 | 来自 `configFields` 的字段 |
| 禁止放端口绑定 | 端口相关数据不得写入 `params` |
| 禁止放 ROI 数组 | 分区数据写入 `regionList`，不使用 `params.roiJson` |
| 禁止放几何标注数组 | 几何数据写入 `annotateList`，不使用 `params.geometryAnnotations` |

**参数引用变量（`$var`）**：配置项的值除了字面量，还可以是变量引用哨兵 `{ "$var": "变量名" }`，表示运行期从工作流变量取值（详见第 13 节）。对字面量参数零影响。

```json
"params": {
  "threshold": { "$var": "varB" },   // 引用变量 varB 的值
  "kernelSize": 5                     // 字面量
}
```

| 取值形态 | 含义 |
|----------|------|
| 字面量（数字/字符串/布尔/对象） | 编译期固定的配置值 |
| `{ "$var": "name" }` | 运行期从变量 `name` 取值，并强转为该参数类型 |

**容器节点额外字段**（写入容器自身的 `params`）：

| 字段 | 说明 |
|------|------|
| `containerKind` | 原始容器算子 ID，如 `builtin::for_loop` |
| `width` | 容器宽度 |
| `height` | 容器高度 |

---

### 4.2 inputBindings — 输入端口绑定

**来源**：后端节点目录的 `inputPorts`（数据来源）。

**含义**：输入端口名 → 上游变量名（字符串引用）。

```json
"inputBindings": {
  "input_mat": "upstream_gray_mat",
  "input_point_cloud": "scan_cloud"
}
```

| 规则 | 说明 |
|------|------|
| key | 输入端口名，与目录 `inputPorts[].name` 一致 |
| value | 上游节点 `outputBindings` 中定义的变量名 |
| 不是算子参数 | 值为变量名字符串，不是数值型运行参数 |

---

### 4.3 outputBindings — 输出端口 / 结果命名

**来源**：后端节点目录的 `outputPorts`（结果命名）。

**含义**：输出端口名 → 本节点产出数据的变量名。

```json
"outputBindings": {
  "gray_mat": "gray_mat",
  "output_point_cloud": "output_point_cloud"
}
```

| 规则 | 说明 |
|------|------|
| key | 输出端口名，与目录 `outputPorts[].name` 一致 |
| value | 本节点输出变量名，供下游 `inputBindings` 引用 |

---

### 4.4 regionList — ROI 分区（可选）

用于 2D/3D 掩膜节点的分区图形，与 `params` 平级。

```json
"regionList": [
  {
    "name": "区域1",
    "type": "rect",
    "x": 100,
    "y": 80,
    "width": 200,
    "height": 150,
    "rotation": 0
  },
  {
    "name": "区域2",
    "type": "polygon",
    "points": [
      { "x": 300, "y": 80 },
      { "x": 420, "y": 80 },
      { "x": 360, "y": 180 }
    ]
  },
  {
    "name": "屏蔽区",
    "type": "circle",
    "cx": 600,
    "cy": 150,
    "r": 40
  }
]
```

**分区类型支持**：

| type | 2D | 3D |
|------|----|----|
| `rect` | ✅ | ✅（3D 仅支持矩形） |
| `polygon` | ✅ | ❌ |
| `circle` | ✅ | ❌ |
| `sector` | ✅ | ❌ |
| `path` | ✅ | ❌ |

**分区元信息**（写入 `params`，不写入 `regionList`）：

| 字段 | 说明 |
|------|------|
| `mergeMode` | 合并模式：`union` / `intersect` / `exclude` |
| `referenceSize` | 参考尺寸 `{ width, height }` |
| `sourceType` | `image` 或 `point_cloud` |
| `projectionMode` | 点云投影面：`XY` / `XZ` / `YZ`（`sourceType` 为 `point_cloud` 时） |

---

### 4.5 annotateList — 2D 几何标注（可选）

仅 **2D 掩膜**节点使用，与 `params` 平级。3D 掩膜节点不含此字段。

```json
"annotateList": [
  { "name": "基准点", "type": "point", "x": 320, "y": 240 },
  {
    "name": "测量线",
    "type": "line",
    "start": { "x": 100, "y": 100 },
    "end": { "x": 300, "y": 200 }
  },
  {
    "name": "夹角",
    "type": "angle",
    "vertex": { "x": 400, "y": 300 },
    "arm1": { "x": 350, "y": 250 },
    "arm2": { "x": 450, "y": 250 },
    "angleDeg": 90
  }
]
```

**标注类型**：`point` / `line` / `angle`

---

### 4.6 innerGraphData — 容器子画布（仅容器节点）

仅 `type === "flow_container"` 的节点携带：

```json
"innerGraphData": {
  "nodes": [],
  "edges": []
}
```

- `nodes`：该容器的**直接子节点**集合，节点规范与本规范相同。
- `edges`：仅该容器内子节点之间的连线。
- 子节点若本身为容器，同样携带 `innerGraphData`，支持无限层嵌套。

---

## 5. 容器节点（flow_container）

编辑器中的循环、条件等容器算子，持久化时统一为 `type: "flow_container"`，由 `params.containerKind` 区分种类。

| 字段 | 说明 |
|------|------|
| `type` | 固定为 `flow_container` |
| `params.containerKind` | 容器种类：`builtin::for_loop` / `builtin::if_else` |
| `params.width` / `params.height` | 容器尺寸 |
| 子图字段 | For 循环用 `innerGraphData`；If/Else 用 `params.thenGraphData` / `params.elseGraphData`（见 5.2） |

### 5.1 For 循环（`builtin::for_loop`）

子流程放在 `innerGraphData`；循环配置（`variableName` / `from` / `to` / `step`）放在 `params`。

```json
{
  "id": "node_loop",
  "type": "flow_container",
  "properties": {
    "params": {
      "containerKind": "builtin::for_loop",
      "variableName": "i",
      "from": 1, "to": 10, "step": 1,
      "width": 520, "height": 280
    },
    "inputBindings": { "input_image": "capture_image" },
    "outputBindings": { "output_result": "loop_result" },
    "innerGraphData": { "nodes": [], "edges": [] }
  }
}
```

### 5.2 If/Else（`builtin::if_else`）

then / else **各为独立子图**，分别放在 `params.thenGraphData` / `params.elseGraphData`（结构同顶层 `graphData`，支持递归嵌套）。判断条件放在 `params.condition`。

```json
{
  "id": "node_if",
  "type": "flow_container",
  "properties": {
    "params": {
      "containerKind": "builtin::if_else",
      "condition": { "left": { "$var": "score" }, "op": ">=", "right": 0.8 },
      "thenGraphData": { "nodes": [ /* 条件成立时执行 */ ], "edges": [] },
      "elseGraphData": { "nodes": [ /* 条件不成立时执行 */ ], "edges": [] }
    }
  }
}
```

**condition 三种形态**：

| 形态 | 示例 | 含义 |
|------|------|------|
| 布尔字面量 | `true` / `false` | 固定分支 |
| 单变量真值 | `{ "$var": "flag" }` | 取变量真值（bool / 非 0 数值 / 非空串） |
| 比较表达式 | `{ "left": …, "op": "…", "right": … }` | `left`/`right` 可为字面量或 `{ "$var": name }`，`op` ∈ `==` `!=` `>` `>=` `<` `<=` |

比较时数值优先按数值比较，否则按字符串比较。`elseGraphData` 可省略（无 else 分支）。

---

## 6. 连线（Edge）规范

```json
{
  "id": "edge_xxx",
  "type": "polyline",
  "sourceNodeId": "node_a",
  "targetNodeId": "node_b",
  "sourceAnchorIndex": 1,
  "targetAnchorIndex": 3,
  "properties": { "branch": "yes" }
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | 是 | 连线唯一 ID |
| `type` | string | 是 | 连线类型，固定 `polyline` |
| `sourceNodeId` | string | 是 | 源节点 ID |
| `targetNodeId` | string | 是 | 目标节点 ID |
| `sourceAnchorIndex` | number | 否 | 源端锚点索引，见 6.3 |
| `targetAnchorIndex` | number | 否 | 目标端锚点索引，见 6.3 |
| `properties` | object | 否 | 连线业务数据（含 `branch`，见 6.2） |

### 6.1 连线隔离规则

| 层级 | edges 范围 |
|------|-----------|
| 顶层 `graphData.edges` | 仅连接顶层节点之间（含与一级 `flow_container` 本体） |
| 容器 `innerGraphData.edges` | 仅连接该容器**直接子节点**之间 |
| 跨层 | **禁止**在任一 `edges` 中出现内外混连 |

### 6.2 条件分支（If/Else）

If/Else 的**执行模型**采用容器形式：`builtin::if_else` 容器的 `params.condition` 决定走 `thenGraphData` 还是 `elseGraphData`（见 5.2），后端据此编译执行。

连线上的 `properties.branch`（`yes` / `no` / `default`）为**前端视觉标记**，标识出边属于哪个分支，便于画布渲染；不参与后端执行（执行以容器的 then/else 子图为准）。

### 6.3 锚点索引（统一约定）

连线两端连接到节点的哪个方位，由 `sourceAnchorIndex` / `targetAnchorIndex` 表示，统一约定：

| 索引 | 方位 |
|------|------|
| `0` | 上 |
| `1` | 右 |
| `2` | 下 |
| `3` | 左 |

| 规则 | 说明 |
|------|------|
| 纯视觉路由 | 仅决定连线视觉走线，**不影响执行**；执行拓扑与分支由 `sourceNodeId` / `targetNodeId` / `branch` 决定 |
| 只存索引 | 持久化只保留两个索引，**不存** LogicFlow 的 `sourceAnchorId` / `targetAnchorId`；前端回显时由索引还原 anchorId |
| 缺省 | 旧数据可缺省（无此字段），后端容错处理 |
| 越界 | 索引应为 0–3，越界后端仅告警、不阻断 |

---

## 7. 字段对照表（前端面板 ↔ JSON）

| 前端右侧面板 | JSON 字段 | 后端目录来源 | 存储内容 |
|-------------|-----------|-------------|----------|
| 数据来源 | `inputBindings` | `inputPorts` | 端口 → 上游变量名 |
| 处理设置 | `params` | `configFields` | 算子运行参数 |
| 结果命名 | `outputBindings` | `outputPorts` | 端口 → 本节点产出变量名 |
| 分区标注 | `regionList` | ROI 编辑器 | 矩形/多边形等分区 |
| 几何标注 | `annotateList` | 几何编辑器 | 点/线/角度（仅 2D） |
| 容器子流程 | `innerGraphData` | 画布嵌套 | 子 nodes + 子 edges |

---

## 8. 禁止出现在持久化 JSON 中的字段

以下为编辑器运行时字段，**存库与执行时可忽略**：

```
runStatus
nodeDefinitionId
nodeType
isContainer
inputPorts
outputPorts
embed
parentNodeId
```

容器 `width` / `height` 已收入 `params`，不应作为 `properties` 顶层字段持久化。

### 8.1 已废弃的 Legacy 字段

以下字段**不再使用**，前后端均不应读写：

```
params.roiJson
params.geometryAnnotations
params.rois
```

---

## 9. API 约定

工作流以「离线编辑、整体提交」方式持久化：**新建 / 修改的请求体直接为完整 WorkflowPayload**（顶层含 `projectId` / `name` / `graphData`），后端从中读取 `projectId`、`name` 落库，整段按原文存储、不解析其内部结构。**无需外层 `{ projectId, name, content }` 包装。**

### 9.1 新建

```
POST /api/app/workflow
Body: <完整 WorkflowPayload>   // 顶层须含 projectId、name、graphData
Response: WorkflowDto（含后端生成的工作流 id）
```

> `id` / `updatedAt` 以后端为准：新建时请求体的 `id` 可为空串，真实 id 由响应 `WorkflowDto.id` 返回。

### 9.2 回显

```
GET /api/app/workflow/{workflowId}?projectId={projectId}
Response: WorkflowDto { id, projectId, name, content, 审计字段 }
```

回显响应仍用 `content` 内联完整 WorkflowPayload；前端将 `content.graphData` 传入画布渲染即可完整回显（含容器嵌套、ROI、2D/3D 标注）。

### 9.3 修改保存

```
PUT /api/app/workflow/{workflowId}
Body: <完整 WorkflowPayload>   // 顶层须含 projectId（归属校验）、name、graphData
Response: WorkflowDto
```

工作流 ID 取自路由；`projectId` 从请求体读取并用于归属校验，防止跨项目写入。

### 9.4 执行

```
POST /api/app/workflow-execution/run
Body: { "projectId": "...", "workflowId": "...",
        "inputVariableKeys": [...]?, "outputVariableNames": [...]? }
Response: WorkflowRunResultDto { variables: [{ name, valueType, scalarValue?, stagedKey? }], ... }
```

加载定义 → 编译 → 注入初始变量 → 执行 → 写回结果。变量进出约定见第 13.7 节（缺省时从 start/end 节点签名自动推导）。

### 9.5 节点目录

```
GET /api/app/workflow-node-palette
Response: { categories: [ { name, nodes: [ NodeDefinition ] } ] }
```

按分类返回全部节点定义，含 `inputPorts`、`outputPorts`、`configFields`，与本文 `properties` 三段式结构对应。分类包含：

| 分类 | 节点 | 说明 |
|------|------|------|
| 流程边界 | `start-node` / `end-node` | `isBoundary: true`，单例边界节点 |
| 流程控制 | `builtin::for_loop` / `builtin::if_else` | 容器节点（`hasBody: true`） |
| 参数赋值 | `builtin::assign` | 变量赋值 |
| （各算子分类） | 算子节点 | `id` 为算子 UUID |

---

## 10. 完整示例

```json
{
  "id": "wf-001",
  "projectId": "1",
  "projectRecordId": "1",
  "name": "循环检测流程",
  "version": "v1.0.0",
  "description": "拍照 → 循环{ROI→检测} → 结束",
  "updatedAt": "2026-06-27T10:00:00.000Z",
  "graphData": {
    "nodes": [
      {
        "id": "node_start",
        "type": "start-node",
        "x": 100,
        "y": 300,
        "text": { "value": "开始" },
        "properties": {
          "params": {},
          "inputBindings": {},
          "outputBindings": {}
        }
      },
      {
        "id": "node_capture",
        "type": "camera-operator-uuid",
        "x": 280,
        "y": 300,
        "text": { "value": "相机拍照" },
        "properties": {
          "params": { "exposure": 120, "gain": 2.0 },
          "inputBindings": {},
          "outputBindings": { "image": "capture_image" }
        }
      },
      {
        "id": "node_preprocess",
        "type": "preprocess-operator-uuid",
        "x": 400,
        "y": 300,
        "properties": {
          "params": {},
          "inputBindings": {
            "input_mat": "capture_image",
            "input_point_cloud": "scan_cloud"
          },
          "outputBindings": {
            "gray_mat": "gray_mat",
            "output_point_cloud": "output_point_cloud"
          }
        }
      },
      {
        "id": "node_loop",
        "type": "flow_container",
        "x": 520,
        "y": 300,
        "text": { "value": "循环检测" },
        "properties": {
          "params": {
            "containerKind": "builtin::for_loop",
            "loop_count": 3,
            "width": 520,
            "height": 280
          },
          "inputBindings": { "input_image": "gray_mat" },
          "outputBindings": { "output_result": "loop_result" },
          "innerGraphData": {
            "nodes": [
              {
                "id": "node_roi",
                "type": "a1b2c3d4-0001-4000-8000-000000000001",
                "x": 80,
                "y": 100,
                "text": { "value": "ROI 分区" },
                "properties": {
                  "params": {
                    "mergeMode": "union",
                    "sourceType": "image",
                    "referenceSize": { "width": 1920, "height": 1080 }
                  },
                  "inputBindings": { "input_image": "gray_mat" },
                  "outputBindings": { "mask": "slot_mask" },
                  "regionList": [
                    {
                      "name": "工位1",
                      "type": "rect",
                      "x": 50,
                      "y": 60,
                      "width": 180,
                      "height": 120,
                      "rotation": 0
                    }
                  ],
                  "annotateList": [
                    { "name": "基准点", "type": "point", "x": 100, "y": 100 }
                  ]
                }
              },
              {
                "id": "node_detect",
                "type": "detect-operator-uuid",
                "x": 280,
                "y": 100,
                "text": { "value": "缺陷检测" },
                "properties": {
                  "params": { "threshold": 0.85 },
                  "inputBindings": {
                    "input_image": "gray_mat",
                    "input_mask": "slot_mask"
                  },
                  "outputBindings": { "result": "slot_result" }
                }
              }
            ],
            "edges": [
              {
                "id": "edge_inner_1",
                "type": "polyline",
                "sourceNodeId": "node_roi",
                "targetNodeId": "node_detect",
                "properties": {}
              }
            ]
          }
        }
      },
      {
        "id": "node_end",
        "type": "end-node",
        "x": 820,
        "y": 300,
        "text": { "value": "结束" },
        "properties": {
          "params": {},
          "inputBindings": {},
          "outputBindings": {}
        }
      }
    ],
    "edges": [
      {
        "id": "edge_1",
        "type": "polyline",
        "sourceNodeId": "node_start",
        "targetNodeId": "node_capture",
        "properties": {}
      },
      {
        "id": "edge_2",
        "type": "polyline",
        "sourceNodeId": "node_capture",
        "targetNodeId": "node_preprocess",
        "properties": {}
      },
      {
        "id": "edge_3",
        "type": "polyline",
        "sourceNodeId": "node_preprocess",
        "targetNodeId": "node_loop",
        "properties": {}
      },
      {
        "id": "edge_4",
        "type": "polyline",
        "sourceNodeId": "node_loop",
        "targetNodeId": "node_end",
        "properties": {}
      }
    ]
  }
}
```

---

## 11. 后端执行建议

### 11.1 变量解析

1. 按拓扑序遍历节点。
2. 执行前：根据 `inputBindings`，从上下文读取上游变量值。
3. 执行后：将结果写入 `outputBindings` 对应的变量名。

### 11.2 容器执行

1. 识别 `type === "flow_container"`，读取 `params.containerKind` 确定容器语义。
2. **For 循环**（`builtin::for_loop`）：按 `params` 的 `variableName` / `from` / `to` / `step` 重复执行 `innerGraphData` 子图，循环变量写入上下文。
3. **If/Else**（`builtin::if_else`）：求值 `params.condition`，为真执行 `params.thenGraphData`，否则执行 `params.elseGraphData`。
4. 子图按本节规则递归执行（支持无限嵌套）。

### 11.3 ROI / 掩膜

1. 从 `regionList` 读取分区图形。
2. 从 `params.mergeMode`、`referenceSize` 等读取元信息。
3. 2D 节点可选读取 `annotateList` 辅助标注。

### 11.4 建议校验项

| 校验项 | 规则 |
|--------|------|
| 连线隔离 | 各层 `edges` 不跨层 |
| 变量存在性 | `inputBindings` 引用的变量名在上游 `outputBindings` 中存在 |
| 端口名合法 | bindings 的 key 与节点目录端口名一致 |
| 容器完整 | `flow_container` 必须含 `innerGraphData` |
| 流程起止 | 全图仅一个 `start-node`、一个 `end-node`（顶层） |

---

## 12. 前端实现说明

| 能力 | 实现位置 |
|------|----------|
| 平铺画布 → 持久化 JSON | `flatGraphToGraphData()` |
| 持久化 JSON → 平铺画布 | `graphDataToFlatGraph()` |
| 组装完整 Payload | `buildWorkflowPersistPayload()` |
| 保存时 console 输出 | `useWorkflowPage.ts` → `saveWorkflow()` |
| 回显渲染 | `useLogicFlowEditor.ts` → `renderGraphData()` |

编辑器运行时画布为 LogicFlow 平铺结构；保存/回显时自动与嵌套 `graphData` 互转，后端无需关心编辑器内部 `parentNodeId` 等字段。

---

## 13. 变量系统

工作流通过**命名变量**在节点间传值。变量是一张运行期命名字典，活的值（Mat、点云、标量等）按名读写。

### 13.1 变量的产生与引用

| 动作 | 来源 | 语义 |
|------|------|------|
| **写（产生）** | `outputBindings` 的值 / `builtin::assign` 的 `variableName` / 循环变量 | **upsert**：变量不存在则创建，已存在则覆盖 |
| **读（引用）** | `inputBindings` 的值 / `params` 中的 `{ "$var": name }` | 按名取上游已产出的变量值 |

- `outputBindings: { "image": "MatB" }` → 把本节点 `image` 端口的产物命名为变量 `MatB`。
- `inputBindings: { "input_mat": "MatB" }` → 本节点 `input_mat` 端口读取变量 `MatB`。
- `params.threshold = { "$var": "varB" }` → 配置参数 `threshold` 运行期取变量 `varB` 的值。

> **upsert 即用户语义"存在不创建、只赋值"**：同名写入直接覆盖；覆盖 Mat/点云时，旧值若无其它引用会被自动释放（防内存堆积）。

### 13.2 作用域

当前运行时为**扁平作用域**（ForLoop / IfElse 的子图在当前作用域内执行，变量全局可见）：

| 操作 | 规则 |
|------|------|
| 读 | 当前作用域 → 逐级向上查父作用域 |
| 写 | 写当前作用域 |
| 容器导出 | 容器内层结果经**容器节点的 `outputBindings`** 提升到父作用域 |

### 13.3 变量符号表（可选）

`WorkflowPayload` 顶层可携带一张**可选**符号表 `variables`，由前端维护，供自动补全 / 类型显示 / 校验参考；后端以 bindings 为权威（可忽略或据其比对告警）。

```json
"variables": [
  { "name": "MatB", "type": "Mat",           "producedBy": "node_A", "scope": "global" },
  { "name": "varB", "type": "System.Double",  "producedBy": "node_B", "scope": "global" }
]
```

变量名规则：`[A-Za-z_][A-Za-z0-9_]*`，**区分大小写**。

### 13.4 工作流对外 I/O 签名

起止节点的端口绑定声明工作流的"函数签名"：

| 节点 | 绑定 | 含义 |
|------|------|------|
| `start-node` | `outputBindings` 的值 | 工作流**输入**变量（执行前由外部注入） |
| `end-node` | `inputBindings` 的值 | 工作流**输出**变量（执行后对外产出） |

执行接口（9.4）未显式指定 `inputVariableKeys` / `outputVariableNames` 时，后端**自动从此签名推导**。

### 13.5 静态校验规则

| 校验 | 级别 | 规则 |
|------|------|------|
| 写前读 | Error | 引用的变量必须由上游已产出 |
| 变量命名 | Error | 匹配 `[A-Za-z_][A-Za-z0-9_]*` |
| 起止唯一 | Error / Warning | 顶层多个 start/end → Error；缺失 → Warning |
| 空工作流 | Warning | 仅含起止节点、无任何算子/赋值/容器 |
| 类型兼容 | Warning | 变量类型与消费端口/参数类型不一致（数值间放宽） |
| 锚点越界 | Warning | `sourceAnchorIndex` / `targetAnchorIndex` 超出 0–3 |

### 13.6 运行期取值

- `inputBindings`：执行前把引用变量写入对应端口变量。
- `params` 的 `{ "$var": name }`：在**节点执行时**从上下文取 `name` 的值，强转为该参数 CLR 类型后传入算子构造函数（字面量参数则编译期固定）。
- `outputBindings`：执行后把端口产物重命名为目标变量（upsert）。

### 13.7 Redis 暂存桥接（跨请求传值）

执行期变量住在**内存**上下文；跨 HTTP 请求 / 持久化结果时，经 Redis 暂存（VariableStage）桥接：

- **值编解码**：标量用文本；`Mat` / 点云用**原始二进制精确往返**（保留位深 / 通道 / float）。`valueType` 令牌：`string` / `int` / `long` / `double` / `bool` / `Mat` / `PointCloudData`。
- **加载**：执行前按 `inputVariableKeys`（或 start 签名）从暂存反序列化为初始变量注入上下文。
- **写回**：执行后按 `outputVariableNames`（或 end 签名）把结果序列化写回暂存，响应返回其 `stagedKey`；标量值同时内联在 `scalarValue`。

---

## 附录 A：properties 快速参考

```
properties
├── params              ← configFields（算子参数；值可为字面量或 { "$var": 变量名 }）
├── inputBindings       ← inputPorts（端口 → 上游变量名）
├── outputBindings      ← outputPorts（端口 → 本节点变量名）
├── regionList?         ← ROI 分区数组
├── annotateList?       ← 2D 几何标注数组
└── innerGraphData?     ← 仅 flow_container（子 nodes + 子 edges）
```

---

## 附录 B：修订记录

| 版本 | 日期 | 说明 |
|------|------|------|
| v1.0 | 2026-06-27 | 初版：嵌套 graphData、regionList/annotateList、flow_container |
| v1.1 | 2026-06-27 | 连线新增 `sourceAnchorIndex` / `targetAnchorIndex`（统一锚点 0=上/1=右/2=下/3=左，见 6.3） |
| v1.2 | 2026-06-27 | 新增变量系统（第 13 节：`$var` 引用 / 符号表 / I/O 签名 / 校验 / Redis 桥接）；start/end 由面板 `isBoundary` 下发；补全 API 约定（新建/回显/修改/执行/目录） |
| v1.3 | 2026-06-27 | If/Else 容器定稿（5.2）：`params.condition` 比较表达式 + `thenGraphData` / `elseGraphData` 双子图；6.2 边 `branch` 改为视觉标记 |
| v1.4 | 2026-06-27 | 新建/修改去掉 `{ projectId, name, content }` 包装，请求体直接为完整 WorkflowPayload（9.1 / 9.3）；projectId/name 由后端从 payload 读取 |
