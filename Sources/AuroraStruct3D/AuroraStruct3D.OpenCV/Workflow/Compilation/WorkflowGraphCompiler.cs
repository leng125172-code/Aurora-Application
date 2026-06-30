using System.Text.Json;
using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.OpenCV.Workflow.Statements;

namespace AuroraStruct3D.OpenCV.Workflow.Compilation;

/// <summary>
/// 工作流图编译器：把持久化的 graphData JSON 编译为可执行的
/// 运行时 <see cref="WorkflowDefinition"/>（<see cref="IWorkflowStatement"/> 列表）。
/// <para>
/// 流程：反序列化 → 静态校验（错误即抛 <see cref="WorkflowCompilationException"/>）→
/// 逐层拓扑排序 → 逐节点编译。参数中的 <c>{ "$var": "name" }</c> 编译为运行期解析的
/// <see cref="ComputedBinding"/>（取值并强转），字面量编译为 <see cref="ConstantBinding"/>。
/// </para>
/// </summary>
public sealed class WorkflowGraphCompiler
{
    private readonly IOperatorRegistry _registry;

    public WorkflowGraphCompiler(IOperatorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>
    /// 编译完整的持久化内容（graphData JSON 字符串）。
    /// </summary>
    public async Task<WorkflowDefinition> CompileAsync(
        string contentJson,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentJson);

        (string name, GraphDataModel graph) = ParseContent(contentJson);
        return await CompileAsync(graph, name, cancellationToken);
    }

    /// <summary>
    /// 编译已解析的 <see cref="GraphDataModel"/>。
    /// </summary>
    public async Task<WorkflowDefinition> CompileAsync(
        GraphDataModel graph,
        string name,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(graph);

        // ① 静态校验：有错误即中止。
        WorkflowValidationResult validation = await new WorkflowGraphValidator(
            _registry
        ).ValidateAsync(graph, cancellationToken);
        if (validation.HasErrors)
            throw new WorkflowCompilationException(validation);

        // ② 编译顶层作用域。
        IReadOnlyList<IWorkflowStatement> statements = await CompileScopeAsync(
            graph,
            cancellationToken
        );

        string workflowName = string.IsNullOrWhiteSpace(name) ? "workflow" : name;
        return new WorkflowDefinition(workflowName, statements);
    }

    // ── 内部实现 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 解析持久化内容为（工作流名, 顶层 graphData 模型）。兼容完整 WorkflowPayload 与裸 graphData。
    /// 供编译与签名提取共用，避免重复解析。
    /// </summary>
    public static (string Name, GraphDataModel Graph) ParseContent(string contentJson)
    {
        using JsonDocument doc = JsonDocument.Parse(contentJson);
        JsonElement root = doc.RootElement;

        string name =
            root.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String
                ? n.GetString() ?? "workflow"
                : "workflow";

        // 兼容两种入参：完整 WorkflowPayload（含 graphData）或裸 graphData（含 nodes）。
        JsonElement graphElement = root.TryGetProperty("graphData", out JsonElement g) ? g : root;

        GraphDataModel graph =
            graphElement.Deserialize<GraphDataModel>(GraphJson.Options) ?? new GraphDataModel();

        return (name, graph);
    }

    private async Task<IReadOnlyList<IWorkflowStatement>> CompileScopeAsync(
        GraphDataModel graph,
        CancellationToken cancellationToken
    )
    {
        var statements = new List<IWorkflowStatement>();

        foreach (NodeModel node in GraphTopology.Order(graph))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IWorkflowStatement? statement = await CompileNodeAsync(node, cancellationToken);
            if (statement is not null)
                statements.Add(statement);
        }

