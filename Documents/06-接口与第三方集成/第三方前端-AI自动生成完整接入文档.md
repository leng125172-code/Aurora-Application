# Aurora Struct3D 第三方前端完整接入规范（AI 自动生成版）

> 本文是第三方前端的主接入规格，不是某一个算子的专项说明。
>
> 代码生成 AI 必须先阅读本文，再按需阅读本文列出的专项附件。不得只读取安装
> 角度文档后生成整个系统。

## 0. 文档体系

主文档：

- `第三方前端-AI自动生成完整接入文档.md`：系统范围、总体架构、动态工作流、
  通用交互、数据展示和验收标准。

专项附件：

- `第三方相机预览接入文档.md`：相机 HTTP Multipart BMP 预览协议及完整解析器规格。
- `第三方前端-安装角度检测AI生成接入文档.md`：安装角度四类检测的详细端口、
  ROI、结果卡片及验收标准。
- `3D缺陷检测能力实施计划.md`：CAD 比较和缺陷检测的后续能力路线。
- `安装角度检测设计记录.md`：安装角度数学定义及阶段记录。
- `检测算子.md`：当前公开算子清单、GUID、分类和源码位置。

代码生成 AI 的读取顺序：

```text
完整接入主文档
  → 检测算子清单
  → 当前功能涉及的专项附件
  → 服务端 OpenAPI
  → 目标前端现有源码和组件规范
```

## 1. 生成目标

生成可独立运行或嵌入现有平台的完整第三方前端，范围包括：

1. 登录态和 HTTP 客户端。
2. 项目与工作流管理。
3. 动态算子面板。
4. 可编程式视觉工作流画布。
5. 2D ROI、3D 平面 ROI 和特征 ROI。
6. 图像、点云、网格和检测叠加结果展示。
7. 相机实时预览。
8. 标定向导在线预览。
9. 三维数模库。
10. 扫描点云与工艺模型 3D 比较。
11. 高度差、形状、面积、角度和安装姿态检测。
12. 工作流校验、仿真、试运行、调试和正式执行。
13. 工作流最终输出变量配置。
14. `OK / NG / UNKNOWN / ERROR` 结果展示。
15. 文件上传、下载和 Blob 结果查看。

推荐技术栈：

```text
Vue 3 + TypeScript + Vite
Pinia
Axios
Three.js
支持自定义锚点和容器节点的流程图库
Vitest
```

React 项目可以替换 UI 层，但必须保留本文的数据契约、生命周期和状态语义。

## 2. 强制设计原则

### 2.1 服务端元数据是算子真源

不得在前端手写全部算子端口。必须从以下接口动态获取：

```http
GET /api/app/workflow-node-palette
```

当前公开算子为 101 个，但前端不得写死这个数量。后端增加算子后，第三方前端
应能自动展示普通算子，只对特殊控件增加专用渲染器。

### 2.2 算子 GUID 是保存标识

节点面板响应中的：

```ts
nodeDefinition.nodeType === 'Operator'
```

只是节点分类。保存工作流时：

```ts
graphData.nodes[index].type = nodeDefinition.id
```

其中 `nodeDefinition.id` 是算子 GUID。

禁止：

```ts
node.type = 'Operator'
node.properties.params.operatorId = '...'
```

### 2.3 显示名不能作为绑定键

端口和参数必须使用服务端返回的 `name`。例如：

```text
显示名：X方向倾角(度)
绑定键：tilt_x
```

显示名可以本地化，绑定键不能翻译或重命名。

### 2.4 三态检测

检测结果不是简单布尔值：

```text
OK       数据可信，结果合格
NG       数据可信，结果超差
UNKNOWN  数据或拟合质量不足，不能形成可信判定
ERROR    工作流或节点执行失败
```

`UNKNOWN` 时 `is_ok` 通常也是 `false`，前端不能仅根据 `is_ok=false` 显示 NG。

### 2.5 高低分辨率数据分工

```text
降采样点云：预览、平面搜索、粗定位、交互选区
原始点云：最终 ROI 提取、拟合、测量和缺陷判定
```

不得因为预览使用低分辨率点云，就把低分辨率结果传入最终检测。

## 3. 配置与认证

环境变量：

