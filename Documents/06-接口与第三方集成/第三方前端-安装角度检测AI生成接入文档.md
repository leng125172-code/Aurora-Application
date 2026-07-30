# 第三方前端安装角度检测接入规范（AI 代码生成版）

> 本文是安装角度检测专项附件，不是 Aurora Struct3D 第三方前端的完整系统
> 接入文档。生成完整前端时，必须先阅读
> `第三方前端-AI自动生成完整接入文档.md`，再使用本文补充安装角度细节。
>
> 本文档面向第三方前端开发者和代码生成 AI。将本文完整提供给 AI 后，应能生成
> 安装角度检测的工作流节点、属性面板、双 ROI 配置、结果展示和接口封装。
>
> 本文描述的是现有后端契约。不得自行修改 GUID、参数名、端口名、角度单位和
> `OK / NG / UNKNOWN` 语义。

## 1. 生成目标

生成一个可嵌入第三方前端的“安装角度检测”功能模块，至少包含：

1. 从服务端加载工作流节点面板。
2. 支持平面 ROI 选择和编辑。
3. 支持安装平面、轴线与平面、轴线与轴线、平面内旋转四类检测。
4. 支持节点端口连线和参数配置。
5. 保存、校验、执行工作流。
6. 展示角度值、拟合质量及 `OK / NG / UNKNOWN`。
7. 对 `UNKNOWN` 使用独立视觉状态，不得当成普通 NG。

推荐技术栈：

- Vue 3 + TypeScript + Composition API
- Pinia
- Axios
- 任意支持自定义锚点的流程图库
- Three.js 用于点云、平面、方向箭头和 ROI 预览

如果项目使用 React，应保留本文中的 TypeScript 数据模型、状态机和接口契约，
只替换组件实现。

## 2. 后端地址和认证

接口根地址由环境变量提供：

```text
VITE_API_BASE_URL=https://server.example.com
```

所有 `/api/app/**` 请求应使用项目现有认证机制。常见形式为：

```http
Authorization: Bearer <access-token>
Content-Type: application/json
```

不要在源码中硬编码服务器地址、令牌或项目 ID。

## 3. 服务端为唯一元数据来源

节点面板接口：

```http
GET /api/app/workflow-node-palette
```

响应结构：

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

前端必须：

- 以接口返回的输入、输出端口顺序生成锚点。
- 以 `name` 作为配置和绑定键，不能使用显示名。
- 允许 `controlType` 为数字或枚举名称字符串。
- 未识别的控件类型回退为通用输入框，不得导致页面崩溃。
- GUID 只用于识别需要专用 UI 的节点，不代替动态元数据。

特别注意：

- 面板响应中的 `nodeDefinition.nodeType === "Operator"` 表示节点分类。
- 保存到 `graphData.nodes[].type` 的算子节点必须是算子 GUID。
- 不要把 `"Operator"` 保存为算子节点的 `type`。
- 不要在 `properties.params` 中重复保存 `operatorId`。

当前 `PortControlType` 数字映射：

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

建议兼容函数：

```ts
export function isPlaneRoiEditor(value: number | string): boolean {
  return value === 10 || value === 'PlaneRoiEditor'
}
```

## 4. 安装角度相关算子

### 4.1 平面选择与 ROI

| 算子 | GUID | 用途 |
|---|---|---|
| 选择拟合平面 | `c38e27a6-8d49-4c41-96a0-a53f30c23101` | 从 A/B/C 拟合平面中选择一个 |
| 定义平面区域 | `d420473b-76f1-455a-83e4-492809c23102` | 在选中平面局部 UV 坐标中定义 ROI |
| 提取平面区域点云 | `e15fb284-2abe-477c-bd47-3cfde9c23103` | 从原始高密度点云提取 ROI 内全部点 |
| SVD 拟合 | `e4f23456-7890-1234-5678-90123456780d` | 对 ROI 内点重新拟合局部平面 |
| 3D 直线拟合 | `b1a10003-0003-4000-8000-000000000033` | 拟合边线、槽方向或轴线 |
| 圆柱拟合 | `b1a10002-0002-4000-8000-000000000032` | 拟合轴、销、管件的轴线 |

