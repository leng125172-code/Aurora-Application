using System.Globalization;
using System.Text;
using System.Text.Json;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using System.Security.Cryptography;
using System.Reflection;

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
    public const int LanguageVersion = 1;
    public static readonly IReadOnlySet<string> AllowedMethods = new HashSet<string>(
        ["Workflow", "GraphBegin", "GraphEnd", "Node", "Input", "Output", "Param", "Edge"],
        StringComparer.Ordinal
    );

    public static string Generate(string workflowName, GraphDataModel graph)
    {
        StringBuilder source = new();
        source.AppendLine("// Aurora Workflow Script - restricted C# syntax");
        source.Append("Workflow(").Append(Json(workflowName)).Append(", ").Append(LanguageVersion)
            .AppendLine(");");
        WriteGraph(source, graph, null, 0);
        return source.ToString();
    }

    public static (string Name, GraphDataModel Graph) Parse(string source)
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
                    if (args[1].GetInt32() != LanguageVersion)
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
    private static string Json(string? value) => JsonSerializer.Serialize(value);
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