```text
VITE_API_BASE_URL
VITE_SIGNALR_BASE_URL
VITE_REQUEST_TIMEOUT_MS
```

HTTP 客户端要求：

- Base URL 集中配置。
- Bearer Token 或 Cookie 认证集中处理。
- 支持请求取消。
- 支持 Blob、Multipart 和普通 JSON。
- 统一提取 ABP 错误响应。
- `401/403` 进入认证流程。
- 不在组件中散落 Axios 实例。

示例：

```ts
export interface ApiError {
  status: number
  code?: string
  message: string
  details?: string
  validationErrors?: Array<{
    message: string
    members?: string[]
  }>
  traceId?: string
}
```

## 4. 系统模块

建议生成：

```text
src/
  api/
    client.ts
    projects.ts
    workflows.ts
    workflow-runtime.ts
    cameras.ts
    calibration.ts
    product-models.ts
    blobs.ts
  types/
    workflow.ts
    operators.ts
    inspection.ts
    point-cloud.ts
    product-model.ts
    calibration.ts
  stores/
    projects.ts
    workflow-editor.ts
    workflow-runtime.ts
    cameras.ts
    product-models.ts
  components/
    workflow/
    roi/
    preview/
    point-cloud/
    inspection/
    product-model/
  views/
    projects/
    workflows/
    calibration/
    product-models/
  streaming/
    multipart-bmp-parser.ts
    camera-preview-client.ts
  composables/
  tests/
```

## 5. 动态节点面板

接口：

```http
GET /api/app/workflow-node-palette
```

核心类型：

```ts
export interface NodePaletteDto {
  categories: NodeCategoryDto[]
}

export interface NodeCategoryDto {
  name: string
  nodes: NodeDefinitionDto[]
}

export interface NodeDefinitionDto {
  id: string
  nodeType: string
  displayName: string
  description?: string | null
  hasBody: boolean
  isBoundary: boolean
  inputPorts: NodePortDto[]
  outputPorts: NodePortDto[]
  configFields: NodeConfigFieldDto[]
}

export interface NodePortDto {
  name?: string | null
  displayName?: string | null
  portTypeName: string
  defaultValue?: unknown
  valueLimit?: unknown
  errorCheck: boolean
  controlType: number | string
  matType: number | string
}

export interface NodeConfigFieldDto {
  name: string
  displayName?: string | null
  valueTypeName: string
  defaultValue?: unknown
  valueLimit?: unknown
  required: boolean
  controlType: number | string
}
```

当前控件类型：

```ts
export enum PortControlType {
  Input = 0,
  Select = 1,
  ImageUpload = 2,
  PointCloudUpload = 3,
  Download = 4,
  Switch = 5,
  Number = 6,
  Color = 7,
  Variable = 8,
  ProductModelSelect = 9,
  PlaneRoiEditor = 10,
}
```

动态渲染规则：

| controlType | 组件 |
|---|---|
| Input / Number | 数字或文本输入 |
| Select | 下拉框，选项来自 `valueLimit` |
| Switch | 布尔开关 |
| Color | 颜色选择器 |
| ImageUpload | 图片上传 |
| PointCloudUpload | 点云上传 |
| Download | 结果下载 |
| Variable | 变量选择或绑定 |
| ProductModelSelect | 三维数模库选择器 |
| PlaneRoiEditor | 三维平面 UV ROI 编辑器 |

未知控件回退为通用字段，并记录开发告警，不能使页面崩溃。

## 6. 算子分类与普通算子生成

当前分类：

```text
2D尺寸测量
2D检测定位
2D预处理
3D点云预处理
3D拟合测量
3D配准
3D平面处理
3D重建分割
流程控制
数据存储
数据读取
```

详细算子及 GUID 读取 `检测算子.md`，运行时仍以节点面板接口为准。

普通算子不需要专门写 Vue 组件。统一节点组件根据：

- `inputPorts`
- `outputPorts`
- `configFields`
- `portTypeName`
- `controlType`
- `valueLimit`

自动生成节点和属性面板。

只有以下能力需要专用 UI：

- 图像和点云上传
- 产品模型选择
- 2D ROI
- 平面 UV ROI
- 组合特征 ROI
- 相机预览
- 点云和网格预览
- 检测结果卡片

