# AuroraStruct3D 第三方工作流与 AI 生成开发指南

> 适用对象：第三方算法开发者、前端开发者、系统集成商，以及使用大模型自动生成工作流的团队。  
> 脚本版本：Aurora Workflow Script V2。  
> 本文中的代码标识符使用英文；工作流名称、节点标题和说明文字可以使用中文。

## 1. 工作流是什么

AuroraStruct3D 工作流由三层数据共同表达：

1. **算子实现**：实现 `IOperator` 的 C# class，负责实际算法。
2. **工作流脚本**：面向开发者和 AI 的 V2 英文调用脚本。
3. **GraphData**：面向画布编辑器的节点、端口绑定和连线 JSON。

服务端负责在脚本和 GraphData 之间转换。第三方不应自行维护两套互相独立的数据。

推荐流程：

```text
算子注册信息
    ↓
AI 或开发者生成 V2 脚本
    ↓
source/diagnostics 校验
    ↓
source/to-graph 转成画布
    ↓
保存工作流
    ↓
调试或正式执行
```

## 2. V2 脚本快速示例

```csharp
// Aurora Workflow Script V2 - operator call syntax
// @node {"id":"start","x":560,"y":40,"title":"开始","externalInputs":[]}
Workflow("3D 配准演示", 2);

// @node {"id":"read-reference","x":320,"y":140,"title":"读取参考点云"}
var referenceCloud = read_point_cloud(
    "reference.ply"
);

// @node {"id":"read-target","x":800,"y":140,"title":"读取目标点云"}
var targetCloud = read_point_cloud(
    "target.ply"
);

// @node {"id":"icp","x":560,"y":300,"title":"ICP 配准"}
var (alignedCloud, transformMatrix, registrationResult) = icp_registration(
    targetCloud,
    referenceCloud,
    maxCorrespondenceDistance: 0.1,
    maxIterations: 50
);

// @node {"id":"save","x":560,"y":460,"title":"保存配准点云"}
var (blobName, alignedCloudUrl) = save_point_cloud_to_blob(
    alignedCloud,
    fileName: "aligned-cloud.ply"
);

// @node {"id":"end","x":560,"y":620,"title":"结束"}
Return(alignedCloudUrl, transformMatrix, registrationResult);
```

脚本保存后，中文应直接显示为中文，不应生成 `\uXXXX`。解析器仍兼容历史脚本中的 Unicode 转义。

## 3. V2 语法规则

### 3.1 工作流声明

每个脚本必须且只能包含一个声明：

```csharp
Workflow("工作流名称", 2);
```

- `2` 是当前语言版本。
- 工作流名称允许中文。
- 名称只是字符串，不是代码标识符。

### 3.2 算子调用名称

调用名称必须是实现 `IOperator` 的 **C# class 简名**，严格区分大小写。

如果 class 为：

```csharp
public sealed class icp_registration : IOperator
```

脚本调用必须为：

```csharp
icp_registration(...)
```

不得使用：

- UI 显示名称；
- 算子 GUID；
- 自行翻译的名称；
- class 全限定名；
- 未注册的别名。

### 3.3 参数顺序

参数顺序固定：

1. `InputVisionParameters` 中的输入端口，按声明顺序使用位置参数。
2. `ConfigParameters` 中的配置项，使用英文命名参数。

```csharp
var output = some_operator(
    inputA,
    inputB,
    threshold: 0.25,
    enabled: true
);
```

禁止把配置参数写成位置参数，也不要随意交换输入端口顺序。

### 3.4 输出数量

无输出：

```csharp
notify_operator(message);
```

单输出：

```csharp
var grayImage = color_to_grayscale(inputImage);
```

多输出：

```csharp
var (binaryImage, thresholdValue) = otsu_threshold(inputImage);
```

Tuple 变量必须与 `OutputVisionParameters` 的声明顺序完全一致。输出数量不设上限。

即使某个输出暂时不被下游使用，也必须声明对应变量，否则 GraphData 的输出端口契约不完整。