        return statements.AsReadOnly();
    }

    private async Task<IWorkflowStatement?> CompileNodeAsync(
        NodeModel node,
        CancellationToken cancellationToken
    )
    {
        switch (node.Type)
        {
            case NodeTypeTokens.StartNode:
            case NodeTypeTokens.EndNode:
                return null; // 结构节点，不产语句。

            case NodeTypeTokens.Assign:
                return CompileAssign(node);

            case NodeTypeTokens.FlowContainer:
                return await CompileContainerAsync(node, cancellationToken);

            default:
                if (Guid.TryParse(node.Type, out Guid operatorId))
                    return await CompileOperatorAsync(node, operatorId, cancellationToken);

                throw new WorkflowCompilationException(
                    $"节点 {node.Id} 的类型 '{node.Type}' 无法识别（既非内置节点，也非合法算子 GUID）。"
                );
        }
    }

    private static AssignStatement CompileAssign(NodeModel node)
    {
        Dictionary<string, JsonElement> p = node.Properties?.Params ?? new();

        string? variableName =
            p.TryGetValue("variableName", out JsonElement vn)
            && vn.ValueKind == JsonValueKind.String
                ? vn.GetString()
                : null;

        if (string.IsNullOrWhiteSpace(variableName))
        {
            throw new WorkflowCompilationException(
                $"赋值节点 {node.Id} 缺少 params.variableName。"
            );
        }

        InputBinding source = p.TryGetValue("value", out JsonElement value)
            ? BuildValueBinding(value, targetTypeName: null)
            : new ConstantBinding(null);

        return new AssignStatement(variableName!, source);
    }

    private async Task<IWorkflowStatement> CompileContainerAsync(
        NodeModel node,
        CancellationToken cancellationToken
    )
    {
        NodePropertiesModel props = node.Properties ?? new();
        Dictionary<string, JsonElement> p = props.Params ?? new();
        string kind = GetString(p, "containerKind") ?? string.Empty;

        switch (kind)
        {
            case NodeTypeTokens.ForLoop:
            {
                GraphDataModel inner = props.InnerGraphData ?? new();
                IReadOnlyList<IWorkflowStatement> body = await CompileScopeAsync(
                    inner,
                    cancellationToken
                );
                string loopVar = GetString(p, "variableName") ?? "i";
                double from = GetDouble(p, "from", 1);
                double to = GetDouble(p, "to", 1);
                double step = GetDouble(p, "step", 1);
                return new ForLoopStatement(loopVar, from, to, step, body);
            }

            case NodeTypeTokens.IfElse:
            {
                if (!p.TryGetValue("condition", out JsonElement conditionEl))
                {
                    throw new WorkflowCompilationException(
                        $"if/else 容器 {node.Id} 缺少 params.condition。"
                    );
                }

                Func<IWorkflowContext, bool> condition = ConditionCompiler.Compile(conditionEl);

                // then / else 各为独立子图（params.thenGraphData / params.elseGraphData）。
                IReadOnlyList<IWorkflowStatement> thenBody = await CompileScopeAsync(
                    GraphJson.ReadSubGraph(p, "thenGraphData"),
                    cancellationToken
                );
                IReadOnlyList<IWorkflowStatement> elseBody = await CompileScopeAsync(
                    GraphJson.ReadSubGraph(p, "elseGraphData"),
                    cancellationToken
                );

                return new IfElseStatement(condition, thenBody, elseBody);
            }

            default:
                throw new WorkflowCompilationException(
                    $"容器节点 {node.Id} 的 containerKind '{kind}' 不支持。"
                );
        }
    }

    private async Task<OperatorCallStatement> CompileOperatorAsync(
        NodeModel node,
        Guid operatorId,
        CancellationToken cancellationToken
    )
    {
        Type? operatorType = await _registry.GetOperatorTypeAsync(operatorId, cancellationToken);
        if (operatorType is null)
        {
            throw new WorkflowCompilationException(
                $"节点 {node.Id} 引用的算子 {operatorId} 未注册。"
            );
        }

        OperatorParametersDescriptor? descriptor = await _registry.GetParametersAsync(
            operatorId,
            cancellationToken
        );

        NodePropertiesModel props = node.Properties ?? new();
        Dictionary<string, JsonElement> p = props.Params ?? new();

        // 配置参数：按算子 Config 顺序构造位置绑定（保证构造函数实参顺序正确）。
        var configArgs = new List<object?>();
        if (descriptor is not null)
        {
            foreach (ConfigParameterDescriptor cfg in descriptor.Config)
            {
                configArgs.Add(
                    p.TryGetValue(cfg.Name, out JsonElement value)
                        ? BuildValueBinding(value, cfg.ParameterTypeName)
                        : new ConstantBinding(
                            ValueCoercion.Coerce(cfg.DefaultValue, cfg.ParameterTypeName)
                        )
                );
            }
        }

        IReadOnlyDictionary<string, InputBinding> inputBindings = (
            props.InputBindings ?? new()
        ).ToDictionary(
            kv => kv.Key,
            kv => (InputBinding)new VariableRefBinding(kv.Value),
            StringComparer.Ordinal
        );

        IReadOnlyDictionary<string, OutputBinding> outputBindings = (
            props.OutputBindings ?? new()
        ).ToDictionary(kv => kv.Key, kv => new OutputBinding(kv.Value), StringComparer.Ordinal);

        return new OperatorCallStatement(operatorType, configArgs, inputBindings, outputBindings);
    }

    /// <summary>
    /// 把一个 params 值编译为输入绑定：<c>{ "$var": n }</c> → 运行期解析（含强转，
    /// 当 <paramref name="targetTypeName"/> 非空时）；否则编译期字面量常量。
    /// </summary>
    private static InputBinding BuildValueBinding(JsonElement value, string? targetTypeName)
    {
        if (VarRef.TryGet(value, out string refName))
        {
            return targetTypeName is null
                ? new VariableRefBinding(refName)
                : new ComputedBinding(ctx =>
                    ValueCoercion.Coerce(ctx.Get(refName), targetTypeName)
                );
        }

        return new ConstantBinding(ValueCoercion.Coerce(value, targetTypeName));
    }

    private static string? GetString(Dictionary<string, JsonElement> p, string key) =>
        p.TryGetValue(key, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static double GetDouble(Dictionary<string, JsonElement> p, string key, double fallback)
    {
        if (!p.TryGetValue(key, out JsonElement v))
            return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d))
            return d;
        if (
            v.ValueKind == JsonValueKind.String
            && double.TryParse(v.GetString(), out double parsed)
        )
            return parsed;
        return fallback;
    }
}