## 7. 工作流图保存协议

```ts
export interface WorkflowGraphDto {
  nodes: WorkflowNodeDto[]
  edges: WorkflowEdgeDto[]
}

export interface WorkflowNodeDto {
  id: string
  type: string
  x?: number
  y?: number
  text?: {
    x?: number
    y?: number
    value?: string
  }
  properties?: WorkflowNodePropertiesDto
}

export interface WorkflowNodePropertiesDto {
  params?: Record<string, unknown>
  paramSources?: Record<string, 'literal' | 'variable'>
  inputBindings?: Record<string, string>
  inputBindingSources?: Record<string, 'literal' | 'variable'>
  outputBindings?: Record<string, string>
  outputBindingSources?: Record<string, 'literal' | 'variable'>
  innerGraphData?: WorkflowGraphDto
}

export interface WorkflowEdgeDto {
  id: string
  sourceNodeId?: string
  targetNodeId?: string
  sourceAnchorIndex?: number
  targetAnchorIndex?: number
  properties?: {
    branch?: string
  }
}
```

要求：

- 算子节点 `type` 为算子 GUID。
- 内置节点使用服务端返回的内置类型。
- 锚点索引严格对应节点面板端口顺序。
- 节点实例 ID 与算子 GUID 是不同概念。
- 参数构造器名称区分大小写。
- JSON 数值不能保存为本地化字符串。
- ROI JSON 配置按算子要求保存为字符串。

## 8. 工作流 API

基础路径：

```text
/api/app/workflow
```

接口：

```http
GET    /api/app/workflow?projectId={projectId}
POST   /api/app/workflow
GET    /api/app/workflow/{workflowId}
PUT    /api/app/workflow/{workflowId}
DELETE /api/app/workflow/{workflowId}

POST   /api/app/workflow/{workflowId}/validate
POST   /api/app/workflow/{workflowId}/simulate
GET    /api/app/workflow/{workflowId}/output-config
PUT    /api/app/workflow/{workflowId}/output-config

POST   /api/app/workflow-execution/run
```

保存请求：

```ts
export interface SaveWorkflowInput {
  projectId: string
  name: string
  graphData: WorkflowGraphDto
}
```

执行请求：

```ts
export interface RunWorkflowInput {
  projectId: string
  workflowId: string
  inputVariableKeys?: string[]
  inputVariableBindings?: WorkflowVariableBindingKeyDto[]
  outputVariableNames?: string[]
  outputVariableBindings?: WorkflowVariableBindingKeyDto[]
  runtimeInstanceId?: string
  variableReadTimeoutMs?: number
}
```

工作流编辑器保存流程：

```text
前端结构校验
  → 保存工作流
  → 服务端 validate
  → 可选 simulate
  → 配置 output-config
  → 试运行或发布
```

## 9. 输出变量

工作流执行完成后展示什么，由输出变量配置决定。不能只保存图节点和连线，而
不配置最终输出。

```ts
export interface WorkflowOutputConfigDto {
  workflowId: string
  outputVariables: string[]
}
```

建议至少输出：

```text
inspection_status
is_valid
is_ok
result_json
```

按功能增加：

```text
output_image
annotated_image
selected_points
colored_point_cloud
mesh_vertices
mesh_indices
mesh_normals
defect_list_json
download_url
```

执行按钮前检查：

- 工作流已经保存。
- 服务端校验通过。
- 至少配置一个输出变量。
- 检测工作流配置状态和结构化结果。
- 图像/点云展示工作流配置对应视觉输出。

## 10. 结果统一模型

```ts
export type InspectionStatus = 'OK' | 'NG' | 'UNKNOWN' | 'ERROR'

export interface InspectionResultViewModel {
  status: InspectionStatus
  isValid: boolean
  isOk: boolean
  title: string
  metrics: Array<{
    key: string
    label: string
    value: string | number | boolean | null
    unit?: string
    lowerLimit?: number
    upperLimit?: number
    passed?: boolean
  }>
  qualityReasons: string[]
  defects: DefectViewModel[]
  visualOutputs: VisualOutputReference[]
  raw: unknown
}
```

统一状态：

