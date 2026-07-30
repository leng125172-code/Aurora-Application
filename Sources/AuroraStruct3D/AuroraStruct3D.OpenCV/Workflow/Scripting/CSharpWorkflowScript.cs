using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using System.Security.Cryptography;
using System.Reflection;
using System.Text.RegularExpressions;

namespace AuroraStruct3D.OpenCV.Workflow.Scripting;

public sealed class WorkflowScriptException : FormatException
{
    public string Code { get; }
    public int Line { get; }
    public int Column { get; }
    public string? NodeId { get; }

    public WorkflowScriptException(
        string code,
        string message,
        int line,
        int column = 1,
        string? nodeId = null
    )
        : base($"脚本第 {line} 行：{message}")
    {
        Code = code;
        Line = line;
        Column = column;
        NodeId = nodeId;
    }
}

/// <summary>
/// 受限 C# 风格工作流脚本。仅支持白名单方法调用，不使用 Roslyn，也不执行任意 C#。
/// </summary>
public static class CSharpWorkflowScript
{
    public const int LanguageVersion = 2;
    private static readonly JsonSerializerOptions ScriptJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private static readonly JsonSerializerOptions NodeMetadataJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
    public static readonly IReadOnlySet<string> AllowedMethods = new HashSet<string>(
        ["Workflow", "Return", "GraphBegin", "GraphEnd", "Node", "Input", "Output", "Param", "Edge"],
        StringComparer.Ordinal
    );

    public static string Generate(string workflowName, GraphDataModel graph)
    {
        return GenerateV2(workflowName, graph);
    }

    public static (string Name, GraphDataModel Graph) Parse(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        int version = ReadLanguageVersion(source);
        return version switch
        {
            1 => ParseV1(source),
            LanguageVersion => ParseV2(source),
            _ => throw Error(1, $"不支持的脚本版本 {version}。"),
        };
    }

    private static string GenerateV2(string workflowName, GraphDataModel graph)
    {
        StringBuilder source = new();
        IReadOnlyDictionary<string, string> variableNames = BuildVariableNames(graph);
        source.AppendLine("// Aurora Workflow Script V2 - operator call syntax");
        NodeModel? start = graph.Nodes.FirstOrDefault(x => x.Type == "start-node");
        if (start is not null)
            WriteNodeMetadata(source, start, variableNames);
        source.Append("Workflow(").Append(Json(workflowName)).Append(", ").Append(LanguageVersion)
            .AppendLine(");");
        source.AppendLine();

        _ = GetOperatorContractsByName(); // Reject ambiguous class names before emitting source.
        Dictionary<Guid, OperatorContract> contracts = GetOperatorContractsById();
        foreach (NodeModel node in OrderOperatorNodes(graph))
        {
            if (!Guid.TryParse(node.Type, out Guid operatorId)
                || !contracts.TryGetValue(operatorId, out OperatorContract? contract))
                throw new WorkflowScriptException(
                    "WFS2001",
                    $"节点 '{node.Id}' 的类型 '{node.Type}' 不是已注册的算子 class。",
                    1,
                    nodeId: node.Id);
            WriteOperatorCall(source, node, contract, variableNames);
            source.AppendLine();
        }

        NodeModel? end = graph.Nodes.FirstOrDefault(x => x.Type == "end-node");
        if (end is not null)
        {
            WriteNodeMetadata(source, end, variableNames);
            IEnumerable<string> values = end.Properties?.InputBindings?.Values
                ?? Enumerable.Empty<string>();
            source.Append("Return(")
                .AppendJoin(", ", values.Select(x => VariableName(variableNames, x)))
                .AppendLine(");");
        }
        return source.ToString();
    }

