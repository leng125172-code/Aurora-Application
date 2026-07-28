# ROI 统一底图接口前端修改说明

## 1. 修改目标

二维 ROI 和平面 ROI 统一调用同一个底图接口：

```http
POST /api/app/workflow/roi-base-image
```

前端不需要根据算子 UUID 选择不同接口，也不要自行判断节点是否具有
`input_mat`。后端会根据 ROI 节点的输入绑定自动识别：

- `input_mat`：普通二维 ROI。
- `plane_params + plane_points`：平面 ROI。

## 2. 调用时机

以下两种参数控件打开编辑器时，都调用统一底图接口：

- `RoiEditor`
- `PlaneRoiEditor`

建议调用顺序：

1. 将当前画布序列化为 `graphData`。
2. 调用 `roi-base-image`。
3. 使用响应中的 `blobName`加载底图。
4. 使用响应中的 `roiJson`整体更新当前节点的 `properties.params.roiJson`。
5. 打开 ROI 编辑器。

如果当前工作流已经保存，可以只传 `workflowId`；如果画布存在未保存修改，必须同时传
最新的 `graphData`，后端会优先使用它执行预运行。

## 3. 请求参数

```ts
interface GenerateRoiBaseImageInput {
  projectId: string;
  workflowId?: string;
  roiNodeId: string;
  graphData?: string;
  inputVariableBindings?: Record<string, string>;
  inputVariableKeys?: Array<{
    variableName: string;
    key: string;
  }>;
}
```

最小请求示例：

```json
{
  "projectId": "3a22a4d3-8e48-81b5-0495-32458dceeda5",
  "workflowId": "3a22aaa3-3e1b-3d8c-1bb5-bd04b3c52a26",
  "roiNodeId": "51a1c78d9f144f63a454a08105442091"
}
```

注意：`roiNodeId`必须是当前正在编辑的 ROI 节点 ID，不能传上游节点 ID、历史节点
ID 或其他 ROI 节点 ID。

未保存画布示例：

```ts
const result = await workflowApi.generateRoiBaseImage({
  projectId,
  workflowId,
  roiNodeId: node.id,
  graphData: JSON.stringify(currentGraph),
});
```

## 4. 响应处理

```ts
interface RoiBaseImageResult {
  error: boolean;
  errorCode?: string | null;
  message?: string | null;
  blobName?: string | null;
  roiJson?: string | null;
  persisted: boolean;
}
```

成功响应示例：

```json
{
  "error": false,
  "errorCode": null,
  "message": null,
  "blobName": "operator-files/xxx/roi-base-image.png",
  "roiJson": "{\"points\":[],\"baseImage\":{\"selectedBlobName\":\"operator-files/xxx/roi-base-image.png\",\"projectionMapping\":{\"viewLabel\":\"UV\",\"worldMinX\":-20.5,\"worldMaxX\":20.5,\"worldMinY\":-10.5,\"worldMaxY\":10.5,\"imageWidth\":1024,\"imageHeight\":528},\"graphHash\":\"...\"}}",
  "persisted": true
}
```

前端必须先检查 `error`，不能只判断 HTTP 状态：

```ts
if (result.error) {
  message.error(result.message || 'ROI 底图生成失败');
  return;
}

if (!result.blobName || !result.roiJson) {
  message.error('ROI 底图响应不完整');
  return;
}

node.properties ??= {};
node.properties.params ??= {};
node.properties.params.roiJson = result.roiJson;
```

`persisted`的含义：

- `true`：后端已把更新后的 `roiJson`写回已保存工作流。
- `false`：工作流尚未保存，前端必须把响应中的 `roiJson`写回当前画布节点，并在后续
  保存工作流时一并提交。

## 5. 不要再按 `input_mat`拦截

需要删除或修改类似下面的前端校验：

```ts
// 错误：PlaneRoiEditor 本来就没有 input_mat。
if (!node.properties?.inputBindings?.input_mat) {
  throw new Error('ROI 节点未绑定 input_mat');
}
```

统一接口的调用条件只需要确认：

```ts
const isRoiEditor =
  parameter.controlType === 'RoiEditor' ||
  parameter.controlType === 'PlaneRoiEditor';
```

节点究竟属于二维 ROI 还是平面 ROI，由后端根据输入端口判断。

## 6. `roiJson`数据结构

### 6.1 普通二维 ROI

```json
{
  "rois": [
    {
      "name": "平面度区域",
      "type": "Rect",
      "x": 40,
      "y": 80,
      "width": 180,
      "height": 180,
      "rotation": 0
    }
  ],
  "baseImage": {
    "selectedBlobName": "operator-files/xxx/roi-base-image.png",
    "projectionMapping": {
      "viewLabel": "XY",
      "worldMinX": 0,
      "worldMaxX": 640,
      "worldMinY": 0,
      "worldMaxY": 480,
      "imageWidth": 640,
      "imageHeight": 480
    },
    "graphHash": "..."
  }
}
```

二维 ROI 的 `rois`仍使用图像像素坐标，现有编辑逻辑可以保持不变。

### 6.2 平面 ROI