ROI 后必须重新拟合局部平面。不能直接把整个分割平面的旧拟合结果用于安装
角度检测，否则用户修改 ROI 后角度可能不变化。

### 4.2 第一版：安装平面角度检测

GUID：

```text
f261a395-3bcf-4e91-9a82-53f777e23104
```

输入端口：

| 顺序 | name | 含义 |
|---:|---|---|
| 0 | `reference_plane_params` | 基准平面参数 |
| 1 | `reference_points` | 基准面 ROI 点云 |
| 2 | `measured_plane_params` | 安装平面参数 |
| 3 | `measured_points` | 安装面 ROI 点云 |

输出端口：

```text
tilt_x
tilt_y
total_tilt
deviation_x
deviation_y
total_deviation
reference_rmse
measured_rmse
is_valid
is_ok
inspection_status
result_json
```

配置参数：

```ts
export interface InstallationPlaneAngleConfig {
  nominalTiltX: number
  nominalTiltY: number
  minDeviationX: number
  maxDeviationX: number
  minDeviationY: number
  maxDeviationY: number
  maxTotalDeviation: number
  maxFitRmse: number
  minPointCount: number
}
```

### 4.3 第二版：安装轴线与平面检测

GUID：

```text
a372b4a6-4cdf-4fa2-aa93-64a888e23105
```

输入端口：

```text
reference_plane_params
reference_points
axis_params
axis_points
```

`axis_params` 可以连接：

- 3D 直线拟合 `line_params`，格式为 6 个 double。
- 圆柱拟合 `cylinder_params`，格式为 7 个 double。

输出端口：

```text
tilt_x
tilt_y
axis_to_normal_angle
axis_to_plane_angle
is_valid
is_ok
inspection_status
result_json
```

配置参数：

```ts
export interface InstallationAxisPlaneConfig {
  nominalTiltX: number
  nominalTiltY: number
  maxDeviationX: number
  maxDeviationY: number
  maxTotalDeviation: number
  maxFitRmse: number
  minPointCount: number
}
```

UI 文案必须区分：

- `axis_to_normal_angle`：轴线与基准面法向量的夹角，垂直安装时接近 `0°`。
- `axis_to_plane_angle`：轴线与基准平面的夹角，垂直安装时接近 `90°`。

### 4.4 第二版：安装轴线夹角检测

GUID：

```text
b483c5b7-5de1-40b3-bba4-75b999e23106
```

输入端口：

```text
reference_axis_params
reference_axis_points
measured_axis_params
measured_axis_points
```

输出端口：

```text
axis_angle
angle_deviation
is_valid
is_ok
inspection_status
result_json
```

配置参数：

```ts
export interface InstallationAxisAxisConfig {
  nominalAngle: number       // 0～90
  maxAngleDeviation: number  // >= 0
  maxFitRmse: number
  minPointCount: number
}
```

轴线为无向几何特征。`axis_angle` 始终是 `0°～90°` 的最小夹角，不能根据
方向向量正负在 UI 中改成 `170°`。

### 4.5 第二版：安装平面内旋转检测

GUID：

```text
c594d6c8-6ef2-41c4-8cb5-86caaae23107
```

输入端口：

```text
reference_plane_params
reference_direction_params
reference_direction_points
measured_direction_params
measured_direction_points
```

方向可以来自边线、槽、孔中心连线或其他经过 3D 直线拟合的特征。

输出端口：

```text
twist_z
twist_deviation
is_valid
is_ok
inspection_status
result_json
```

配置参数：

```ts
export interface InstallationTwistConfig {
  nominalTwist: number
  maxTwistDeviation: number
  maxFitRmse: number
  minPointCount: number
}
```

`twist_z` 是绕基准平面法向的有符号角度，范围为 `-180°～180°`。

## 5. 平面 ROI 编辑器

“定义平面区域”节点的 `roiJson` 配置字段使用
`PlaneRoiEditor / 10` 控件。

用户操作：

1. 从上游获取选中平面参数和内点。
2. 在三维视图高亮当前平面。
3. 将相机视图切换到平面正视图。
4. 在平面局部 UV 坐标中绘制矩形或多边形。
5. 支持拖动顶点、删除顶点、清空和恢复整个平面范围。
6. 保存时只把用户编辑的 UV 点写入 `roiJson` 配置。