    private static (string Name, GraphDataModel Graph) ParseV1(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        string workflowName = string.Empty;
        Stack<(GraphDataModel Graph, string? OwnerNodeId)> graphs = new();
        GraphDataModel? root = null;
        Dictionary<string, NodeModel> nodes = new(StringComparer.Ordinal);

        foreach ((string method, JsonElement[] args, int line) in ReadCalls(source))
        {
            switch (method)
            {
                case "Workflow":
                    RequireArgs(method, args, 2, line);
                    workflowName = args[0].GetString() ?? string.Empty;
                    if (args[1].GetInt32() != 1)
                        throw Error(line, $"不支持的脚本版本 {args[1]}。");
                    break;
                case "GraphBegin":
                {
                    RequireArgs(method, args, 1, line);
                    string? ownerNodeId =
                        args[0].ValueKind == JsonValueKind.Null ? null : args[0].GetString();
                    GraphDataModel graph = new();
                    if (graphs.Count == 0)
                    {
                        root = graph;
                    }
                    else
                    {
                        if (
                            string.IsNullOrWhiteSpace(ownerNodeId)
                            || !nodes.TryGetValue(ownerNodeId, out NodeModel? owner)
                        )
                            throw Error(line, $"找不到子图所属节点 '{ownerNodeId}'。");
                        owner.Properties ??= new NodePropertiesModel();
                        owner.Properties.InnerGraphData = graph;
                    }
                    graphs.Push((graph, ownerNodeId));
                    break;
                }
                case "GraphEnd":
                    RequireArgs(method, args, 0, line);
                    if (graphs.Count == 0)
                        throw Error(line, "GraphEnd 没有匹配的 GraphBegin。");
                    graphs.Pop();
                    break;
                case "Node":
                {
                    GraphDataModel graph = CurrentGraph(graphs, line);
                    RequireArgs(method, args, 7, line);
                    NodeModel node = new()
                    {
                        Id = RequiredString(args[0], "node id", line),
                        Type = RequiredString(args[1], "node type", line),
                        X = NullableDouble(args[2]),
                        Y = NullableDouble(args[3]),
                        Text = new NodeTextModel
                        {
                            Value = NullableString(args[4]),
                            X = NullableDouble(args[5]),
                            Y = NullableDouble(args[6]),
                        },
                        Properties = new NodePropertiesModel(),
                    };
                    graph.Nodes.Add(node);
                    nodes[node.Id] = node;
                    break;
                }
                case "Input":
                case "Output":
                {
                    RequireArgs(method, args, 4, line);
                    NodeModel node = RequiredNode(nodes, args[0], line);
                    string port = RequiredString(args[1], "port", line);
                    string value = RequiredString(args[2], "binding", line);
                    string sourceKind = RequiredString(args[3], "source", line);
                    NodePropertiesModel properties = node.Properties ??= new();
                    if (method == "Input")
                    {
                        (properties.InputBindings ??= [])[port] = value;
                        (properties.InputBindingSources ??= [])[port] = sourceKind;
                    }
                    else
                    {
                        (properties.OutputBindings ??= [])[port] = value;
                        (properties.OutputBindingSources ??= [])[port] = sourceKind;
                    }
                    break;
                }
                case "Param":
                {
                    RequireArgs(method, args, 4, line);
                    NodeModel node = RequiredNode(nodes, args[0], line);
                    string name = RequiredString(args[1], "parameter", line);
                    NodePropertiesModel properties = node.Properties ??= new();
                    (properties.Params ??= [])[name] = args[2].Clone();
                    (properties.ParamSources ??= [])[name] = RequiredString(
                        args[3],
                        "source",
                        line
                    );
                    break;
                }
                case "Edge":
                {
                    RequireArgs(method, args, 6, line);
                    CurrentGraph(graphs, line)
                        .Edges.Add(
                            new EdgeModel
                            {
                                Id = RequiredString(args[0], "edge id", line),
                                SourceNodeId = NullableString(args[1]),
                                TargetNodeId = NullableString(args[2]),
                                SourceAnchorIndex = NullableInt(args[3]),
                                TargetAnchorIndex = NullableInt(args[4]),
                                Properties = new EdgePropertiesModel
                                {
                                    Branch = NullableString(args[5]),
                                },
                            }
                        );
                    break;
                }
                default:
                    throw Error(line, $"不允许的语句 '{method}'。");
            }
        }

        if (string.IsNullOrWhiteSpace(workflowName))
            throw new FormatException("脚本缺少 Workflow(name, version) 语句。");
        if (root is null || graphs.Count != 0)
            throw new FormatException("脚本 GraphBegin/GraphEnd 结构不完整。");
        return (workflowName, root);
    }