### 3.5 输入值

输入支持：

```csharp
variableName
"string value"
123
12.5
true
false
null
```

- 变量引用不能加引号。
- 字符串常量必须加引号。
- 可选输入缺失时使用 `null`，不要使用 `_null`。
- 先定义变量，再引用变量。

### 3.6 返回值

`Return(...)` 定义工作流对外输出：

```csharp
Return(resultUrl, measurement, isOk);
```

- 可以返回一个或多个变量。
- 顺序即调用方看到的输出顺序。
- 只能返回已经定义的变量或合法输出路径。

## 4. `@node` 画布元数据

每个调用前可以添加一条元数据注释：

```csharp
// @node {"id":"node-001","x":560,"y":240,"textX":560,"textY":240,"title":"平面拟合"}
```

字段说明：

| 字段 | 必填 | 说明 |
|---|---:|---|
| `id` | 推荐 | 节点稳定 ID；画布局部更新依赖它 |
| `x`、`y` | 否 | 节点位置 |
| `textX`、`textY` | 否 | 标题位置 |
| `title` | 否 | UI 显示标题，允许中文 |
| `externalInputs` | 仅开始节点 | 工作流外部输入变量 |

规则：

- 元数据只影响画布，不参与算子执行。
- 元数据必须是单行合法 JSON。
- 中文直接写中文。
- 缺少元数据时，服务端会生成 ID 并自动布局。
- AI 修改已有脚本时应保留原节点 ID。
- 连线不写入脚本，由变量定义和引用自动推导。

## 5. 第三方算子开发

### 5.1 基本结构

```csharp
using AuroraStruct3D.OpenCV;
using AuroraStruct3D.OpenCV.Workflow;

public sealed class calculate_height : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
    [
        new PointCloudData
        {
            ParameterName = "input_point_cloud",
            DisplayName = "输入点云",
        },
    ];

    public static List<IVisionParameter>? OutputVisionParameters =>
    [
        new VisionParameter<double>
        {
            ParameterName = "height",
            ParameterType = typeof(double),
            DisplayName = "高度",
        },
        new VisionParameter<bool>
        {
            ParameterName = "is_ok",
            ParameterType = typeof(bool),
            DisplayName = "是否合格",
        },
    ];

    public static List<IConfigParameter>? ConfigParameters =>
    [
        new ConfigParameter
        {
            Name = "minHeight",
            DisplayName = "最小高度",
            ParameterType = typeof(double),
            DefaultValue = "0",
            Required = false,
        },
        new ConfigParameter
        {
            Name = "maxHeight",
            DisplayName = "最大高度",
            ParameterType = typeof(double),
            DefaultValue = "100",
            Required = false,
        },
    ];

    private readonly double _minHeight;
    private readonly double _maxHeight;

    public calculate_height(double minHeight = 0, double maxHeight = 100)
    {
        _minHeight = minHeight;
        _maxHeight = maxHeight;
    }

    public void Execute(IWorkflowContext context)
    {
        PointCloudData cloud = context.Get<PointCloudData>("input_point_cloud");
        double height = Calculate(cloud);
        context.Set("height", height);
        context.Set("is_ok", height >= _minHeight && height <= _maxHeight);
    }

    public void Dispose()
    {
    }

    private static double Calculate(PointCloudData cloud)
    {
        // 第三方算法实现
        return 0;
    }
}
```

对应脚本：

```csharp
var (height, isOk) = calculate_height(
    inputCloud,
    minHeight: 10,
    maxHeight: 20
);
```

### 5.2 算子开发约束

- class 简名必须全局唯一。
- `ParameterName` 和配置 `Name` 必须使用稳定的英文标识符。
- 输入、输出、配置的声明顺序属于公开契约，不要随意调整。
- `Execute` 必须使用端口名读写，不要使用脚本变量名。
- 每个声明的输出都必须执行 `context.Set(...)`。
- OpenCV `Mat` 等非托管对象必须明确所有权并正确释放。
- 算子不要依赖 UI 状态。
- 异常消息应说明节点输入、配置或算法失败原因。
- 不要把 API 密钥、密码、绝对开发机路径写入算子。
- 配置用于构造算法；工作流变量通过输入端口传递。

