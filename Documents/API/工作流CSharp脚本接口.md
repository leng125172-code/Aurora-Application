# 工作流 C# 风格脚本接口

工作流以受限的 C# 风格脚本作为执行权威来源，`GraphData` 仅用于前端 JSON 画布编辑和兼容回显。脚本由自有解析器处理，不使用 Roslyn，也不允许执行任意 C# 代码。

## 语法

语言版本当前为 `1`，只允许以下语句：

```csharp
Workflow("检测流程", 1);
GraphBegin(null);
Node("start", "start-node", 0, 0, null);
Node("assign", "assign", 160, 0, null);
Param("assign", "variableName", "retstatus", "literal");
Param("assign", "value", {"status": true, "regions": []}, "literal");
Node("end", "end-node", 320, 0, null);
Input("end", "result", "retstatus", "variable");
Edge("e1", "start", "assign", null, null, null);
Edge("e2", "assign", "end", null, null, null);
GraphEnd();
```

嵌套容器通过 `GraphBegin("容器节点 ID")` 与 `GraphEnd()` 表示。参数值支持 JSON 标量、对象和数组。允许 `//` 行注释。

类型声明、成员调用、循环、反射、文件及网络调用，以及所有非白名单方法都会被拒绝。错误响应包含错误码、消息、行、列和可识别的节点 ID。

## 原生接口

- `GET /api/app/workflow/{id}/source`：读取脚本、哈希、语言版本、修订号及派生 JSON。
- `PUT /api/app/workflow/{id}/source`：校验并保存脚本，同时反向更新 JSON。请求中的 `sourceRevision` 用于乐观锁。
- `POST /api/app/workflow/source/validate`：执行语法、图结构、变量及算子校验。
- `POST /api/app/workflow/graph/to-source`：把前端 JSON 转为规范脚本。
- `POST /api/app/workflow/source/to-graph`：把脚本转为前端 JSON。
- `POST /api/app/workflow/{id}/source/debug`：调试已保存脚本。
- `POST /api/app/workflow/source/debug`：调试未保存脚本；请求仍需提供所属项目和工作流 ID，以复用现有变量、文件和调试会话。
- `POST /api/app/workflow/source/migrate-legacy`：批量为尚无脚本的旧工作流生成并保存脚本。

现有 JSON 创建和更新接口会进入同一保存管线，因此 JSON 保存与脚本保存会生成一致的 `SourceHash` 和 `ProgramHash`。

## 执行与部署

保存时会解析、校验并预热程序缓存。正式执行按 `ProgramHash` 读取已验证的执行图、拓扑顺序、签名、不可变语句定义和预编译输出路径，不会重复解析 `GraphData`。算子实例仍在每次执行时独立创建和释放。

旧数据在迁移期首次运行时允许一次性从 `GraphData` 生成脚本并写回。部署前应调用批量迁移接口；发布快照会冻结 `SourceCode`、`ProgramHash` 和 `LanguageVersion`，同时保留 `GraphData` 供旧前端回显。

输出路径支持根变量和下探路径，例如：

```text
retstatus
retstatus.status
retstatus.regions[0].name
```

这些路径在程序进入缓存时编译为访问器，执行期间不再重复拆分路径。

## IDE 接口

现代化编辑器使用 `/api/app/workflow/source` 下的语言服务接口，包括
`diagnostics`、`completions`、`hover`、`signature-help`、`document-symbols`、
`definition`、`references`、`format`、`code-actions`、`semantic-tokens`、
`patch-graph` 和 `map-position`。所有请求携带 `documentVersion`，客户端必须丢弃
版本不匹配的响应。

脚本语句具有稳定 `StatementId`，节点语句同时映射 `NodeId` 和源码范围。保存使用
`SourceRevision + ConcurrencyStamp` 双重并发控制；自动保存写入用户草稿，不覆盖正式版本。

调试会话额外支持：

- `PUT /api/app/workflow/executions/{id}/breakpoints`
- `POST /api/app/workflow/executions/{id}/continue`
- `POST /api/app/workflow/executions/{id}/pause`
- `POST /api/app/workflow/executions/{id}/run-to`
- `GET /api/app/workflow/executions/{id}/stack`
- `GET /api/app/workflow/executions/{id}/variables`
- `POST /api/app/workflow/executions/{id}/watch`
- `GET /api/app/workflow/executions/{id}/trace`

Watch 仅接受变量名、对象属性和数组下标，不支持方法调用。