    private static (string Name, GraphDataModel Graph) ParseV2(string source)
    {
        Dictionary<string, OperatorContract> contracts = GetOperatorContractsByName();
        GraphDataModel graph = new();
        Dictionary<string, string> producers = new(StringComparer.Ordinal);
        HashSet<string> variables = new(StringComparer.Ordinal);
        string workflowName = string.Empty;
        int autoIndex = 0;
        NodeMetadata? pendingMetadata = null;
        NodeModel? start = null;
        NodeModel? end = null;

        foreach ((string statement, int line, NodeMetadata? metadata) in ReadV2Statements(source))
        {
            pendingMetadata = metadata ?? pendingMetadata;
            Match workflow = Regex.Match(
                statement,
                @"^Workflow\s*\(\s*(?<name>""(?:\\.|[^""])*"")\s*,\s*(?<version>\d+)\s*\)\s*;$",
                RegexOptions.Singleline);
            if (workflow.Success)
            {
                workflowName = JsonSerializer.Deserialize<string>(workflow.Groups["name"].Value)
                    ?? string.Empty;
                if (int.Parse(workflow.Groups["version"].Value, CultureInfo.InvariantCulture)
                    != LanguageVersion)
                    throw Error(line, "Workflow 版本必须为 2。");
                start = CreateSpecialNode(
                    "start-node", pendingMetadata, ref autoIndex, "Start");
                if (pendingMetadata?.ExternalInputs is { Count: > 0 } externalInputs)
                {
                    start.Properties = new NodePropertiesModel
                    {
                        OutputBindings = new(StringComparer.Ordinal),
                        OutputBindingSources = new(StringComparer.Ordinal),
                    };
                    int inputIndex = 0;
                    foreach (string variable in externalInputs)
                    {
                        string identifier = RequireIdentifier(variable, line);
                        if (!variables.Add(identifier))
                            throw Error(line, $"外部输入变量 '{identifier}' 重复定义。");
                        start.Properties.OutputBindings[$"input{++inputIndex}"] = identifier;
                        start.Properties.OutputBindingSources[$"input{inputIndex}"] = "variable";
                        producers[identifier] = start.Id;
                    }
                }
                graph.Nodes.Add(start);
                pendingMetadata = null;
                continue;
            }

            Match returnCall = Regex.Match(
                statement,
                @"^Return\s*\((?<args>[\s\S]*)\)\s*;$");
            if (returnCall.Success)
            {
                end = CreateSpecialNode("end-node", pendingMetadata, ref autoIndex, "End");
                end.Properties = new NodePropertiesModel
                {
                    InputBindings = new(StringComparer.Ordinal),
                    InputBindingSources = new(StringComparer.Ordinal),
                    InputBindingDisplayNames = new(StringComparer.Ordinal),
                };
                List<string> returnVariables = SplitV2Arguments(
                        returnCall.Groups["args"].Value
                    )
                    .Select(arg => RequireIdentifier(arg.Trim(), line))
                    .ToList();
                bool metadataMatches =
                    pendingMetadata?.Outputs?.Count == returnVariables.Count
                    && pendingMetadata.Outputs
                        .Select(x => x.Variable)
                        .SequenceEqual(returnVariables, StringComparer.Ordinal);
                HashSet<string> usedPorts = new(StringComparer.Ordinal);
                for (int index = 0; index < returnVariables.Count; index++)
                {
                    string variable = returnVariables[index];
                    if (!variables.Contains(variable))
                        throw Error(line, $"Return 引用了未定义变量 '{variable}'。");
                    EndOutputMetadata? outputMetadata = metadataMatches
                        ? pendingMetadata!.Outputs![index]
                        : null;
                    string portName =
                        !string.IsNullOrWhiteSpace(outputMetadata?.Port)
                        && usedPorts.Add(outputMetadata.Port)
                            ? outputMetadata.Port
                            : $"output{index + 1}";
                    usedPorts.Add(portName);
                    end.Properties.InputBindings[portName] = variable;
                    end.Properties.InputBindingSources[portName] = "variable";
                    if (!string.IsNullOrWhiteSpace(outputMetadata?.DisplayName))
                    {
                        end.Properties.InputBindingDisplayNames[portName] =
                            outputMetadata.DisplayName.Trim();
                    }
                }
                if (end.Properties.InputBindingDisplayNames.Count == 0)
                    end.Properties.InputBindingDisplayNames = null;
                graph.Nodes.Add(end);
                pendingMetadata = null;
                continue;
            }

            Match call = Regex.Match(
                statement,
                @"^(?:(?:var\s+(?<single>[A-Za-z_][A-Za-z0-9_]*)|var\s*\((?<tuple>[^)]*)\))\s*=\s*)?(?<operator>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<args>[\s\S]*)\)\s*;$",
                RegexOptions.Singleline);
            if (!call.Success)
                throw Error(line, "需要 Workflow、Return 或算子 class 调用语句。");

            string operatorName = call.Groups["operator"].Value;
            if (!contracts.TryGetValue(operatorName, out OperatorContract? contract))
                throw Error(line, $"未知算子 class '{operatorName}'。");

            List<string> outputVariables = call.Groups["single"].Success
                ? [call.Groups["single"].Value]
                : call.Groups["tuple"].Success
                    ? SplitV2Arguments(call.Groups["tuple"].Value)
                        .Select(x => RequireIdentifier(x.Trim(), line)).ToList()
                    : [];
            if (outputVariables.Count != contract.Outputs.Count)
                throw Error(
                    line,
                    $"算子 {operatorName} 声明 {contract.Outputs.Count} 个输出，实际绑定 {outputVariables.Count} 个。");
            foreach (string variable in outputVariables)
            {
                if (!variables.Add(variable))
                    throw Error(line, $"变量 '{variable}' 重复定义。");
            }

            NodeModel node = CreateOperatorNode(contract, pendingMetadata, ref autoIndex);
            NodePropertiesModel properties = node.Properties!;
            List<string> arguments = SplitV2Arguments(call.Groups["args"].Value).ToList();
            List<string> positional = [];
            Dictionary<string, string> named = new(StringComparer.Ordinal);
            bool sawNamed = false;
            foreach (string argument in arguments)
            {
                Match namedArg = Regex.Match(
                    argument,
                    @"^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<value>[\s\S]+)$");
                if (namedArg.Success)
                {
                    sawNamed = true;
                    if (!named.TryAdd(namedArg.Groups["name"].Value, namedArg.Groups["value"].Value.Trim()))
                        throw Error(line, $"配置参数 '{namedArg.Groups["name"].Value}' 重复。");
                }
                else
                {
                    if (sawNamed)
                        throw Error(line, "位置输入参数不能出现在命名配置参数之后。");
                    positional.Add(argument.Trim());
                }
            }
            if (positional.Count != contract.Inputs.Count)
                throw Error(
                    line,
                    $"算子 {operatorName} 需要 {contract.Inputs.Count} 个输入，实际 {positional.Count} 个。");

            for (int i = 0; i < contract.Inputs.Count; i++)
                AddInputBinding(properties, contract.Inputs[i], positional[i], variables, line);
            foreach (string unknown in named.Keys.Except(contract.Config, StringComparer.Ordinal))
                throw Error(line, $"算子 {operatorName} 没有配置参数 '{unknown}'。");
            foreach (string configName in contract.Config)
            {
                if (!named.TryGetValue(configName, out string? value))
                    continue;
                AddParamBinding(properties, configName, value, variables, line);
            }
            for (int i = 0; i < contract.Outputs.Count; i++)
            {
                properties.OutputBindings![contract.Outputs[i]] = outputVariables[i];
                properties.OutputBindingSources![contract.Outputs[i]] = "variable";
                producers[outputVariables[i]] = node.Id;
            }
            graph.Nodes.Add(node);
            pendingMetadata = null;
        }

        if (string.IsNullOrWhiteSpace(workflowName) || start is null)
            throw Error(1, "脚本缺少 Workflow(name, 2) 语句。");

        BuildDerivedEdges(graph, start, end, producers);
        return (workflowName, graph);
    }

