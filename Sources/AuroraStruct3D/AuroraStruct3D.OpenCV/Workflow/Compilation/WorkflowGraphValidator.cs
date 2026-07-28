using System.Text.Json;
using System.Text.RegularExpressions;
using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;

namespace AuroraStruct3D.OpenCV.Workflow.Compilation;

/// <summary>
/// 工作流图静态校验器：按拓扑序推导变量符号表，检查写前读、类型兼容、命名规则、
/// 不可达节点、未使用变量、重复变量名、循环变量保护、分支数据流完整性。
/// <para>
/// 当前运行时为<b>扁平作用域</b>（ForLoop/IfElse 在当前作用域执行 body），故符号表
/// 跨容器单层共享：容器内层节点沿用并贡献同一张表。类型不兼容仅产出 <c>Warning</c>，
/// 写前读未定义 / 非法变量名为 <c>Error</c>。
/// </para>
/// </summary>
public sealed class WorkflowGraphValidator
{
    private static readonly Regex NameRule = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

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

    // MOD: 精度敏感端口白名单（operatorGuid:portName），启用严格数值类型匹配。
    private static readonly HashSet<string> PrecisionSensitivePorts = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "d3f12345-6789-0123-4567-89012345670c:distanceThreshold",
        "d3f12345-6789-0123-4567-89012345670c:probability",
    };

    private readonly IOperatorRegistry _registry;
    private readonly WorkflowValidationOptions _options;

    /// <summary>变量消费记录：变量名 → 消费它的节点 ID 列表（用于未使用变量检测）。</summary>
    private readonly Dictionary<string, HashSet<string>> _consumers = new(StringComparer.Ordinal);

    /// <summary>变量定义记录：变量名 → 定义它的节点 ID（用于重复定义检测）。</summary>
    private readonly Dictionary<string, string> _definers = new(StringComparer.Ordinal);

    /// <summary>ForLoop 循环体内受保护的变量名集合。</summary>
    private readonly Stack<HashSet<string>> _protectedVars = new();

    public WorkflowGraphValidator(IOperatorRegistry registry)
        : this(registry, null) { }

    public WorkflowGraphValidator(IOperatorRegistry registry, WorkflowValidationOptions? options)
    {
        _registry = registry;
        _options = options ?? new WorkflowValidationOptions();
    }

    /// <summary>校验整张图，返回诊断汇总。</summary>
    public async Task<WorkflowValidationResult> ValidateAsync(
        GraphDataModel graph,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(graph);

        _consumers.Clear();
        _definers.Clear();
        _protectedVars.Clear();

        var diagnostics = new List<WorkflowDiagnostic>();
        var symbols = new Dictionary<string, string?>(StringComparer.Ordinal);

        // 顶层流程边界检查（start/end 唯一性、空工作流）。
        ValidateFlowBoundaries(graph, diagnostics);

        await ValidateScopeAsync(graph, symbols, diagnostics, cancellationToken);

        // 拓扑后检查：不可达节点。
        ValidateUnreachableNodes(graph, diagnostics);

        // 扫描结束后检查：未使用变量。
        ValidateUnusedVariables(diagnostics);

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
            diagnostics.Add(
                ErrorDiag(
                    "(graph)",
                    "start-node",
                    $"工作流顶层只能有一个 start-node，当前 {startCount} 个。"
                )
            );
        else if (startCount == 0)
            diagnostics.Add(WarnDiag("(graph)", "start-node", "工作流缺少 start-node。"));

        if (endCount > 1)
            diagnostics.Add(
                ErrorDiag(
                    "(graph)",
                    "end-node",
                    $"工作流顶层只能有一个 end-node，当前 {endCount} 个。"
                )
            );
        else if (endCount == 0)
            diagnostics.Add(WarnDiag("(graph)", "end-node", "工作流缺少 end-node。"));

        // 是否存在可执行节点（算子 / 赋值 / 容器）。
        bool hasExecutable = graph.Nodes.Any(n =>
            n.Type == NodeTypeTokens.Assign
            || n.Type == NodeTypeTokens.FlowContainer
            || Guid.TryParse(n.Type, out _)
        );
        if (!hasExecutable)
            diagnostics.Add(
                WarnDiag("(graph)", null, "工作流为空（仅含起止节点，无任何算子/赋值/容器）。")
            );
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
                return;

            case NodeTypeTokens.EndNode:
                ValidateEndNode(node, symbols, diagnostics);
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

    private void ValidateEndNode(
        NodeModel node,
        Dictionary<string, string?> symbols,
        List<WorkflowDiagnostic> diagnostics
    )
    {
        Dictionary<string, string>? bindings = node.Properties?.InputBindings;
        if (bindings is null || bindings.Count == 0)
        {
            diagnostics.Add(ErrorDiag(node.Id, "inputBindings", "结束节点至少需要配置一个输出变量。"));
            return;
        }

        foreach ((string portName, string variableName) in bindings)
        {
            if (
                !BindingSource.TryResolveSource(
                    node.Properties?.InputBindingSources,
                    portName,
                    out bool isVariable,
                    out string sourceError
                )
            )
            {
                diagnostics.Add(
                    ErrorDiag(node.Id, portName, $"结束节点输出 source 无效：{sourceError}")
                );
                continue;
            }

            if (!isVariable)
            {
                diagnostics.Add(ErrorDiag(node.Id, portName, "结束节点输出必须绑定运行时变量。"));
                continue;
            }

            CheckRead(
                node.Id,
                portName,
                GetOutputRootVariableName(variableName),
                symbols,
                diagnostics
            );
        }
    }

    private static string GetOutputRootVariableName(string outputPath)
    {
        int dotIndex = outputPath.IndexOf('.');
        int bracketIndex = outputPath.IndexOf('[');
        int separatorIndex =
            dotIndex < 0 ? bracketIndex
            : bracketIndex < 0 ? dotIndex
            : Math.Min(dotIndex, bracketIndex);
        return separatorIndex < 0 ? outputPath : outputPath[..separatorIndex];
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
            diagnostics.Add(
                ErrorDiag(node.Id, "variableName", "赋值节点缺少 params.variableName。")
            );
            return;
        }

        CheckName(node.Id, variableName!, diagnostics);

        // 推导赋值来源类型。
        string? sourceType = null;
        if (p.TryGetValue("value", out JsonElement value))
        {
            if (
                !BindingSource.TryResolveSource(
                    node.Properties?.ParamSources,
                    "value",
                    out bool isVariable,
                    out string sourceError
                )
            )
            {
                diagnostics.Add(ErrorDiag(node.Id, "value", $"source 无效：{sourceError}"));
            }
            else if (isVariable)
            {
                if (
                    !BindingSource.TryGetParamVariableNameStrict(
                        node.Properties?.ParamSources,
                        "value",
                        value,
                        out string refName,
                        out string variableError
                    )
                )
                {
                    diagnostics.Add(ErrorDiag(node.Id, "value", $"变量引用无效：{variableError}"));
                    sourceType = null;
                    goto SourceDone;
                }

                CheckRead(node.Id, "value", refName, symbols, diagnostics);
                sourceType = symbols.GetValueOrDefault(refName);
            }
            else
            {
                sourceType = LiteralTypeName(value);
            }

            SourceDone:
            ;
        }

        // 声明变量（含重复定义、循环变量保护检查）。
        DeclareVariable(
            node.Id,
            variableName!,
            sourceType,
            symbols,
            diagnostics,
            IsShadowAllowed(node, variableName!)
        );
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

            // 声明循环变量并保护（防止循环体内被重新定义）。
            DeclareVariable(node.Id, loopVar, "System.Double", symbols, diagnostics);
            var protectedSet = new HashSet<string>(StringComparer.Ordinal) { loopVar };
            _protectedVars.Push(protectedSet);

            GraphDataModel inner = node.Properties?.InnerGraphData ?? new();
            await ValidateScopeAsync(inner, symbols, diagnostics, cancellationToken);

            _protectedVars.Pop();
            // 循环结束后移除循环变量，防止外部节点仍可访问该变量
            symbols.Remove(loopVar);
            _definers.Remove(loopVar);
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
                diagnostics.Add(
                    ErrorDiag(node.Id, "condition", "if/else 容器缺少 params.condition。")
                );
            }

            // 分支数据流完整性：快照当前符号表，分别校验两个分支，比较新增变量。
            var preSymbols = new Dictionary<string, string?>(symbols, StringComparer.Ordinal);

            // then 分支。
            var thenSymbols = new Dictionary<string, string?>(symbols, StringComparer.Ordinal);
            await ValidateScopeAsync(
                GraphJson.ReadSubGraph(p, "thenGraphData"),
                thenSymbols,
                diagnostics,
                cancellationToken
            );

            // else 分支。
            var elseSymbols = new Dictionary<string, string?>(symbols, StringComparer.Ordinal);
            await ValidateScopeAsync(
                GraphJson.ReadSubGraph(p, "elseGraphData"),
                elseSymbols,
                diagnostics,
                cancellationToken
            );

            // 检查两个分支产生的新变量是否一致。
            var thenNew = new HashSet<string>(
                thenSymbols.Keys.Where(k => !preSymbols.ContainsKey(k)),
                StringComparer.Ordinal
            );
            var elseNew = new HashSet<string>(
                elseSymbols.Keys.Where(k => !preSymbols.ContainsKey(k)),
                StringComparer.Ordinal
            );

            if (!thenNew.SetEquals(elseNew))
            {
                var onlyThen = thenNew.Except(elseNew).ToList();
                var onlyElse = elseNew.Except(thenNew).ToList();

                if (onlyThen.Count > 0)
                    diagnostics.Add(
                        WarnDiag(
                            node.Id,
                            "thenGraphData",
                            $"then 分支独有变量 {string.Join("、", onlyThen)}，else 分支未产出，可能导致下游读未定义。"
                        )
                    );
                if (onlyElse.Count > 0)
                    diagnostics.Add(
                        WarnDiag(
                            node.Id,
                            "elseGraphData",
                            $"else 分支独有变量 {string.Join("、", onlyElse)}，then 分支未产出，可能导致下游读未定义。"
                        )
                    );
            }

            // 合并两个分支的符号表（取并集，类型未知时用 null）。
            foreach (string key in thenNew.Union(elseNew))
            {
                symbols[key] =
                    thenSymbols.GetValueOrDefault(key) ?? elseSymbols.GetValueOrDefault(key);
            }
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
            foreach ((string portName, string varName) in outs)
            {
                if (
                    !BindingSource.TryResolveSource(
                        node.Properties.OutputBindingSources,
                        portName,
                        out bool isVariable,
                        out string sourceError
                    )
                )
                {
                    diagnostics.Add(
                        ErrorDiag(node.Id, portName, $"output source 无效：{sourceError}")
                    );
                    continue;
                }

                if (!isVariable)
                {
                    diagnostics.Add(
                        ErrorDiag(node.Id, portName, "输出绑定必须为变量引用，不能标记为字面量。")
                    );
                    continue;
                }

                CheckName(node.Id, varName, diagnostics);
                DeclareVariable(
                    node.Id,
                    varName,
                    null,
                    symbols,
                    diagnostics,
                    IsShadowAllowed(node, varName)
                );
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

        if (descriptor is not null)
        {
            HashSet<string> knownInputs = descriptor
                .Inputs.Where(x => !string.IsNullOrWhiteSpace(x.ParameterName))
                .Select(x => x.ParameterName!)
                .ToHashSet(StringComparer.Ordinal);
            HashSet<string> knownOutputs = descriptor
                .Outputs.Where(x => !string.IsNullOrWhiteSpace(x.ParameterName))
                .Select(x => x.ParameterName!)
                .ToHashSet(StringComparer.Ordinal);
            HashSet<string> knownConfig = descriptor
                .Config.Select(x => x.Name)
                .ToHashSet(StringComparer.Ordinal);

            foreach (
                string portName in props.InputBindings?.Keys ?? Enumerable.Empty<string>()
            )
            {
                if (!knownInputs.Contains(portName))
                {
                    diagnostics.Add(
                        ErrorDiag(node.Id, portName, $"算子不存在输入端口 '{portName}'。")
                    );
                }
            }

            foreach (
                string portName in props.OutputBindings?.Keys ?? Enumerable.Empty<string>()
            )
            {
                if (!knownOutputs.Contains(portName))
                {
                    diagnostics.Add(
                        ErrorDiag(node.Id, portName, $"算子不存在输出端口 '{portName}'。")
                    );
                }
            }

            foreach (string configName in props.Params?.Keys ?? Enumerable.Empty<string>())
            {
                if (!knownConfig.Contains(configName))
                {
                    diagnostics.Add(
                        ErrorDiag(node.Id, configName, $"算子不存在配置参数 '{configName}'。")
                    );
                }
            }

            if (descriptor.Inputs.Count > 0 && (props.InputBindings?.Count ?? 0) == 0)
            {
                diagnostics.Add(
                    ErrorDiag(node.Id, "inputBindings", "算子声明了输入端口，但节点未配置任何输入绑定。")
                );
            }

            foreach (ConfigParameterDescriptor config in descriptor.Config.Where(x => x.Required))
            {
                if (!(props.Params?.ContainsKey(config.Name) ?? false))
                {
                    diagnostics.Add(
                        ErrorDiag(node.Id, config.Name, $"缺少必填配置参数 '{config.Name}'。")
                    );
                }
            }
        }

        // ① 输入端口绑定：读变量（写前读）+ 类型兼容。
        if (props.InputBindings is { } inputs)
        {
            foreach ((string portName, string varName) in inputs)
            {
                if (
                    !BindingSource.TryResolveSource(
                        props.InputBindingSources,
                        portName,
                        out bool isVariable,
                        out string sourceError
                    )
                )
                {
                    diagnostics.Add(
                        ErrorDiag(node.Id, portName, $"input source 无效：{sourceError}")
                    );
                    continue;
                }

                if (!isVariable)
                {
                    continue;
                }

                CheckRead(node.Id, portName, varName, symbols, diagnostics);

                string? portType = descriptor
                    ?.Inputs.FirstOrDefault(i => i.ParameterName == portName)
                    ?.ParameterTypeName;
                CheckTypeCompat(
                    node.Id,
                    portName,
                    symbols.GetValueOrDefault(varName),
                    portType,
                    diagnostics,
                    operatorId,
                    portName
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
                if (
                    !BindingSource.TryResolveSource(
                        props.ParamSources,
                        cfg.Name,
                        out bool isVariable,
                        out string sourceError
                    )
                )
                {
                    diagnostics.Add(
                        ErrorDiag(node.Id, cfg.Name, $"param source 无效：{sourceError}")
                    );
                    continue;
                }

                if (!isVariable)
                {
                    continue;
                }

                if (
                    !BindingSource.TryGetParamVariableNameStrict(
                        props.ParamSources,
                        cfg.Name,
                        value,
                        out string refName,
                        out string variableError
                    )
                )
                {
                    diagnostics.Add(ErrorDiag(node.Id, cfg.Name, $"变量引用无效：{variableError}"));
                    continue;
                }

                CheckRead(node.Id, cfg.Name, refName, symbols, diagnostics);
                CheckTypeCompat(
                    node.Id,
                    cfg.Name,
                    symbols.GetValueOrDefault(refName),
                    cfg.ParameterTypeName,
                    diagnostics,
                    operatorId,
                    cfg.Name
                );
            }
        }

        // ③ 输出端口绑定：声明变量（类型取输出端口类型）。
        if (props.OutputBindings is { } outputs)
        {
            foreach ((string portName, string varName) in outputs)
            {
                if (
                    !BindingSource.TryResolveSource(
                        props.OutputBindingSources,
                        portName,
                        out bool isVariable,
                        out string sourceError
                    )
                )
                {
                    diagnostics.Add(
                        ErrorDiag(node.Id, portName, $"output source 无效：{sourceError}")
                    );
                    continue;
                }

                if (!isVariable)
                {
                    diagnostics.Add(
                        ErrorDiag(node.Id, portName, "输出绑定必须为变量引用，不能标记为字面量。")
                    );
                    continue;
                }

                CheckName(node.Id, varName, diagnostics);

                string? portType = descriptor
                    ?.Outputs.FirstOrDefault(o => o.ParameterName == portName)
                    ?.ParameterTypeName;
                DeclareVariable(
                    node.Id,
                    varName,
                    portType,
                    symbols,
                    diagnostics,
                    IsShadowAllowed(node, varName)
                );
            }
        }
    }

    // ── 校验小工具 ────────────────────────────────────────────────────────────

    private void CheckRead(
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
            return;
        }

        // 记录消费关系（用于后续未使用变量检测）。
        if (!_consumers.TryGetValue(variableName, out HashSet<string>? consumerSet))
        {
            consumerSet = new HashSet<string>(StringComparer.Ordinal);
            _consumers[variableName] = consumerSet;
        }
        consumerSet.Add(nodeId);
    }

    private void CheckTypeCompat(
        string nodeId,
        string target,
        string? haveType,
        string? needType,
        List<WorkflowDiagnostic> diagnostics,
        Guid operatorId,
        string portName
    )
    {
        if (haveType is null || needType is null)
            return; // 任一类型未知，跳过。
        if (string.Equals(haveType, needType, StringComparison.Ordinal))
            return;

        bool numericMismatch =
            NumericTypeNames.Contains(haveType) && NumericTypeNames.Contains(needType);
        if (numericMismatch)
        {
            // MOD: 白名单端口启用严格数值匹配，阻止静默精度丢失。
            string preciseKey = $"{operatorId:D}:{portName}";
            if (PrecisionSensitivePorts.Contains(preciseKey))
            {
                diagnostics.Add(
                    ErrorDiag(
                        nodeId,
                        target,
                        $"精度敏感端口 '{portName}' 要求严格类型一致：变量类型 '{haveType}'，期望 '{needType}'。"
                    )
                );
            }

            return;
        }

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
                ErrorDiag(nodeId, variableName, "非法变量名（需匹配 [A-Za-z_][A-Za-z0-9_]*）。")
            );
        }
    }

    /// <summary>
    /// 声明变量：检查重复定义、循环变量保护，并记录定义节点。
    /// </summary>
    private void DeclareVariable(
        string nodeId,
        string variableName,
        string? typeName,
        Dictionary<string, string?> symbols,
        List<WorkflowDiagnostic> diagnostics,
        bool allowShadow = false
    )
    {
        // 检查是否与受保护的循环变量冲突。
        foreach (HashSet<string> protectedSet in _protectedVars)
        {
            if (protectedSet.Contains(variableName))
            {
                diagnostics.Add(
                    ErrorDiag(
                        nodeId,
                        variableName,
                        $"循环体内不允许重新定义受保护的循环变量 '{variableName}'。"
                    )
                );
                return;
            }
        }

        // 检查重复定义（同一变量被多个节点声明）。
        if (_definers.TryGetValue(variableName, out string? previousNode))
        {
            // MOD: 严格模式下重复定义升级为 Error，除非显式标记允许 shadow。
            if (!allowShadow && _options.StrictVariableDefinition)
            {
                diagnostics.Add(
                    ErrorDiag(
                        nodeId,
                        variableName,
                        $"变量 '{variableName}' 已被节点 {previousNode} 定义；严格模式禁止重复定义。"
                    )
                );
                return;
            }

            if (!allowShadow)
            {
                diagnostics.Add(
                    WarnDiag(
                        nodeId,
                        variableName,
                        $"变量 '{variableName}' 已被节点 {previousNode} 定义，此处覆盖可能导致数据流歧义。"
                    )
                );
            }
        }
        else
        {
            _definers[variableName] = nodeId;
        }

        symbols[variableName] = typeName;
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

    // ── 拓扑后校验 ────────────────────────────────────────────────────────────

    /// <summary>MOD: 基于 start-node 的全局可达性检查（BFS）。</summary>
    private static void ValidateUnreachableNodes(
        GraphDataModel graph,
        List<WorkflowDiagnostic> diagnostics
    )
    {
        if (graph.Nodes.Count == 0)
            return;

        Dictionary<string, NodeModel> nodeMap = graph.Nodes.ToDictionary(x => x.Id, x => x);
        List<string> startNodeIds = graph
            .Nodes.Where(x => x.Type == NodeTypeTokens.StartNode)
            .Select(x => x.Id)
            .ToList();
        if (startNodeIds.Count == 0)
        {
            return;
        }

        Dictionary<string, List<string>> adjacency = new(StringComparer.Ordinal);
        foreach (string nodeId in nodeMap.Keys)
        {
            adjacency[nodeId] = [];
        }

        foreach (EdgeModel edge in graph.Edges)
        {
            if (
                edge.SourceNodeId is null
                || edge.TargetNodeId is null
                || !nodeMap.ContainsKey(edge.SourceNodeId)
                || !nodeMap.ContainsKey(edge.TargetNodeId)
            )
            {
                continue;
            }

            adjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
        }

        HashSet<string> reachable = new(StringComparer.Ordinal);
        Queue<string> queue = new(startNodeIds);
        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            if (!reachable.Add(current))
            {
                continue;
            }

            foreach (string next in adjacency[current])
            {
                if (!reachable.Contains(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        foreach (NodeModel node in graph.Nodes)
        {
            if (node.Type is NodeTypeTokens.StartNode or NodeTypeTokens.EndNode)
                continue;
            if (reachable.Contains(node.Id))
                continue;
            if (
                Guid.TryParse(node.Type, out _)
                || node.Type is NodeTypeTokens.Assign or NodeTypeTokens.FlowContainer
            )
            {
                diagnostics.Add(
                    WarnDiag(node.Id, null, "该节点从 start-node 不可达，将不会被执行。")
                );
            }
        }
    }

    // MOD: 显式 shadow 标记读取，支持 params.allowShadow 或 params.shadowVariables。
    private static bool IsShadowAllowed(NodeModel node, string variableName)
    {
        Dictionary<string, JsonElement>? parameters = node.Properties?.Params;
        if (parameters is null)
        {
            return false;
        }

        if (
            parameters.TryGetValue("allowShadow", out JsonElement allowShadow)
            && (allowShadow.ValueKind == JsonValueKind.True)
        )
        {
            return true;
        }

        if (
            !parameters.TryGetValue("shadowVariables", out JsonElement shadowVariables)
            || shadowVariables.ValueKind != JsonValueKind.Array
        )
        {
            return false;
        }

        foreach (JsonElement item in shadowVariables.EnumerateArray())
        {
            if (
                item.ValueKind == JsonValueKind.String
                && string.Equals(item.GetString(), variableName, StringComparison.Ordinal)
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>检测已定义但从未被任何节点消费的变量。</summary>
    private void ValidateUnusedVariables(List<WorkflowDiagnostic> diagnostics)
    {
        foreach ((string varName, string definerNodeId) in _definers)
        {
            if (!_consumers.ContainsKey(varName))
            {
                diagnostics.Add(
                    WarnDiag(
                        definerNodeId,
                        varName,
                        $"变量 '{varName}' 已定义但从未被任何节点消费。"
                    )
                );
            }
        }
    }

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