```ts
export function resolveStatus(
  status: unknown,
  isValid: unknown,
  isOk: unknown,
): InspectionStatus {
  if (status === 'ERROR') return 'ERROR'
  if (isValid !== true || status === 'UNKNOWN') return 'UNKNOWN'
  if (isOk === true && status === 'OK') return 'OK'
  return 'NG'
}
```

颜色：

```text
OK       绿色
NG       红色
UNKNOWN  黄色或灰色
ERROR    深红色
```

## 11. 2D ROI

2D ROI 支持：

- 矩形
- 多边形
- 路径
- 圆形
- 扇形

通用要求：

- ROI 保存相对原图坐标，不能保存 CSS 显示坐标。
- 图像缩放和 Letterbox 时必须执行坐标变换。
- 支持多个命名 ROI。
- ROI 可以分别绑定不同测量节点。
- ROI 叠加结果与原图分别保留。
- 支持恢复、删除、复制和锁定。

高度差等双区域测量需要两个独立 ROI，不能共用对象引用。

## 12. 3D 平面 ROI

相关算子：

| 算子 | GUID |
|---|---|
| 选择拟合平面 | `c38e27a6-8d49-4c41-96a0-a53f30c23101` |
| 定义平面区域 | `d420473b-76f1-455a-83e4-492809c23102` |
| 提取平面区域点云 | `e15fb284-2abe-477c-bd47-3cfde9c23103` |

正确流程：

```text
功能 1：选择 A/B/C 平面
功能 2：在选中平面局部 UV 中绘制区域
功能 3：从原始高密度点云提取区域内所有点
```

配置：

```ts
export interface PlaneRoiConfig {
  points: Array<{ u: number; v: number }>
}
```

执行输出：

```ts
export interface PlaneRoiRuntimeValue {
  origin: [number, number, number]
  axisU: [number, number, number]
  axisV: [number, number, number]
  normal: [number, number, number]
  points: Array<{ u: number; v: number }>
}
```

用户配置写入：

```ts
node.properties.params.roiJson = JSON.stringify({ points })
```

空点集表示使用整个平面范围；1～2 个点属于无效配置。

## 13. 形状、面积、高度和角度选区

### 面积和形状

```text
选择平面
  → 平面 ROI
  → 提取 ROI 全部点
  → 在 ROI 内执行矩形、圆、面积或长度测量
```

### 高度差

```text
基准 ROI → 高度统计 ─┐
测量 ROI → 高度统计 ─┴→ 高度差判定
```

### 安装角度

角度不是单个普通 ROI。需要两个特征 ROI：

- 基准特征 ROI
- 被测特征 ROI

支持：

- 平面与平面
- 轴线与平面
- 轴线与轴线
- 平面内旋转 twistZ

详细接口读取：

```text
第三方前端-安装角度检测AI生成接入文档.md
```

## 14. 图像预览和输出

图像显示要求：

- 支持 PNG、JPEG、BMP。
- 不假设固定分辨率。
- 支持原图、处理图和叠加标注图切换。
- 支持 ROI Overlay。
- 支持 Fit、1:1、缩放和平移。
- 显示真实像素坐标。
- Blob URL 替换时释放旧 URL。

最终展示应优先使用工作流输出的标注图，而不是前端自行重复计算判定。

## 15. 点云和网格展示

点云：

- 支持 XYZ。
- 支持逐点 RGB。
- 支持按 Z、高度差、标签和缺陷状态着色。
- 支持原始点云、选中点、剩余点切换。
- 大点云渲染时允许仅在 GPU 显示层抽样，不修改检测数据。

网格：

- `mesh_vertices`
- `mesh_indices`
- `mesh_normals`
- `boundary_json`
- `mesh_info`

拟合平面连续面效果使用“拟合平面网格生成”，而不是把投影点强行连成规则网格。

Three.js 资源释放：

- 销毁 `BufferGeometry`
- 销毁 `Material`
- 移除事件监听
- 取消动画帧
- 释放 Blob URL

## 16. 三维数模库

基础路径：

```text
/api/app/product-model
```

接口：

```http
GET    /api/app/product-model
GET    /api/app/product-model/{id}
GET    /api/app/product-model/logs
POST   /api/app/product-model/upload
PUT    /api/app/product-model/{id}/name
DELETE /api/app/product-model/{id}
POST   /api/app/product-model/{id}/retry-conversion
GET    /api/app/product-model/{id}/download
POST   /api/app/product-model/clean-up-orphaned-records
```