### 5.3 类型名称

变量库的类型名最大长度为 128。系统会规范化泛型类型，例如：

```text
System.Collections.Generic.List<OpenCvSharp.Mat>
```

第三方不得保存带程序集版本、公钥和 Culture 的超长 Assembly Qualified Name。

## 6. API 接入

基础路径：

```text
/api/app/workflow
```

### 6.1 获取工作流源码

```http
GET /api/app/workflow/{workflowId}/source
```

重要返回字段：

```json
{
  "workflowId": "...",
  "name": "工作流名称",
  "sourceCode": "Workflow(\"工作流名称\", 2);",
  "languageVersion": 2,
  "revision": 3,
  "concurrencyStamp": "...",
  "graphData": {
    "nodes": [],
    "edges": []
  }
}
```

### 6.2 GraphData 转源码

```http
POST /api/app/workflow/graph/to-source
Content-Type: application/json
```

```json
{
  "name": "工作流名称",
  "graphData": {
    "nodes": [],
    "edges": []
  }
}
```

响应可能是 `text/plain`，客户端不能强制按 JSON 解析。

### 6.3 源码转 GraphData

```http
POST /api/app/workflow/source/to-graph
Content-Type: application/json
```

```json
{
  "sourceCode": "Workflow(\"Demo\", 2);\nReturn();"
}
```

### 6.4 源码诊断

```http
POST /api/app/workflow/source/diagnostics
```

```json
{
  "sourceCode": "...",
  "documentVersion": 1,
  "offset": 0,
  "workflowId": "...",
  "projectId": "..."
}
```

保存前必须确认没有 error 级诊断。

### 6.5 代码提示与签名帮助

```http
POST /api/app/workflow/source/completions
POST /api/app/workflow/source/signature-help
POST /api/app/workflow/source/hover
POST /api/app/workflow/source/format
```

AI 或自定义编辑器应优先使用这些接口取得当前部署版本的算子名称、参数及提示，不要依赖过期的静态列表。

### 6.6 保存源码

```http
PUT /api/app/workflow/{workflowId}/source
```

```json
{
  "sourceCode": "...",
  "expectedRevision": 3,
  "baseRevision": 3,
  "documentVersion": 12,
  "editOrigin": "source",
  "concurrencyStamp": "..."
}
```

保存后使用响应中的新 `revision` 和 `concurrencyStamp`，不要继续提交旧值。

## 7. 调试流程

### 7.1 启动调试

```http
POST /api/app/workflow/source/debug
```

```json
{
  "projectId": "...",
  "workflowId": "...",
  "sourceCode": "...",
  "mode": 1,
  "loopCount": 1
}
```

成功：

```json
{
  "error": false,
  "executionId": "非零 GUID",
  "message": null,
  "status": {
    "isPaused": true,
    "currentNodeId": "...",
    "executedSteps": 0,
    "variables": []
  }
}
```

失败时可能仍返回 HTTP 200，但 `error=true` 且 `executionId` 为零 GUID。客户端必须先检查 `error` 和 `executionId`，不能直接调用继续或单步。

### 7.2 调试操作

```text
POST   /api/app/workflow/executions/{executionId}/steps
POST   /api/app/workflow/executions/{executionId}/continue
POST   /api/app/workflow/executions/{executionId}/pause
POST   /api/app/workflow/executions/{executionId}/step-into
POST   /api/app/workflow/executions/{executionId}/step-over
POST   /api/app/workflow/executions/{executionId}/step-out
POST   /api/app/workflow/executions/{executionId}/run-to
PUT    /api/app/workflow/executions/{executionId}/breakpoints
GET    /api/app/workflow/executions/{executionId}/stack
GET    /api/app/workflow/executions/{executionId}/variables
GET    /api/app/workflow/executions/{executionId}/trace
GET    /api/app/workflow/executions/{executionId}/performance
POST   /api/app/workflow/executions/{executionId}/watch
DELETE /api/app/workflow/executions/{executionId}
```