    private static void WriteGraph(
        StringBuilder source,
        GraphDataModel graph,
        string? ownerNodeId,
        int depth
    )
    {
        string indent = new(' ', depth * 4);
        source.Append(indent).Append("GraphBegin(").Append(Json(ownerNodeId)).AppendLine(");");
        foreach (NodeModel node in graph.Nodes)
        {
            source.Append(indent).Append("    Node(")
                .AppendJoin(
                    ", ",
                    Json(node.Id),
                    Json(node.Type),
                    Number(node.X),
                    Number(node.Y),
                    Json(node.Text?.Value),
                    Number(node.Text?.X),
                    Number(node.Text?.Y)
                )
                .AppendLine(");");
            NodePropertiesModel properties = node.Properties ?? new();
            WriteBindings(source, indent, "Input", node.Id, properties.InputBindings, properties.InputBindingSources);
            WriteBindings(source, indent, "Output", node.Id, properties.OutputBindings, properties.OutputBindingSources);
            foreach ((string name, JsonElement value) in properties.Params ?? [])
            {
                source.Append(indent).Append("    Param(")
                    .Append(Json(node.Id)).Append(", ").Append(Json(name)).Append(", ")
                    .Append(value.GetRawText()).Append(", ")
                    .Append(Json(properties.ParamSources?.GetValueOrDefault(name) ?? "literal"))
                    .AppendLine(");");
            }
            if (properties.InnerGraphData is not null)
                WriteGraph(source, properties.InnerGraphData, node.Id, depth + 1);
        }
        foreach (EdgeModel edge in graph.Edges)
        {
            source.Append(indent).Append("    Edge(")
                .AppendJoin(
                    ", ",
                    Json(edge.Id),
                    Json(edge.SourceNodeId),
                    Json(edge.TargetNodeId),
                    Integer(edge.SourceAnchorIndex),
                    Integer(edge.TargetAnchorIndex),
                    Json(edge.Properties?.Branch)
                )
                .AppendLine(");");
        }
        source.Append(indent).AppendLine("GraphEnd();");
    }

    private sealed record OperatorContract(
        Guid Id,
        Type Type,
        string Name,
        string DisplayName,
        IReadOnlyList<string> Inputs,
        IReadOnlyList<string> Outputs,
        IReadOnlyList<string> Config);

    private sealed record NodeMetadata(
        string? Id,
        double? X,
        double? Y,
        double? TextX,
        double? TextY,
        string? Title,
        IReadOnlyList<string>? ExternalInputs,
        IReadOnlyList<EndOutputMetadata>? Outputs);

    private sealed record EndOutputMetadata(
        string Port,
        string Variable,
        string? DisplayName);

    private static void WriteOperatorCall(
        StringBuilder source,
        NodeModel node,
        OperatorContract contract,
        IReadOnlyDictionary<string, string> variableNames)
    {
        WriteNodeMetadata(source, node, variableNames);
        NodePropertiesModel properties = node.Properties ?? new();
        List<string> outputs = contract.Outputs
            .Select(port => VariableName(
                variableNames,
                properties.OutputBindings?.GetValueOrDefault(port) ?? port))
            .ToList();
        if (outputs.Count == 1)
            source.Append("var ").Append(outputs[0]).Append(" = ");
        else if (outputs.Count > 1)
            source.Append("var (").AppendJoin(", ", outputs).Append(") = ");

        source.Append(contract.Name).AppendLine("(");
        List<string> args = [];
        foreach (string port in contract.Inputs)
        {
            string? value = null;
            bool hasBinding = properties.InputBindings is not null
                && properties.InputBindings.TryGetValue(port, out value);
            value ??= "null";
            string kind = hasBinding
                ? properties.InputBindingSources?.GetValueOrDefault(port) ?? "variable"
                : "literal";
            args.Add(
                kind == "variable"
                    ? VariableName(variableNames, value)
                    : LiteralFromString(value));
        }
        foreach (string name in contract.Config)
        {
            if (properties.Params?.TryGetValue(name, out JsonElement value) != true)
                continue;
            string kind = properties.ParamSources?.GetValueOrDefault(name) ?? "literal";
            args.Add(
                name + ": " + (
                    kind == "variable"
                        ? VariableName(variableNames, ReadVariableFromJson(value))
                        : value.GetRawText()));
        }
        for (int i = 0; i < args.Count; i++)
            source.Append("    ").Append(args[i])
                .AppendLine(i + 1 == args.Count ? string.Empty : ",");
        source.AppendLine(");");
    }

    private static void WriteNodeMetadata(
        StringBuilder source,
        NodeModel node,
        IReadOnlyDictionary<string, string> variableNames)
    {
        var metadata = new
        {
            id = node.Id,
            x = node.X,
            y = node.Y,
            textX = node.Text?.X,
            textY = node.Text?.Y,
            title = node.Text?.Value,
            externalInputs = node.Type == "start-node"
                ? node.Properties?.OutputBindings?.Values
                    .Select(x => VariableName(variableNames, x)).ToArray()
                : null,
            outputs = node.Type == "end-node"
                ? node.Properties?.InputBindings?.Select(binding => new
                    {
                        port = binding.Key,
                        variable = VariableName(variableNames, binding.Value),
                        displayName = node.Properties.InputBindingDisplayNames
                            ?.GetValueOrDefault(binding.Key),
                    })
                    .ToArray()
                : null,
        };
        source.Append("// @node ")
            .AppendLine(JsonSerializer.Serialize(metadata, NodeMetadataJsonOptions));
    }

    private static NodeModel CreateSpecialNode(
        string type,
        NodeMetadata? metadata,
        ref int autoIndex,
        string title) =>
        new()
        {
            Id = metadata?.Id ?? $"auto-{type}-{++autoIndex}",
            Type = type,
            X = metadata?.X ?? 560,
            Y = metadata?.Y ?? (type == "start-node" ? 40 : 120 + autoIndex * 100),
            Text = new NodeTextModel
            {
                Value = metadata?.Title ?? title,
                X = metadata?.TextX,
                Y = metadata?.TextY,
            },
            Properties = new NodePropertiesModel(),
        };