主要 DTO：

```ts
export interface ProductModelDto {
  id: string
  name: string
  originalFileName: string
  fileFormat: number
  fileFormatDisplay: string
  fileSizeBytes: number
  conversionStatus: number
  conversionErrorMessage: string | null
  isReady: boolean
  needsConversion: boolean
  uploaderUserName: string | null
  creationTime: string
  lastModificationTime: string | null
}
```

工作流中的 `ProductModelSelect / 9`：

- 只显示 `isReady=true` 的模型。
- 配置值保存模型 GUID。
- 显示名称和文件格式。
- 不保存临时下载 URL。
- 模型被删除或未就绪时显示失效状态。

当前工作流读取模型算子：

```text
读取工艺模型
GUID: ec67f5da-e934-4e0e-9eb5-46e7f8e02101
```

## 17. 3D 比较

推荐流程：

```text
扫描点云
  → 预处理
  → 粗配准
  → ICP 精配准
  → 应用变换
  → 3D 比较
               ↑
模型库 → 读取工艺模型
```

核心输出：

```text
is_ok
max_distance
mean_distance
defect_ratio
missing_ratio
result_json
比较或着色点云
```

前端结果页：

- 显示模型与扫描点云叠加。
- 偏差热力图必须带色标和单位。
- 区分凸起、凹陷、缺失和未知区域。
- 配准失败不能继续显示普通 OK/NG。
- 允许点击缺陷列表定位到三维区域。

CAD 面内均匀采样、有符号点到面距离、局部公差和盲区过滤属于后续增强能力，
见 `3D缺陷检测能力实施计划.md`。前端设计应预留这些字段，但不能伪装成已经实现。

## 18. 相机实时预览

协议事实：

| 项目 | 值 |
|---|---|
| 相机 REST 根路径 | `/api/app/camera-device` |
| 启动预览 | `POST /api/app/camera-device/{cameraId}/start-preview` |
| 停止预览 | `POST /api/app/camera-device/{cameraId}/stop-preview` |
| 普通预览流 | `GET /api/streaming/cameras/{cameraId}/preview` |
| 标定预览流 | `GET /api/streaming/calibration/{projectId}/cameras/{cameraRole}/preview` |
| SignalR | `/signalr-hubs/camera`，可选 |
| 流格式 | `multipart/x-mixed-replace` |
| 帧格式 | `image/bmp` |

强制要求：

- 使用 `fetch + ReadableStream`。
- 动态解析 Multipart boundary。
- 每帧按 `Content-Length` 读取。
- Chunk 可以在任意字节位置拆分。
- 只保留最新显示帧。
- 替换帧时释放旧 Object URL。
- 停止和组件卸载时 Abort。
- 不用 `<img src="流地址">` 直接播放。
- 不再次解包 `BAYGB12Packed`。
- 不把 BMP 当 JPEG。

完整解析器、重连和 SignalR 规范读取：

```text
第三方相机预览接入文档.md
```

## 19. 标定向导

标定向导至少包含：

1. 相机和投影仪选择。
2. 参数配置。
3. 棋盘格采集。
4. 棋盘格角点检测和质量评分。
5. 标定计算。
6. 在线扫描和点云预览。

在线标定图像预览优先使用专用 HTTP Streaming 端点：

```http
GET /api/streaming/calibration/{projectId}/cameras/{cameraRole}/preview
```

SignalR 只用于状态、进度和可选通知，不应把高频图像帧作为主要传输方式。

前端必须处理：

- 左右相机角色。
- 项目切换时中止旧流。
- 步骤切换时释放预览。
- 棋盘格照片有效性和质量原因。
- 点云在线预览和下载。
- 标定失败时保留可诊断信息。

## 20. ROI 底图预运行

编辑 ROI 时，底图必须来自目标 ROI 节点实际依赖的上游工作流，而不是使用
上传阶段缓存的其他尺寸图像。

服务端提供“生成 ROI 编辑底图”运行时方法，用于：

```text
只执行目标 ROI 节点真正依赖的祖先子图
  → 返回同源底图 Blob
  → 返回真实尺寸
  → 返回投影映射
```