操作语义：

| 操作 | 行为 |
| --- | --- |
| 调试 | 创建内存调试会话，停在第一个可执行节点之前 |
| 继续 | 运行到下一个有效断点；没有后续断点时运行到结束 |
| 单步/进入节点 | 执行当前节点，然后停在下一个节点 |
| 越过节点 | 当前扁平工作流中与“进入节点”相同 |
| 跳出工作流 | 顶层工作流没有调用者，因此运行剩余节点到结束 |
| 运行到节点 | 在目标节点执行前暂停 |
| 暂停 | 请求在当前节点执行结束后暂停；不能中断正在执行的同步算子 |
| 停止 | 删除调试会话；后续操作必须重新启动调试 |

当前运行时以算子节点为最小调试单位，不支持进入算子的 C# 实现逐行调试。只有未来引入子工作流调用栈后，“进入”和“越过”才会产生不同效果。

### 7.3 设置断点

断点必须在成功取得非零 `executionId` 后提交：

```http
PUT /api/app/workflow/executions/{executionId}/breakpoints
Content-Type: application/json
```

```json
{
  "breakpoints": [
    {
      "nodeId": "ba94eff41af44c338bc53f259fbf8aaa",
      "enabled": true
    }
  ]
}
```

也可以使用稳定的 `statementId`，并可选传入 `condition`、`hitCount` 或
`logMessage`。每次 PUT 表示替换该会话的完整断点集合，不是增量追加。

官方 IDE 支持两种设置方式：

1. 选中画布节点，点击“设置断点”。
2. 双击画布节点切换断点。

红框表示断点，绿色高亮表示当前将要执行的节点。开始调试前在前端暂存的断点，
应在取得 `executionId` 后立即同步到后端。

### 7.4 步进与运行到节点请求

单步请求：

```json
{
  "steps": 1,
  "includeVariables": true
}
```

运行到节点请求：

```json
{
  "nodeId": "目标节点 ID"
}
```

每次调试操作后，客户端应：

1. 用 `currentNodeId` 高亮当前节点。
2. 自动定位画布。
3. 刷新变量、调用栈、监视、轨迹和性能列表。
4. 会话完成或停止后禁用调试按钮。

服务重启后，内存中的调试会话会失效，必须重新启动调试。

### 7.5 调试状态机

前端不得再根据 `currentNodeId`、`status` 数值或 HTTP 请求是否返回来猜测调试状态。
所有调试状态响应新增以下字段：

```json
{
  "debugState": "paused",
  "isTerminal": false,
  "isPaused": true,
  "canContinue": true,
  "canStep": true,
  "canPause": false,
  "canStop": true,
  "executedSteps": 3,
  "totalSteps": 12,
  "currentNodeId": "..."
}
```

`debugState` 的取值和前端行为：

| 状态 | 含义 | 前端行为 |
| --- | --- | --- |
| `ready` | 会话创建成功，尚未执行首节点 | 显示“已就绪”，允许继续、单步和停止 |
| `running` | 正在执行一个或多个节点 | 显示动态蓝色状态，禁用继续和单步，允许请求暂停 |
| `paused` | 停在断点、运行到节点目标或单步之后 | 显示黄色暂停状态，允许继续和单步 |
| `completed` | 所有节点执行完成 | 显示绿色完成状态，禁用所有执行按钮 |
| `faulted` | 节点执行失败 | 显示红色失败状态，展示 `faultNodeId` 和 `errorMessage` |
| `stopped` | 用户停止会话 | 显示灰色停止状态；再次调试必须创建新会话 |

推荐状态转换：

```text
未调试 -> ready -> running -> paused -> running -> completed
                            \-> faulted
              \-> stopped
```