```json
{
  "points": [
    { "u": -10.0, "v": -5.0 },
    { "u": 10.0, "v": -5.0 },
    { "u": 10.0, "v": 5.0 },
    { "u": -10.0, "v": 5.0 }
  ],
  "baseImage": {
    "selectedBlobName": "operator-files/xxx/roi-base-image.png",
    "projectionMapping": {
      "viewLabel": "UV",
      "worldMinX": -20.5,
      "worldMaxX": 20.5,
      "worldMinY": -10.5,
      "worldMaxY": 10.5,
      "imageWidth": 1024,
      "imageHeight": 528
    },
    "graphHash": "..."
  }
}
```

平面 ROI 的 `points`是平面局部坐标，不是图像像素坐标：

- `u`对应 `worldMinX ~ worldMaxX`。
- `v`对应 `worldMinY ~ worldMaxY`。
- `viewLabel`固定为 `UV`。

前端保存平面 ROI 时必须继续使用 `{u, v}`，不能保存成 `{x, y}`。

## 7. 平面 ROI 坐标转换

假设底图尺寸和映射为：

```ts
interface ProjectionMapping {
  viewLabel: string;
  worldMinX: number;
  worldMaxX: number;
  worldMinY: number;
  worldMaxY: number;
  imageWidth: number;
  imageHeight: number;
}
```

### 7.1 像素坐标转 UV

底图的 Y 轴向下，平面 V 轴向上，因此 V 方向需要翻转：

```ts
function pixelToUv(
  x: number,
  y: number,
  mapping: ProjectionMapping,
) {
  const u =
    mapping.worldMinX +
    (x / Math.max(1, mapping.imageWidth - 1)) *
      (mapping.worldMaxX - mapping.worldMinX);

  const v =
    mapping.worldMaxY -
    (y / Math.max(1, mapping.imageHeight - 1)) *
      (mapping.worldMaxY - mapping.worldMinY);

  return { u, v };
}
```

### 7.2 UV 转像素坐标

用于回显已经保存的平面 ROI：

```ts
function uvToPixel(
  u: number,
  v: number,
  mapping: ProjectionMapping,
) {
  const x =
    ((u - mapping.worldMinX) /
      (mapping.worldMaxX - mapping.worldMinX)) *
    Math.max(1, mapping.imageWidth - 1);

  const y =
    ((mapping.worldMaxY - v) /
      (mapping.worldMaxY - mapping.worldMinY)) *
    Math.max(1, mapping.imageHeight - 1);

  return { x, y };
}
```

如果编辑器画布对图片进行了缩放，还需要先把屏幕坐标换算为原始图片像素坐标：

```ts
const imageX = (screenX - imageOffsetX) / imageScale;
const imageY = (screenY - imageOffsetY) / imageScale;
const uv = pixelToUv(imageX, imageY, mapping);
```

## 8. 推荐的统一处理代码

```ts
async function prepareRoiEditor(
  node: WorkflowNode,
  parameter: OperatorParameter,
) {
  if (
    parameter.controlType !== 'RoiEditor' &&
    parameter.controlType !== 'PlaneRoiEditor'
  ) {
    return;
  }

  const response = await workflowApi.generateRoiBaseImage({
    projectId: currentProjectId,
    workflowId: currentWorkflowId,
    roiNodeId: node.id,
    graphData: JSON.stringify(getCurrentGraph()),
  });

  if (response.error) {
    throw new Error(response.message || 'ROI 底图生成失败');
  }

  if (!response.roiJson || !response.blobName) {
    throw new Error('ROI 底图响应缺少 roiJson 或 blobName');
  }

  node.properties ??= {};
  node.properties.params ??= {};
  node.properties.params.roiJson = response.roiJson;

  const roiConfig = JSON.parse(response.roiJson);
  const mapping = roiConfig.baseImage?.projectionMapping;

  if (!mapping) {
    throw new Error('ROI 底图缺少 projectionMapping');
  }

  await openRoiEditor({
    mode: mapping.viewLabel === 'UV' ? 'plane' : 'image',
    imageBlobName: response.blobName,
    roiConfig,
    mapping,
  });
}
```

编辑器模式应优先根据当前参数的 `controlType`决定；`viewLabel === "UV"`可以作为响应
校验或兼容性判断，不建议把算子 UUID 写死在前端。

## 9. 错误提示建议

接口返回以下错误时直接显示后端的 `message`：

- 找不到 `roiNodeId`：检查传入的是否为当前节点 ID。
- 无法识别 ROI 节点输入：检查节点输入绑定是否完整。
- 平面参数无效：检查 `plane_params`上游输出。
- 平面点云无效：检查 `plane_points`上游输出。
- 预运行到 ROI 上游失败：显示具体失败节点及原因。

不要再把所有错误统一显示为“未绑定 input_mat”，因为平面 ROI 不需要该输入。

## 10. 验收清单

- 普通 `RoiEditor`仍能生成、显示和缓存二维底图。
- `PlaneRoiEditor`不会再出现“未绑定 input_mat”。
- 安装面节点能显示平面点云的 UV 投影底图。
- 平面 ROI 绘制后保存为 `{u, v}`点数组。
- 关闭并重新打开编辑器时，已有平面 ROI 能正确回显。
- 未保存画布能够使用最新 `graphData`生成底图。
- 接口返回 `persisted: false`时，前端会把 `roiJson`写回当前画布。
- 切换上游输入后重新生成底图，不继续使用旧的 `graphHash`缓存。