    private static NodeModel CreateOperatorNode(
        OperatorContract contract,
        NodeMetadata? metadata,
        ref int autoIndex)
    {
        int index = ++autoIndex;
        return new NodeModel
        {
            Id = metadata?.Id ?? $"auto-{contract.Name}-{index}",
            Type = contract.Id.ToString(),
            X = metadata?.X ?? 560,
            Y = metadata?.Y ?? 40 + index * 100,
            Text = new NodeTextModel
            {
                Value = metadata?.Title ?? contract.DisplayName,
                X = metadata?.TextX,
                Y = metadata?.TextY,
            },
            Properties = new NodePropertiesModel
            {
                InputBindings = new(StringComparer.Ordinal),
                InputBindingSources = new(StringComparer.Ordinal),
                OutputBindings = new(StringComparer.Ordinal),
                OutputBindingSources = new(StringComparer.Ordinal),
                Params = new(StringComparer.Ordinal),
                ParamSources = new(StringComparer.Ordinal),
            },
        };
    }

    private static void AddInputBinding(
        NodePropertiesModel properties,
        string port,
        string expression,
        IReadOnlySet<string> variables,
        int line)
    {
        // Compatibility for V2 scripts produced before the null-binding fix:
        // the generator normalized an unbound `null` input to the identifier `_null`.
        if (string.Equals(expression, "_null", StringComparison.Ordinal)
            && !variables.Contains(expression))
        {
            properties.InputBindings![port] = "null";
            properties.InputBindingSources![port] = "literal";
            return;
        }
        if (IsIdentifier(expression))
        {
            if (!variables.Contains(expression))
                throw Error(line, $"输入引用了未定义变量 '{expression}'。");
            properties.InputBindings![port] = expression;
            properties.InputBindingSources![port] = "variable";
            return;
        }
        JsonElement literal = ParseLiteral(expression, line);
        properties.InputBindings![port] = LiteralToBindingString(literal);
        properties.InputBindingSources![port] = "literal";
    }

    private static void AddParamBinding(
        NodePropertiesModel properties,
        string name,
        string expression,
        IReadOnlySet<string> variables,
        int line)
    {
        if (IsIdentifier(expression))
        {
            if (!variables.Contains(expression))
                throw Error(line, $"配置参数引用了未定义变量 '{expression}'。");
            using JsonDocument variable = JsonDocument.Parse(
                JsonSerializer.Serialize(new Dictionary<string, string> { ["$var"] = expression }));
            properties.Params![name] = variable.RootElement.Clone();
            properties.ParamSources![name] = "variable";
            return;
        }
        properties.Params![name] = ParseLiteral(expression, line);
        properties.ParamSources![name] = "literal";
    }

    private static void BuildDerivedEdges(
        GraphDataModel graph,
        NodeModel start,
        NodeModel? end,
        IReadOnlyDictionary<string, string> producers)
    {
        HashSet<(string Source, string Target)> pairs = [];
        foreach (NodeModel node in graph.Nodes.Where(x =>
                     x.Type is not "start-node" and not "end-node"))
        {
            List<string> dependencies = (node.Properties?.InputBindings ?? [])
                .Where(x =>
                    node.Properties?.InputBindingSources?.GetValueOrDefault(x.Key) == "variable")
                .Select(x => producers.GetValueOrDefault(x.Value))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (dependencies.Count == 0)
                dependencies.Add(start.Id);
            foreach (string source in dependencies)
                AddDerivedEdge(graph, pairs, source, node.Id);
        }
        if (end is not null)
        {
            List<string> endSources = (end.Properties?.InputBindings?.Values
                    ?? Enumerable.Empty<string>())
                .Select(x => producers.GetValueOrDefault(x))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();
            foreach (string source in endSources)
                AddDerivedEdge(graph, pairs, source, end.Id);
        }
    }