后端调试通知事件包括 `running`、`paused`、`pause-requested`、`faulted` 和
`session-ended`。前端发出继续、单步或运行到节点请求时，也应立即进入本地
`running` 状态，避免网络往返期间仍显示“已暂停”；收到响应或通知后，以服务端
`debugState` 为准。

旧客户端可以继续使用 `isPaused`，但新客户端必须优先使用 `debugState` 和
`canContinue/canStep/canPause/canStop`。服务重启、会话过期或接口返回“执行会话
不存在”时，前端应切换到“未调试/会话失效”，清空 `executionId` 并提示重新调试。

### 7.6 输出绑定类型错误

每个输出端口必须绑定独立变量，变量类型必须与算子契约一致。禁止把两个不同输出
绑定到同一变量。例如 `roi_partition` 的三个输出应分别绑定：

```text
primary_mask  -> Mat
output_masks  -> List<Mat>
roi_metadata  -> String
```

新版本会在编译或创建运行语句时拦截重复输出变量。旧版本可能执行到节点后才出现
“期望 `List<Mat>`，实际 `String`”的错误。第三方或 AI 生成 GraphData 时，不要依据
显示名称猜测输出顺序，应严格使用算子注册表返回的端口名称、类型和顺序。

## 8. 面向 AI 生成工作流

### 8.1 不要让 AI 猜算子

生成前必须提供：

- 当前节点面板或算子注册表；
- 每个算子的 class 简名；
- 输入端口名称、类型和顺序；
- 输出端口名称、类型和顺序；
- 配置参数名称、类型、默认值和范围；
- 用户期望的工作流输入和输出。

AI 不应根据中文标题猜测调用名称。

### 8.2 推荐生成过程

```text
1. 获取当前服务的算子契约
2. 将用户需求拆成有向无环数据流
3. 为每个节点选择精确 class 名
4. 按输入端口顺序连接变量
5. 为所有输出声明唯一英文变量
6. 用命名参数填写配置
7. 生成 @node 元数据
8. 生成 Return(...)
9. 调用 diagnostics
10. 根据诊断修复，直到无 error
11. 调用 source/to-graph 验证可生成画布
12. 保存或交给用户审核
```

### 8.3 AI 系统提示词模板

```text
You generate Aurora Workflow Script V2.

Rules:
1. Output only one complete workflow script unless explanation is requested.
2. Start with Workflow("name", 2); and end with Return(...).
3. Operator call names must exactly match registered IOperator class names.
4. Use positional arguments for InputVisionParameters in declared order.
5. Use English named arguments for ConfigParameters.
6. Declare every OutputVisionParameters item in declared order.
7. Use var for one output and tuple deconstruction for multiple outputs.
8. Use unique valid English C# variable names.
9. Never use _null; use null for an unbound optional input.
10. Preserve existing @node IDs when editing.
11. Metadata keys and identifiers are English; title strings may be Chinese.
12. Do not emit Node(...), Edge(...), Input(...), Output(...), Param(...), or GraphBegin(...).
13. Do not invent operators, ports, configurations, or output order.
14. Return only variables that are defined.
15. Ask for the operator contract if required information is missing.
```

将当前算子契约附在提示词后，再附用户需求。

### 8.4 AI 用户提示词模板

```text
请生成一个 Aurora Workflow Script V2：

工作流名称：工件高度检测
输入：点云文件路径
处理：
1. 读取点云
2. 体素下采样，voxelSize=0.2
3. 统计滤波
4. 计算高度
5. 保存处理后的点云
输出：高度、是否合格、点云下载 URL

要求：
- 节点标题使用中文。
- 代码变量使用英文 camelCase。
- 保留所有算子输出，未使用输出也要声明。
- 使用以下算子契约，不允许猜测或替换算子：
[粘贴服务端算子契约]
```

### 8.5 AI 输出自检

AI 输出后逐项检查：