具体 REST 路由以服务端 OpenAPI 中
`WorkflowRuntimeAppService.GenerateRoiBaseImageAsync` 为准。第三方应从 OpenAPI
生成客户端，不能猜测或硬编码未确认的 ABP Action 路由。

## 21. 调试和执行

支持：

- 静态校验
- 数据流仿真
- 试运行
- 调试会话
- 单步执行
- 状态查询
- 停止调试
- 项目部署和正式运行

状态页至少显示：

- executionId
- 当前节点
- 已完成节点
- 节点耗时
- 变量摘要
- 输出变量
- 错误节点 ID
- 错误消息

错误消息必须能定位画布节点：

```text
工作流执行失败，节点 <node-id>：<message>
```

## 22. 文件和 Blob

数据存储算子可以输出：

- Blob Key
- 下载 URL
- 文件名
- MIME

第三方前端：

- 认证下载使用 Axios Blob 或 fetch。
- 不假设 URL 永久有效。
- 点云下载按 PLY 等实际格式处理。
- 图片下载按服务端 MIME 处理。
- 预览 URL 生命周期结束时及时释放。

## 23. 连线兼容

前端连线前检查：

- 输出端口与输入端口类型兼容。
- Mat 图像与点云矩阵不能仅因为同为 Mat 就随意连接。
- PointCloudData 与图像数据区分。
- 端口名称不能替代类型检查。
- 连接后仍以服务端 validate 为最终结果。

建议连线状态：

```text
valid
warning
invalid
unresolved
```

## 24. 前端保存前校验

1. Start 和 End 边界节点存在且唯一。
2. 不允许悬空必填输入。
3. 构造参数完整且类型正确。
4. 数字为有限值。
5. ROI 点数合法。
6. 双 ROI 使用不同节点实例。
7. 端口类型兼容。
8. 输出绑定变量名合法且不冲突。
9. 检测节点输出状态和结果。
10. 模型选择值有效且模型已就绪。
11. 工作流无非法环路。
12. 容器节点子图结构合法。

前端校验只是提升体验，不能替代服务端校验。

## 25. 错误处理

| 状态 | 处理 |
|---|---|
| 400 | 显示字段或工作流校验详情 |
| 401 | 重新认证 |
| 403 | 显示权限不足 |
| 404 | 提示项目、工作流、模型或设备不存在 |
| 409 | 显示状态、版本或变量冲突 |
| 500 | 显示服务端消息、节点 ID、traceId |

不得：

- 静默吞掉执行错误。
- 只显示“请求失败”而丢失服务端 details。
- 无限自动重试。
- 因某个视觉输出加载失败而覆盖检测结构化结果。

## 26. 性能和生命周期

- 相机流只能存在一个有效 reader。
- 组件卸载停止网络流和动画。
- Three.js 对象必须销毁。
- 点云预览可抽样，检测输入不可修改。
- 长列表虚拟化。
- 节点面板缓存但支持刷新。
- 工作流编辑采用脏状态提示。
- 大文件上传支持进度和取消。
- 状态轮询带退避和停止条件。

## 27. 安全

- 不在浏览器保存明文设备密码。
- 不在 URL 查询参数放长期令牌。
- 不允许前端构造任意本地文件路径给服务端读取。
- 上传文件校验格式和大小。
- 下载文件名进行安全处理。
- 所有 ID 视为不透明字符串。
- 不根据 GUID 推导业务权限。

## 28. 测试要求

### 单元测试

- 动态控件选择。
- 数字和枚举参数序列化。
- 工作流算子节点 type 为 GUID。
- ROI 显示坐标与真实坐标转换。
- Plane UV ROI 序列化。
- OK/NG/UNKNOWN 归一化。
- Multipart 跨 Chunk 解析。
- Object URL 释放。
- 点云颜色和网格索引解析。

### 集成测试

- 加载节点面板。
- 创建、保存、回显和修改工作流。
- 服务端校验。
- 输出变量配置。
- 试运行并显示结构化结果。
- 模型库选择后保存和回显。
- 相机预览启动、断开和停止。
- 标定步骤切换时流被释放。

### 端到端测试