编辑器配置值：

```ts
export interface PlaneRoiConfig {
  points: Array<{ u: number; v: number }>
}
```

示例：

```json
{
  "points": [
    { "u": -10, "v": -5 },
    { "u": 10, "v": -5 },
    { "u": 10, "v": 5 },
    { "u": -10, "v": 5 }
  ]
}
```

写入工作流节点参数时，`roiJson` 本身是 JSON 字符串：

```ts
node.properties.params.roiJson = JSON.stringify(planeRoiConfig)
```

执行后，算子输出的 `plane_roi_json` 包含完整坐标系：

```ts
export interface PlaneRoiRuntimeValue {
  origin: [number, number, number]
  axisU: [number, number, number]
  axisV: [number, number, number]
  normal: [number, number, number]
  points: Array<{ u: number; v: number }>
}
```

空 `points` 表示由服务端使用平面内点包围矩形。前端应提示“使用整个平面”，
而不是提示配置错误。

同一角度检测必须允许配置两个独立 ROI，不得让基准 ROI 和被测 ROI 共用同一个
`roiJson` 对象引用。

## 6. 推荐工作流模板

### 6.1 平面与平面

```text
原始点云
 ├→ 选择基准平面 → 定义 ROI-A → 提取 ROI 点云 → SVD 拟合 ─┐
 └→ 选择安装平面 → 定义 ROI-B → 提取 ROI 点云 → SVD 拟合 ─┤
                                                            ↓
                                                  安装平面角度检测
```

“提取 ROI 点云”的 `input_point_cloud` 必须连接原始高密度点云。平面选择和
ROI 预览可以使用降采样点云。

### 6.2 轴线与平面

```text
基准面 ROI 点云 → SVD 拟合 ────────────┐
轴/圆柱 ROI 点云 → 直线拟合或圆柱拟合 ─┴→ 安装轴线与平面检测
```

### 6.3 轴线与轴线

```text
基准轴 ROI 点云 → 直线/圆柱拟合 ─┐
安装轴 ROI 点云 → 直线/圆柱拟合 ─┴→ 安装轴线夹角检测
```

### 6.4 twistZ

```text
基准平面 ROI → SVD 拟合 ──────────┐
基准方向 ROI → 3D 直线拟合 ───────┼→ 安装平面内旋转检测
安装方向 ROI → 3D 直线拟合 ───────┘
```

## 7. 工作流保存模型

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
  text?: { x?: number; y?: number; value?: string }
  properties?: {
    params?: Record<string, unknown>
    paramSources?: Record<string, 'literal' | 'variable'>
    inputBindings?: Record<string, string>
    inputBindingSources?: Record<string, 'literal' | 'variable'>
    outputBindings?: Record<string, string>
    outputBindingSources?: Record<string, 'literal' | 'variable'>
    innerGraphData?: WorkflowGraphDto
  }
}