- [ ] `Workflow(..., 2)` 存在且唯一。
- [ ] `Return(...)` 存在。
- [ ] 没有 V1 的 `Node/Edge/GraphBegin`。
- [ ] 算子 class 名完全匹配。
- [ ] 输入位置顺序正确。
- [ ] 配置参数全部为英文命名参数。
- [ ] 所有输出均已声明，Tuple 顺序正确。
- [ ] 变量名唯一、先定义后使用。
- [ ] 没有 `_null`。
- [ ] 没有硬编码密码、Token 或开发机绝对路径。
- [ ] `@node` JSON 合法，已有 ID 未变化。
- [ ] 中文直接显示，没有不必要的 `\uXXXX`。
- [ ] diagnostics 无 error。
- [ ] source/to-graph 转换成功。

## 9. GraphData 注意事项

如果第三方必须直接生成 GraphData，每个节点至少包含：

```json
{
  "id": "stable-node-id",
  "type": "operator-guid",
  "x": 560,
  "y": 240,
  "text": {
    "x": 560,
    "y": 240,
    "value": "节点标题"
  },
  "properties": {
    "params": {},
    "paramSources": {},
    "inputBindings": {},
    "inputBindingSources": {},
    "outputBindings": {},
    "outputBindingSources": {}
  }
}
```

source map 必须与绑定 map 的键完全对应：

```text
params                ↔ paramSources
inputBindings         ↔ inputBindingSources
outputBindings        ↔ outputBindingSources
```

来源值通常为：

```text
literal
variable
```

不得写成 `outputSources`，正确字段是 `outputBindingSources`。

推荐第三方生成 V2 脚本，再调用服务端转换成 GraphData，而不是手写 GraphData。

## 10. 常见错误

### 算子不存在

原因：使用了显示名称、错误大小写或 AI 编造名称。  
处理：重新读取注册表，使用 class 简名。

### 输出数量不一致

原因：算子升级后增加输出，旧脚本只声明部分输出。  
处理：按最新 `OutputVisionParameters` 补齐 Tuple。

### source map 缺少键

原因：直接修改 GraphData 时只更新了绑定，没有更新来源 map。  
处理：确保两侧键集合完全相同。

### 类型名超过 128

原因：第三方写入 Assembly Qualified Name。  
处理：使用系统规范化短类型，不要自行保存程序集限定名。

### `_null` 未定义

原因：历史生成器把可选输入写成 `_null`。  
处理：新脚本使用 `null`。

### 执行会话不存在：零 GUID

原因：调试启动已经失败，但客户端仍继续调用调试操作。  
处理：检查启动结果的 `error`、`message` 和非零 `executionId`。

### Worker 404

原因：Monaco Worker 没有进入前端部署产物。  
处理：使用项目提供的 Worker 注册方式，并完整发布 `wwwroot/assets`。

## 11. 交付验收清单

第三方交付前应提供：

- [ ] 算子源码及依赖说明。
- [ ] 算子 class 名、GUID、显示名称及分类。
- [ ] 输入、输出和配置契约。
- [ ] 至少一个完整 V2 示例脚本。
- [ ] 正常输入、边界输入和错误输入测试。
- [ ] 资源释放和并发说明。
- [ ] diagnostics 校验截图或响应。
- [ ] source/to-graph 往返验证结果。
- [ ] 单步调试验证结果。
- [ ] 输出变量及类型说明。
- [ ] AI 生成时使用的契约和提示词版本。
- [ ] 不包含密钥、密码和本机绝对路径。

## 12. 推荐原则

1. 服务端算子注册信息是唯一事实来源。
2. V2 脚本是第三方和 AI 的首选交换格式。
3. GraphData 由服务端转换生成。
4. 保存前必须诊断，保存后必须回读确认版本。
5. AI 只能在明确契约内组合，不允许猜测。
6. 已有脚本编辑时保留节点 ID，避免破坏画布和断点。
7. 调试启动失败时立即停止，不得使用零 GUID。