1. 创建图像检测工作流并显示标注图。
2. 创建高度差双 ROI 工作流。
3. 创建三平面选择和局部 ROI 工作流。
4. 创建安装平面角度工作流。
5. 创建轴线与平面安装检测。
6. 从模型库选择 OBJ/PLY 并执行 3D 比较。
7. 未配置输出变量时阻止执行。
8. 点数不足显示 UNKNOWN。
9. 节点错误能定位画布节点。
10. 页面离开后不存在相机流和 Three.js 资源泄漏。

## 29. AI 生成禁止事项

代码生成 AI 不得：

- 只生成安装角度页面并称为完整前端。
- 硬编码 101 个算子及全部端口。
- 把 `"Operator"` 保存到算子节点 `type`。
- 虚构 `operatorId` 构造参数。
- 使用显示名作为绑定键。
- 忽略 output-config。
- 把 UNKNOWN 当 NG。
- 把低分辨率预览点云用于最终测量。
- 将 Plane UV 坐标当图像像素。
- 用两个平面法向推算 twistZ。
- 直接 `<img src>` 播放 BMP Multipart 流。
- 再次解包 BAYGB12Packed。
- 假设固定图像分辨率、点数或帧率。
- 根据未确认的方法名猜 ABP 路由。
- 伪装尚未实现的 CAD 有符号距离或盲区过滤能力。
- 生成 TODO、伪代码或空实现作为最终交付。

## 30. 完整验收标准

生成结果至少满足：

1. 动态加载并分类显示所有服务端节点。
2. 普通新增算子无需修改前端即可显示。
3. 算子节点保存 type 为 GUID。
4. 支持完整 CRUD、validate、simulate 和 output-config。
5. 支持 2D ROI 与三维平面 UV ROI。
6. 支持选择 A/B/C 平面及从原始点云提取区域点。
7. 支持图像、点云、网格和结构化结果。
8. 支持模型库上传、选择、下载和失效提示。
9. 支持扫描点云与工艺模型比较。
10. 支持四类安装角度检测。
11. 严格区分 OK、NG、UNKNOWN、ERROR。
12. 支持相机 BMP Multipart 预览。
13. 支持标定向导 HTTP 在线预览。
14. 未配置输出变量时给出明确提示。
15. 工作流节点错误可点击定位。
16. 页面卸载无流、URL、Three.js 或定时器泄漏。
17. 所有接口错误保留可诊断信息。
18. TypeScript 无隐式 any，核心测试通过。

## 31. 可直接交给代码生成 AI 的主提示词

```text
你是一名资深工业视觉前端架构师。请为 Aurora Struct3D 生成完整的第三方前端，
不能只生成某一个检测页面。

必须依次阅读：
1. 第三方前端-AI自动生成完整接入文档.md
2. 检测算子.md
3. 第三方相机预览接入文档.md
4. 第三方前端-安装角度检测AI生成接入文档.md

实现范围：
- 项目与工作流管理
- 动态算子面板和工作流画布
- 参数、端口、变量和输出配置
- 2D/3D ROI
- 图像、点云和网格展示
- 相机和标定在线预览
- 三维数模库
- 3D 模型比较
- 高度差、形状、面积和安装角度检测
- 工作流校验、仿真、试运行和结果页

强制约束：
- GET /api/app/workflow-node-palette 是算子元数据真源。
- 算子节点 graphData.nodes[].type 必须是算子 GUID。
- 不得虚构 operatorId 参数。
- 严格使用服务端端口和参数 name。
- 严格区分 OK、NG、UNKNOWN、ERROR。
- 必须配置并读取工作流输出变量。
- 相机预览必须按 Multipart + Content-Length 增量解析 BMP。
- 不得再次处理 BAYGB12Packed。
- 不得将预览降采样数据用于最终检测。
- 所有生命周期资源必须释放。

输出：
1. 架构说明和文件树。
2. 所有新增或修改文件的完整代码。
3. 依赖安装命令。
4. 环境变量示例。
5. 单元、集成和端到端测试。
6. 对照主文档第 30 节逐项说明验收结果。

不得输出伪代码、TODO 或省略关键文件。如果目标项目已有 HTTP 客户端、认证、
路由、状态管理或 UI 组件，优先复用并保持现有风格。
```