export interface WorkflowEdgeDto {
  id: string
  sourceNodeId?: string
  targetNodeId?: string
  sourceAnchorIndex?: number
  targetAnchorIndex?: number
  properties?: { branch?: string }
}
```

算子节点：

```ts
const angleNode: WorkflowNodeDto = {
  id: crypto.randomUUID(),
  type: 'f261a395-3bcf-4e91-9a82-53f777e23104',
  x: 900,
  y: 300,
  text: { value: '安装平面角度检测' },
  properties: {
    params: {
      nominalTiltX: 0,
      nominalTiltY: 0,
      minDeviationX: -0.5,
      maxDeviationX: 0.5,
      minDeviationY: -0.5,
      maxDeviationY: 0.5,
      maxTotalDeviation: 0.7,
      maxFitRmse: 0.05,
      minPointCount: 30,
    },
    paramSources: {
      nominalTiltX: 'literal',
      nominalTiltY: 'literal',
      minDeviationX: 'literal',
      maxDeviationX: 'literal',
      minDeviationY: 'literal',
      maxDeviationY: 'literal',
      maxTotalDeviation: 'literal',
      maxFitRmse: 'literal',
      minPointCount: 'literal',
    },
    inputBindings: {},
    inputBindingSources: {},
    outputBindings: {
      tilt_x: 'install_tilt_x',
      tilt_y: 'install_tilt_y',
      total_tilt: 'install_total_tilt',
      is_valid: 'install_angle_valid',
      is_ok: 'install_angle_ok',
      inspection_status: 'install_angle_status',
      result_json: 'install_angle_result',
    },
    outputBindingSources: {
      tilt_x: 'variable',
      tilt_y: 'variable',
      total_tilt: 'variable',
      is_valid: 'variable',
      is_ok: 'variable',
      inspection_status: 'variable',
      result_json: 'variable',
    },
  },
}
```

保存请求：

```ts
export interface SaveWorkflowInput {
  projectId: string
  name: string
  graphData: WorkflowGraphDto
}
```

常用接口：

```http
GET  /api/app/workflow-node-palette
POST /api/app/workflow
GET  /api/app/workflow/{id}
PUT  /api/app/workflow/{id}
POST /api/app/workflow/{id}/validate
POST /api/app/workflow-execution/run
```

创建和更新前必须先进行前端校验，保存后再调用服务端校验。

## 8. 执行请求

```ts
export interface RunWorkflowInput {
  projectId: string
  workflowId: string
  inputVariableKeys?: string[]
  outputVariableNames?: string[]
  runtimeInstanceId?: string
  variableReadTimeoutMs?: number
}
```

示例：

```json
{
  "projectId": "PROJECT_GUID",
  "workflowId": "WORKFLOW_GUID",
  "outputVariableNames": [
    "install_tilt_x",
    "install_tilt_y",
    "install_total_tilt",
    "install_angle_valid",
    "install_angle_ok",
    "install_angle_status",
    "install_angle_result"
  ],
  "variableReadTimeoutMs": 5000
}
```

工作流必须配置输出变量。前端在执行前检查至少包含：

```text
inspection_status
is_valid
is_ok
result_json
```

否则阻止执行并提示“请先配置检测结果输出变量”。

## 9. 结果类型和 UI

```ts
export type InspectionStatus = 'OK' | 'NG' | 'UNKNOWN'

export interface InstallationInspectionViewModel {
  status: InspectionStatus
  isValid: boolean
  isOk: boolean
  primaryAngle: number | null
  angleItems: Array<{
    key: string
    label: string
    value: number
    unit: '°'
  }>
  qualityReasons: string[]
  rawResult: unknown
}
```

颜色建议：

- `OK`：绿色
- `NG`：红色
- `UNKNOWN`：黄色或灰色

状态规则：

```ts
export function normalizeInspectionStatus(
  status: unknown,
  isValid: unknown,
  isOk: unknown,
): InspectionStatus {
  if (isValid !== true || status === 'UNKNOWN') return 'UNKNOWN'
  if (isOk === true && status === 'OK') return 'OK'
  return 'NG'
}
```

不得使用 `isOk === false` 直接显示 NG，因为 UNKNOWN 时 `is_ok` 同样为 false。

结果卡片至少显示：

- 检测类型
- 基准 ROI 名称
- 被测 ROI 名称
- 标称角度
- 实测角度
- 角度偏差
- 允许公差
- 拟合 RMSE
- 有效点数
- 状态及质量原因

三维视图建议显示：

- 基准平面：蓝色半透明
- 被测平面或轴线：橙色
- 基准方向箭头：蓝色
- 被测方向箭头：橙色
- 超差方向：红色
- ROI 轮廓及名称

## 10. 前端组件拆分

代码生成 AI 应生成以下职责清晰的模块：

```text
src/
  api/
    workflow-api.ts
  types/
    workflow.ts
    installation-angle.ts
  stores/
    workflow-editor.ts
  components/workflow/
    OperatorNode.vue
    OperatorConfigPanel.vue
    PortAnchor.vue
  components/installation-angle/
    PlaneRoiEditor.vue
    InstallationAngleConfigPanel.vue
    InstallationAngleResultCard.vue
    InstallationAngle3DOverlay.vue
    InspectionStatusBadge.vue
  composables/
    useNodePalette.ts
    usePlaneRoi.ts
    useInstallationAngleResult.ts
