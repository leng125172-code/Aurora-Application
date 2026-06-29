using System.Text.Json;
using System.Text.RegularExpressions;
using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;

namespace AuroraStruct3D.OpenCV.Workflow.Compilation;

/// <summary>
/// 工作流图静态校验器：按拓扑序推导变量符号表，检查写前读、类型兼容、命名规则。
/// <para>
/// 当前运行时为<b>扁平作用域</b>（ForLoop/IfElse 在当前作用域执行 body），故符号表
/// 跨容器单层共享：容器内层节点沿用并贡献同一张表。类型不兼容仅产出 <c>Warning</c>，
/// 写前读未定义 / 非法变量名为 <c>Error</c>。
/// </para>
/// </summary>
public sealed class WorkflowGraphValidator
{
    private static readonly Regex NameRule = new(
        "^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.Compiled
    );

    private static readonly HashSet<string> NumericTypeNames = new(StringComparer.Ordinal)
    {
        "System.Byte",
        "System.SByte",
        "System.Int16",
        "System.UInt16",
        "System.Int32",
        "System.UInt32",
        "System.Int64",
        "System.UInt64",
        "System.Single",
        "System.Double",
        "System.Decimal",
    };

    private readonly IOperatorRegistry _registry;

    public WorkflowGraphValidator(IOperatorRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>校验整张图，返回诊断汇总。</summary>
    public async Task<WorkflowValidationResult> ValidateAsync(
        GraphDataModel graph,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(graph);

        var diagnostics = new List<WorkflowDiagnostic>();
        // 变量名 → 类型全名（null 表示类型未知，跳过类型校验）。
        var symbols = new Dictionary<string, string?>(StringComparer.Ordinal);

        // 顶层流程边界检查（start/end 唯一性、空工作流）。
        ValidateFlowBoundaries(graph, diagnostics);

        await ValidateScopeAsync(graph, symbols, diagnostics, cancellationToken);

        return new WorkflowValidationResult(diagnostics);
    }

    /// <summary>
    /// 校验顶层流程边界：起止节点数量、是否为空工作流。仅作用于顶层（容器内层不含 start/end）。
    /// </summary>
    private static void ValidateFlowBoundaries(
        GraphDataModel graph,
        List<WorkflowDiagnostic> diagnostics
    )
    {
        int startCount = graph.Nodes.Count(n => n.Type == NodeTypeTokens.StartNode);
        int endCount = graph.Nodes.Count(n => n.Type == NodeTypeTokens.EndNode);

        // 多个起止 → 入口/出口歧义，阻断；缺失 → 可运行但异常，告警。
        if (startCount > 1)
            diagnostics.Add(ErrorDiag("(graph)", "start-node", $"工作流顶层只能有一个 start-node，当前 {startCount} 个。"));
        else if (startCount == 0)
            diagnostics.Add(WarnDiag("(graph)", "start-node", "工作流缺少 start-node。"));

        if (endCount > 1)
            diagnostics.Add(ErrorDiag("(graph)", "end-node", $"工作流顶层只能有一个 end-node，当前 {endCount} 个。"));
        else if (endCount == 0)
            diagnostics.Add(WarnDiag("(graph)", "end-node", "工作流缺少 end-node。"));

        // 是否存在可执行节点（算子 / 赋值 / 容器）。
        bool hasExecutable = graph.Nodes.Any(n =>
            n.Type == NodeTypeTokens.Assign
            || n.Type == NodeTypeTokens.FlowContainer
            || Guid.TryParse(n.Type, out _)
        );
        if (!hasExecutable)
            diagnostics.Add(WarnDiag("(graph)", null, "工作流为空（仅含起止节点，无任何算子/赋值/容器）。"));
    }

    private async Task ValidateScopeAsync(
        GraphDataModel graph,
        Dictionary<string, string?> symbols,
        List<WorkflowDiagnostic> diagnostics,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<NodeModel> ordered;
        try
        {
            ordered = GraphTopology.Order(graph);
        }
        catch (WorkflowCompilationException ex)
        {
            diagnostics.Add(ErrorDiag("(graph)", null, ex.Message));
            return;
        }

        foreach (NodeModel node in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ValidateNodeAsync(node, symbols, diagnostics, cancellationToken);
        }

        // 锚点索引范围检查（视觉路由信息，越界仅告警，不阻断执行）。
        foreach (EdgeModel edge in graph.Edges)
        {
            CheckAnchor(edge.Id, "sourceAnchorIndex", edge.SourceAnchorIndex, diagnostics);
            CheckAnchor(edge.Id, "targetAnchorIndex", edge.TargetAnchorIndex, diagnostics);
        }
    }

    private static void CheckAnchor(
        string edgeId,
        string target,
        int? index,
        List<WorkflowDiagnostic> diagnostics
    )
    {
        if (index is { } value && !WorkflowAnchor.IsValidIndex(value))
        {
            diagnostics.Add(
                new WorkflowDiagnostic
                {
                    Severity = WorkflowDiagnosticSeverity.Warning,
                    NodeId = edgeId,
                    Target = target,
                    Message = $"锚点索引 {value} 超出范围（应为 0=上 / 1=右 / 2=下 / 3=左）。",
                }
            );
        }
    }

    private async Task ValidateNodeAsync(
        NodeModel node,
        Dictionary<string, string?> symbols,
        List<WorkflowDiagnostic> diagnostics,
        CancellationToken cancellationToken
    )
    {
        switch (node.Type)
        {
            case NodeTypeTokens.StartNode:
            case NodeTypeTokens.EndNode:
                return;

            case NodeTypeTokens.Assign:
                ValidateAssign(node, symbols, diagnostics);
                return;

            case NodeTypeTokens.FlowContainer:
                await ValidateContainerAsync(node, symbols, diagnostics, cancellationToken);
                return;

            default:
                if (Guid.TryParse(node.Type, out Guid operatorId))
                {
                    await ValidateOperatorAsync(
                        node,
                        operatorId,
                        symbols,
                        diagnostics,
                        cancellationToken
                    );
                }
                else
                {
                    diagnostics.Add(
                        ErrorDiag(
                            node.Id,
                            node.Type,
                            $"无法识别的节点类型 '{node.Type}'（既非内置节点，也非合法算子 GUID）。"
                        )
                    );
                }
                return;
        }
    }

    private void ValidateAssign(
        NodeModel node,
        Dictionary<string, string?> symbols,
        List<WorkflowDiagnostic> diagnostics
    )
    {
        Dictionary<string, JsonElement> p = node.Properties?.Params ?? new();

        string? variableName = GetString(p, "variableName");
        if (string.IsNullOrWhiteSpace(variableName))
        {
            diagnostics.Add(ErrorDiag(node.Id, "variableName", "赋值节点缺少 params.variableName。"));
            return;
        }

        CheckName(node.Id, variableName!, diagnostics);

        // 推导赋值来源类型。
        string? sourceType = null;
        if (p.TryGetValue("value", out JsonElement value))
        {
            if (VarRef.TryGet(value, out string refName))
            {
                CheckRead(node.Id, "value", refName, symbols, diagnostics);
                sourceType = symbols.GetValueOrDefault(refName);
            }
            else
            {
                sourceType = LiteralTypeName(value);
            }
        }

        // 声明（upsert）。
        symbols[variableName!] = sourceType;
    }

    private async Task ValidateContainerAsync(
        NodeModel node,
        Dictionary<string, string?> symbols,
        List<WorkflowDiagnostic> diagnostics,
        CancellationToken cancellationToken
    )
    {
        Dictionary<string, JsonElement> p = node.Properties?.Params ?? new();
        string kind = GetString(p, "containerKind") ?? string.Empty;

        if (kind == NodeTypeTokens.ForLoop)
        {
            string loopVar = GetString(p, "variableName") ?? "i";
            CheckName(node.Id, loopVar, diagnostics);
            symbols[loopVar] = "System.Double"; // 循环变量为数值

            GraphDataModel inner = node.Properties?.InnerGraphData ?? new();
            await ValidateScopeAsync(inner, symbols, diagnostics, cancellationToken);
        }
        else if (kind == NodeTypeTokens.IfElse)
        {
            // 条件中引用的变量须已定义（写前读）。
            if (p.TryGetValue("condition", out JsonElement condition))
            {
                foreach (string refName in ConditionCompiler.ExtractVarRefs(condition))
                    CheckRead(node.Id, "condition", refName, symbols, diagnostics);
            }
            else
            {
                diagnostics.Add(ErrorDiag(node.Id, "condition", "if/else 容器缺少 params.condition。"));
            }

            // then / else 各为独立子图，递归校验（扁平作用域，共享符号表）。
            await ValidateScopeAsync(
                GraphJson.ReadSubGraph(p, "thenGraphData"),
                symbols,
                diagnostics,
                cancellationToken
            );
            await ValidateScopeAsync(
                GraphJson.ReadSubGraph(p, "elseGraphData"),
                symbols,
                diagnostics,
                cancellationToken
            );
        }
        else
        {
            // 未知容器种类：回退递归 innerGraphData，覆盖符号。
            GraphDataModel inner = node.Properties?.InnerGraphData ?? new();
            await ValidateScopeAsync(inner, symbols, diagnostics, cancellationToken);
        }

        // 容器输出绑定：把内层结果导出为父作用域变量。
        if (node.Properties?.OutputBindings is { } outs)
        {
            foreach (string varName in outs.Values)
            {
                CheckName(node.Id, varName, diagnostics);
                symbols[varName] = null; // 类型未知
            }
        }
    }

    private async Task ValidateOperatorAsync(
        NodeModel node,
        Guid operatorId,
        Dictionary<string, string?> symbols,
        List<WorkflowDiagnostic> diagnostics,
        CancellationToken cancellationToken
    )
    {
        Type? operatorType = await _registry.GetOperatorTypeAsync(operatorId, cancellationToken);
        if (operatorType is null)
        {
            diagnostics.Add(
                ErrorDiag(node.Id, node.Type, $"算子 {operatorId} 未注册，无法编译该节点。")
            );
            return;
        }

        OperatorParametersDescriptor? descriptor = await _registry.GetParametersAsync(
            operatorId,
            cancellationToken
        );

        NodePropertiesModel props = node.Properties ?? new();

        // ① 输入端口绑定：读变量（写前读）+ 类型兼容。
        if (props.InputBindings is { } inputs)
        {
            foreach ((string portName, string varName) in inputs)
            {
                CheckRead(node.Id, portName, varName, symbols, diagnostics);

                string? portType = descriptor
                    ?.Inputs.FirstOrDefault(i => i.ParameterName == portName)
                    ?.ParameterTypeName;
                CheckTypeCompat(
                    node.Id,
                    portName,
                    symbols.GetValueOrDefault(varName),
                    portType,
                    diagnostics
                );
            }
        }

        // ② 配置参数中的 $var 引用：读变量 + 类型兼容。
        if (props.Params is { } prms && descriptor is not null)
        {
            foreach (ConfigParameterDescriptor cfg in descriptor.Config)
            {
                if (!prms.TryGetValue(cfg.Name, out JsonElement value))
                    continue;
                if (!VarRef.TryGet(value, out string refName))
                    continue;

                CheckRead(node.Id, cfg.Name, refName, symbols, diagnostics);
                CheckTypeCompat(
                    node.Id,
                    cfg.Name,
                    symbols.GetValueOrDefault(refName),
                    cfg.ParameterTypeName,
                    diagnostics
                );
            }
        }

        // ③ 输出端口绑定：声明变量（类型取输出端口类型）。
        if (props.OutputBindings is { } outputs)
        {
            foreach ((string portName, string varName) in outputs)
            {
                CheckName(node.Id, varName, diagnostics);

                string? portType = descriptor
                    ?.Outputs.FirstOrDefault(o => o.ParameterName == portName)
                    ?.ParameterTypeName;
                symbols[varName] = portType;
            }
        }
    }

    // ── 校验小工具 ────────────────────────────────────────────────────────────

    private static void CheckRead(
        string nodeId,
        string target,
        string variableName,
        Dictionary<string, string?> symbols,
        List<WorkflowDiagnostic> diagnostics
    )
    {
        if (string.IsNullOrWhiteSpace(variableName))
        {
            diagnostics.Add(ErrorDiag(nodeId, target, "绑定引用了空变量名。"));
            return;
        }

        if (!symbols.ContainsKey(variableName))
        {
            diagnostics.Add(
                ErrorDiag(
                    nodeId,
                    target,
                    $"引用了未定义或上游尚未产出的变量 '{variableName}'（写前读）。"
                )
            );
        }
    }

    private static void CheckTypeCompat(
        string nodeId,
        string target,
        string? haveType,
        string? needType,
        List<WorkflowDiagnostic> diagnostics
    )
    {
        if (haveType is null || needType is null)
            return; // 任一类型未知，跳过。
        if (string.Equals(haveType, needType, StringComparison.Ordinal))
            return;
        if (NumericTypeNames.Contains(haveType) && NumericTypeNames.Contains(needType))
            return; // 数值之间允许（强转放宽）。

        diagnostics.Add(
            new WorkflowDiagnostic
            {
                Severity = WorkflowDiagnosticSeverity.Warning,
                NodeId = nodeId,
                Target = target,
                Message = $"类型可能不匹配：变量类型 '{haveType}' → 期望 '{needType}'。",
            }
        );
    }

    private static void CheckName(
        string nodeId,
        string variableName,
        List<WorkflowDiagnostic> diagnostics
    )
    {
        if (!NameRule.IsMatch(variableName))
        {
            diagnostics.Add(
                ErrorDiag(
                    nodeId,
                    variableName,
                    "非法变量名（需匹配 [A-Za-z_][A-Za-z0-9_]*）。"
                )
            );
        }
    }

    private static string? GetString(Dictionary<string, JsonElement> p, string key) =>
        p.TryGetValue(key, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string? LiteralTypeName(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Number => "System.Double",
            JsonValueKind.String => "System.String",
            JsonValueKind.True or JsonValueKind.False => "System.Boolean",
            _ => null,
        };

    private static WorkflowDiagnostic ErrorDiag(string nodeId, string? target, string message) =>
        new()
        {
            Severity = WorkflowDiagnosticSeverity.Error,
            NodeId = nodeId,
            Target = target,
            Message = message,
        };

    private static WorkflowDiagnostic WarnDiag(string nodeId, string? target, string message) =>
        new()
        {
            Severity = WorkflowDiagnosticSeverity.Warning,
            NodeId = nodeId,
            Target = target,
            Message = message,
        };
}