    private static void AddDerivedEdge(
        GraphDataModel graph,
        HashSet<(string Source, string Target)> pairs,
        string source,
        string target)
    {
        if (!pairs.Add((source, target)))
            return;
        string key = source + "\n" + target;
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24]
            .ToLowerInvariant();
        graph.Edges.Add(new EdgeModel
        {
            Id = $"edge-{id}",
            SourceNodeId = source,
            TargetNodeId = target,
            SourceAnchorIndex = 2,
            TargetAnchorIndex = 0,
        });
    }

    private static Dictionary<Guid, OperatorContract> GetOperatorContractsById() =>
        GetOperatorContracts().ToDictionary(x => x.Id);

    private static Dictionary<string, OperatorContract> GetOperatorContractsByName()
    {
        List<OperatorContract> contracts = GetOperatorContracts();
        IGrouping<string, OperatorContract>? duplicate = contracts
            .GroupBy(x => x.Name, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"算子 class 名称 '{duplicate.Key}' 冲突："
                + string.Join(", ", duplicate.Select(x => x.Type.FullName)));
        return contracts.ToDictionary(x => x.Name, StringComparer.Ordinal);
    }

    private static List<OperatorContract> GetOperatorContracts()
    {
        return typeof(IOperator).Assembly.GetTypes()
            .Where(x => x.IsClass && !x.IsAbstract && typeof(IOperator).IsAssignableFrom(x))
            .Select(TryCreateContract)
            .Where(x => x is not null)
            .Cast<OperatorContract>()
            .ToList();
    }

    private static OperatorContract? TryCreateContract(Type type)
    {
        object? guidAttribute = type.GetCustomAttributes()
            .FirstOrDefault(x => x.GetType().Name == "GuidAttribute");
        string? guidText = guidAttribute?.GetType().GetProperty("Value")?
            .GetValue(guidAttribute)?.ToString();
        if (!Guid.TryParse(guidText, out Guid id))
            return null;
        object? displayAttribute = type.GetCustomAttributes()
            .FirstOrDefault(x => x.GetType().Name == "DisplayNameAttribute");
        string displayName = displayAttribute?.GetType()
            .GetProperty("DisplayName")?.GetValue(displayAttribute)?.ToString()
            ?? type.Name;
        return new OperatorContract(
            id,
            type,
            type.Name,
            displayName,
            ReadParameterNames(type, "InputVisionParameters", "ParameterName"),
            ReadParameterNames(type, "OutputVisionParameters", "ParameterName"),
            ReadParameterNames(type, "ConfigParameters", "Name"));
    }

    private static IReadOnlyList<string> ReadParameterNames(
        Type type,
        string propertyName,
        string itemProperty)
    {
        object? value = type.GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (value is not System.Collections.IEnumerable items)
            return [];
        return items.Cast<object>()
            .Select(x => x.GetType().GetProperty(itemProperty)?.GetValue(x)?.ToString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();
    }

    private static IEnumerable<(string Statement, int Line, NodeMetadata? Metadata)>
        ReadV2Statements(string source)
    {
        NodeMetadata? pending = null;
        StringBuilder statement = new();
        int statementLine = 1;
        int line = 1;
        bool inString = false;
        bool escaped = false;
        int nesting = 0;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (!inString && c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                int end = source.IndexOf('\n', i);
                if (end < 0) end = source.Length;
                string comment = source[(i + 2)..end].Trim();
                if (comment.StartsWith("@node ", StringComparison.Ordinal))
                    pending = ParseNodeMetadata(comment[6..], line);
                i = end - 1;
                continue;
            }
            if (c == '\n') line++;
            if (statement.Length == 0 && char.IsWhiteSpace(c))
                continue;
            if (statement.Length == 0)
                statementLine = line;
            statement.Append(c);
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c is '(' or '[' or '{') nesting++;
            else if (c is ')' or ']' or '}') nesting--;
            else if (c == ';' && nesting == 0)
            {
                yield return (statement.ToString().Trim(), statementLine, pending);
                statement.Clear();
                pending = null;
            }
        }
        if (!string.IsNullOrWhiteSpace(statement.ToString()))
            throw Error(statementLine, "语句末尾缺少分号。");
    }

    private static NodeMetadata ParseNodeMetadata(string json, int line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            return new NodeMetadata(
                root.TryGetProperty("id", out JsonElement id) ? id.GetString() : null,
                ReadNullableDouble(root, "x"),
                ReadNullableDouble(root, "y"),
                ReadNullableDouble(root, "textX"),
                ReadNullableDouble(root, "textY"),
                root.TryGetProperty("title", out JsonElement title) ? title.GetString() : null,
                root.TryGetProperty("externalInputs", out JsonElement inputs)
                && inputs.ValueKind == JsonValueKind.Array
                    ? inputs.EnumerateArray()
                        .Select(x => x.GetString() ?? string.Empty)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToArray()
                    : null,
                ReadEndOutputs(root));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw Error(line, $"@node 元数据不是有效 JSON：{ex.Message}");
        }
    }

    private static IReadOnlyList<EndOutputMetadata>? ReadEndOutputs(JsonElement root)
    {
        if (
            !root.TryGetProperty("outputs", out JsonElement outputs)
            || outputs.ValueKind != JsonValueKind.Array
        )
        {
            return null;
        }

        return outputs
            .EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Object)
            .Select(x => new EndOutputMetadata(
                x.TryGetProperty("port", out JsonElement port)
                    ? port.GetString() ?? string.Empty
                    : string.Empty,
                x.TryGetProperty("variable", out JsonElement variable)
                    ? variable.GetString() ?? string.Empty
                    : string.Empty,
                x.TryGetProperty("displayName", out JsonElement displayName)
                    ? displayName.GetString()
                    : null))
            .ToArray();
    }

    private static double? ReadNullableDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    internal static IReadOnlyList<string> SplitV2Arguments(string text)
    {
        List<string> result = [];
        int start = 0;
        int depth = 0;
        bool inString = false;
        bool escaped = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (c == ',' && depth == 0)
            {
                if (!string.IsNullOrWhiteSpace(text[start..i]))
                    result.Add(text[start..i].Trim());
                start = i + 1;
            }
        }
        if (!string.IsNullOrWhiteSpace(text[start..]))
            result.Add(text[start..].Trim());
        return result;
    }

    private static JsonElement ParseLiteral(string expression, int line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(expression);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw Error(line, $"'{expression}' 不是支持的字面量或变量。");
        }
    }

    private static string LiteralToBindingString(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.GetRawText();

    private static string LiteralFromString(string value)
    {
        string trimmed = value.Trim();
        if (trimmed is "true" or "false" or "null"
            || decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            return trimmed;
        if ((trimmed.StartsWith('{') && trimmed.EndsWith('}'))
            || (trimmed.StartsWith('[') && trimmed.EndsWith(']')))
        {
            try
            {
                using JsonDocument _ = JsonDocument.Parse(trimmed);
                return trimmed;
            }
            catch (JsonException) { }
        }
        return Json(value);
    }

    private static string ReadVariableFromJson(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("$var", out JsonElement variable))
            return variable.GetString() ?? string.Empty;
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.GetRawText();
    }

    private static int ReadLanguageVersion(string source)
    {
        Match match = Regex.Match(
            source,
            @"Workflow\s*\(\s*""(?:\\.|[^""])*""\s*,\s*(?<version>\d+)\s*\)",
            RegexOptions.Singleline);
        if (!match.Success)
            throw Error(1, "脚本缺少 Workflow(name, version) 语句。");
        return int.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture);
    }

    private static IReadOnlyDictionary<string, string> BuildVariableNames(GraphDataModel graph)
    {
        List<string> values = [];
        foreach (NodeModel node in graph.Nodes)
        {
            NodePropertiesModel? properties = node.Properties;
            if (properties is null)
                continue;
            values.AddRange((properties.OutputBindings ?? []).Values);
            values.AddRange(
                (properties.InputBindings ?? [])
                    .Where(x =>
                        properties.InputBindingSources?.GetValueOrDefault(x.Key)
                        == "variable")
                    .Select(x => x.Value));
            foreach ((string name, JsonElement value) in properties.Params ?? [])
            {
                if (properties.ParamSources?.GetValueOrDefault(name) == "variable")
                    values.Add(ReadVariableFromJson(value));
            }
        }

        Dictionary<string, string> result = new(StringComparer.Ordinal);
        HashSet<string> used = new(StringComparer.Ordinal);
        foreach (string value in values.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (result.ContainsKey(value))
                continue;
            string candidate = Identifier(value);
            string unique = candidate;
            int suffix = 2;
            while (!used.Add(unique))
                unique = candidate + suffix++.ToString(CultureInfo.InvariantCulture);
            result[value] = unique;
        }
        return result;
    }

    private static IReadOnlyList<NodeModel> OrderOperatorNodes(GraphDataModel graph)
    {
        List<NodeModel> nodes = graph.Nodes
            .Where(x => x.Type is not "start-node" and not "end-node")
            .ToList();
        Dictionary<string, int> originalOrder = nodes
            .Select((node, index) => (node.Id, index))
            .ToDictionary(x => x.Id, x => x.index, StringComparer.Ordinal);
        Dictionary<string, string> producers = nodes
            .SelectMany(node => (node.Properties?.OutputBindings?.Values
                    ?? Enumerable.Empty<string>())
                .Select(variable => (variable, node.Id)))
            .GroupBy(x => x.variable, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First().Id, StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> dependencies = nodes.ToDictionary(
            x => x.Id,
            x => (x.Properties?.InputBindings ?? [])
                .Where(binding =>
                    x.Properties?.InputBindingSources?.GetValueOrDefault(binding.Key)
                    == "variable")
                .Select(binding => producers.GetValueOrDefault(binding.Value))
                .Where(id => !string.IsNullOrWhiteSpace(id) && id != x.Id)
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
        List<NodeModel> ordered = [];
        HashSet<string> emitted = new(StringComparer.Ordinal);
        while (ordered.Count < nodes.Count)
        {
            NodeModel? next = nodes
                .Where(x => !emitted.Contains(x.Id)
                    && dependencies[x.Id].All(emitted.Contains))
                .OrderBy(x => originalOrder[x.Id])
                .FirstOrDefault();
            if (next is null)
            {
                // Preserve the original order for an invalid/cyclic graph; validation
                // will report the actual cycle with node context.
                ordered.AddRange(nodes.Where(x => !emitted.Contains(x.Id)));
                break;
            }
            emitted.Add(next.Id);
            ordered.Add(next);
        }
        return ordered;
    }

    private static string VariableName(
        IReadOnlyDictionary<string, string> variableNames,
        string original) =>
        variableNames.GetValueOrDefault(original) ?? Identifier(original);

    private static readonly IReadOnlySet<string> CSharpKeywords = new HashSet<string>(
        [
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch",
            "char", "checked", "class", "const", "continue", "decimal", "default",
            "delegate", "do", "double", "else", "enum", "event", "explicit",
            "extern", "false", "finally", "fixed", "float", "for", "foreach",
            "goto", "if", "implicit", "in", "int", "interface", "internal", "is",
            "lock", "long", "namespace", "new", "null", "object", "operator",
            "out", "override", "params", "private", "protected", "public",
            "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
            "stackalloc", "static", "string", "struct", "switch", "this", "throw",
            "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
            "ushort", "using", "virtual", "void", "volatile", "while", "var",
        ],
        StringComparer.Ordinal);

    private static bool IsIdentifier(string value) =>
        Regex.IsMatch(value, @"^[A-Za-z_][A-Za-z0-9_]*$")
        && !CSharpKeywords.Contains(value);

    private static string RequireIdentifier(string value, int line) =>
        IsIdentifier(value)
            ? value
            : throw Error(line, $"'{value}' 不是合法的英文变量名。");

    private static string Identifier(string value)
    {
        if (IsIdentifier(value))
            return value;
        StringBuilder result = new();
        foreach (char c in value)
            result.Append(
                c <= 127 && (char.IsLetterOrDigit(c) || c == '_') ? c : '_');
        if (result.Length == 0 || char.IsDigit(result[0]))
            result.Insert(0, '_');
        if (CSharpKeywords.Contains(result.ToString()))
            result.Insert(0, '_');
        return result.ToString();
    }

    private static void WriteBindings(
        StringBuilder source,
        string indent,
        string method,
        string nodeId,
        Dictionary<string, string>? bindings,
        Dictionary<string, string>? sources
    )
    {
        foreach ((string port, string value) in bindings ?? [])
            source.Append(indent).Append("    ").Append(method).Append("(")
                .AppendJoin(", ", Json(nodeId), Json(port), Json(value), Json(sources?.GetValueOrDefault(port) ?? "variable"))
                .AppendLine(");");
    }

    private static IEnumerable<(string Method, JsonElement[] Args, int Line)> ReadCalls(string source)
    {
        StringBuilder call = new();
        int callLine = 0;
        int line = 1;
        bool inString = false;
        bool escaped = false;
        int nesting = 0;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '\n') line++;
            if (!inString && c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
                line++;
                continue;
            }
            if (call.Length == 0 && char.IsWhiteSpace(c)) continue;
            if (call.Length == 0) callLine = line;
            call.Append(c);
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c is '(' or '[' or '{') nesting++;
            else if (c is ')' or ']' or '}') nesting--;
            else if (c == ';' && nesting == 0)
            {
                string statement = call.ToString().Trim();
                call.Clear();
                int open = statement.IndexOf('(');
                int close = statement.LastIndexOf(')');
                if (open <= 0 || close < open)
                    throw Error(callLine, "语句必须是 Method(...); 形式。");
                string method = statement[..open].Trim();
                string argsText = statement[(open + 1)..close];
                using JsonDocument args = JsonDocument.Parse("[" + argsText + "]");
                yield return (
                    method,
                    args.RootElement.EnumerateArray().Select(x => x.Clone()).ToArray(),
                    callLine
                );
            }
        }
        if (call.ToString().Trim().Length > 0)
            throw Error(callLine, "语句末尾缺少分号。");
    }

    private static GraphDataModel CurrentGraph(
        Stack<(GraphDataModel Graph, string? OwnerNodeId)> graphs,
        int line
    ) => graphs.Count == 0 ? throw Error(line, "语句不在 GraphBegin/GraphEnd 内。") : graphs.Peek().Graph;

    private static NodeModel RequiredNode(
        IReadOnlyDictionary<string, NodeModel> nodes,
        JsonElement value,
        int line
    )
    {
        string id = RequiredString(value, "node id", line);
        return nodes.TryGetValue(id, out NodeModel? node)
            ? node
            : throw Error(line, $"找不到节点 '{id}'。");
    }

    private static void RequireArgs(string method, JsonElement[] args, int count, int line)
    {
        if (args.Length != count)
            throw Error(line, $"{method} 需要 {count} 个参数，实际 {args.Length} 个。");
    }

    private static string RequiredString(JsonElement value, string name, int line) =>
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw Error(line, $"{name} 必须是非空字符串。");

    private static string? NullableString(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    private static double? NullableDouble(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? null : value.GetDouble();
    private static int? NullableInt(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();
    private static string Json(string? value) =>
        JsonSerializer.Serialize(value, ScriptJsonOptions);
    private static string Number(double? value) =>
        value?.ToString("R", CultureInfo.InvariantCulture) ?? "null";
    private static string Integer(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "null";
    private static WorkflowScriptException Error(int line, string message) =>
        new("WFS1001", message, line);
}

public static class WorkflowProgramHash
{
    private static readonly Lazy<string> OperatorContractHash =
        new(ComputeOperatorContractHashCore, LazyThreadSafetyMode.ExecutionAndPublication);
    public static string NormalizeSource(string source) => source.Replace("\r\n", "\n").Trim();

    public static string ComputeContentHash(string source) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeSource(source)))
        );

    public static string ComputeSourceHash(string source) => ComputeContentHash(source);

    public static string ComputeSemanticHash(string source)
    {
        (string name, GraphDataModel graph) = CSharpWorkflowScript.Parse(source);
        string canonical = CSharpWorkflowScript.Generate(name, graph);
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeSource(canonical)))
        );
    }

    public static string ComputeOperatorContractHash() => OperatorContractHash.Value;

    private static string ComputeOperatorContractHashCore()
    {
        Assembly assembly = typeof(IOperator).Assembly;
        List<object> contracts = assembly.GetTypes()
            .Where(x => x.IsClass && !x.IsAbstract && typeof(IOperator).IsAssignableFrom(x))
            .Select(type => new
            {
                Type = type.FullName,
                Version = assembly.GetName().Version?.ToString(),
                Id = ReadOperatorId(type),
                Inputs = ReadContractItems(type, "InputVisionParameters"),
                Outputs = ReadContractItems(type, "OutputVisionParameters"),
                Config = ReadContractItems(type, "ConfigParameters"),
            })
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ThenBy(x => x.Type, StringComparer.Ordinal)
            .Cast<object>().ToList();
        string contract = JsonSerializer.Serialize(contracts);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contract)));
    }

    private static IReadOnlyList<Dictionary<string, string?>> ReadContractItems(
        Type operatorType,
        string propertyName
    )
    {
        object? value = operatorType.GetProperty(
            propertyName, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (value is not System.Collections.IEnumerable items)
            return [];
        string[] propertyNames =
        [
            "ParameterName", "Name", "ParameterType", "DefaultValue", "ValueLimit",
            "JsonSchema", "ErrorCheck", "Required", "ControlType", "MatType",
        ];
        List<Dictionary<string, string?>> result = [];
        foreach (object item in items)
        {
            Dictionary<string, string?> contract = new(StringComparer.Ordinal);
            foreach (string name in propertyNames)
            {
                PropertyInfo? property = item.GetType().GetProperty(name);
                if (property is null)
                    continue;
                object? itemValue = property.GetValue(item);
                contract[name] = CanonicalContractValue(itemValue);
            }
            result.Add(contract);
        }
        return result.OrderBy(x => x.GetValueOrDefault("ParameterName")
                                   ?? x.GetValueOrDefault("Name"), StringComparer.Ordinal).ToList();
    }

    private static string? ReadOperatorId(Type type)
    {
        object? attribute = type.GetCustomAttributes()
            .FirstOrDefault(x => x.GetType().Name == "GuidAttribute");
        return attribute?.GetType().GetProperty("Value")?.GetValue(attribute)?.ToString();
    }

    private static string? CanonicalContractValue(object? value)
    {
        if (value is null)
            return null;
        if (value is Type type)
            return type.FullName;
        try { return JsonSerializer.Serialize(value, value.GetType()); }
        catch (NotSupportedException) { return Convert.ToString(value, CultureInfo.InvariantCulture); }
    }

    public static string ComputeProgramHash(string source)
    {
        string semanticHash = ComputeSemanticHash(source);
        string operatorContractHash = ComputeOperatorContractHash();
        string payload =
            $"aurora-csharp-workflow:{CSharpWorkflowScript.LanguageVersion}:{operatorContractHash}:{semanticHash}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