```

组件要求：

- `PlaneRoiEditor` 只处理 UV ROI 编辑，不负责调用角度算法。
- `OperatorConfigPanel` 根据 `configFields` 动态渲染普通配置。
- 安装角度专用面板只提供更友好的组合 UI，最终仍写入节点 `params`。
- API、状态、Three.js 场景和表单逻辑分离。

## 11. 前端校验

保存前检查：

1. 两个 ROI 是否为不同节点实例。
2. 多边形点数为空或不少于 3；1～2 个点视为错误。
3. 所有角度和 RMSE 参数必须为有限数字。
4. 最小偏差不能大于最大偏差。
5. 最大绝对偏差、最大 RMSE、最大合成偏差不得为负数。
6. 最小点数：平面不少于 3，轴线不少于 2。
7. 输入端口必须连接类型兼容的输出端口。
8. 检测结果至少绑定 `inspection_status`、`is_valid`、`is_ok`。
9. twistZ 必须连接基准平面和两个方向特征。
10. 轴线-轴线标称角范围必须为 `0°～90°`。

## 12. 错误处理

统一处理：

- `400`：显示服务端校验详情，并定位相关节点。
- `401/403`：进入认证处理，不重复无限请求。
- `404`：提示工作流、项目或算子不存在。
- `409`：提示配置版本或变量冲突。
- `500`：显示服务端消息和执行 ID，不吞掉错误。

执行错误信息必须保留节点 ID，例如：

```text
工作流执行失败，节点 <node-id>：<message>
```

前端应支持点击错误消息定位并高亮节点。

## 13. AI 生成时的禁止事项

代码生成 AI 不得：

- 修改本文中的算子 GUID。
- 将 `"Operator"` 写入算子实例的 `graphData.nodes[].type`；这里必须写算子 GUID。
- 在 `properties.params` 中虚构 `operatorId` 参数。
- 将显示名当作端口绑定键。
- 假设端口顺序永远不变而跳过面板接口。
- 将 ROI UV 坐标当成图像像素坐标。
- 将降采样点云用于最终 ROI 点云提取。
- 将 `UNKNOWN` 显示为 NG。
- 用两平面法向量推算 twistZ。
- 将轴线方向正负翻转解释成 `180°` 安装错误。
- 将圆柱表面点到轴线的距离直接当作拟合误差。
- 在源码中硬编码令牌、项目 ID、工作流 ID 或服务器地址。
- 忽略保存前和执行前的输出变量配置。

## 14. 验收标准

生成的前端必须通过以下人工或自动化验收：

1. 能从节点面板找到四个安装角度算子。
2. 能分别绘制和保存基准 ROI、安装 ROI。
3. 修改任一 ROI 后，相应局部拟合和角度结果会更新。
4. 平面安装模拟 `tiltX=2°、tiltY=-1°` 时显示对应有符号值。
5. 轴线垂直基准面时：
   - `axis_to_normal_angle` 接近 `0°`
   - `axis_to_plane_angle` 接近 `90°`
6. 两轴方向分别为 `10°` 和 `190°` 的等价表达时，最小夹角仍为 `10°`。
7. twistZ 逆时针 `15°` 时显示 `+15°`，反向时显示负值。
8. ROI 点数不足时显示 UNKNOWN 和质量原因。
9. 拟合 RMSE 超限时显示 UNKNOWN，不显示普通 NG。
10. 工作流未配置输出变量时，执行按钮被阻止并提示配置。
11. 服务端返回节点错误时能定位对应画布节点。
12. 页面刷新后 ROI、参数、连线和输出绑定均能正确回显。

## 15. 可直接提供给代码生成 AI 的任务指令

```text
请严格依据《第三方前端-安装角度检测AI生成接入文档.md》实现第三方工作流前端。

要求：
1. 先读取 GET /api/app/workflow-node-palette，以服务端元数据作为节点和端口真源。
2. 使用 TypeScript 严格类型。
3. 实现 PlaneRoiEditor、四类安装角度配置及结果卡片。
4. 支持工作流保存、服务端校验和运行。
5. 严格区分 OK、NG、UNKNOWN。
6. 不修改算子 GUID、端口名和参数名。
7. 补充单元测试，覆盖本文第 14 节验收标准中的核心状态和角度方向。
8. 输出完整文件，不使用伪代码或 TODO 占位。
```
