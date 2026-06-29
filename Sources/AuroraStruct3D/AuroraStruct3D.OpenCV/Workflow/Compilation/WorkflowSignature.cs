using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;

namespace AuroraStruct3D.OpenCV.Workflow.Compilation;

/// <summary>
/// 工作流对外 I/O 签名：由起止节点的端口绑定推导。
/// <list type="bullet">
///   <item><see cref="Inputs"/> ← start-node 的 <c>outputBindings</c> 值（外部注入的变量名）</item>
///   <item><see cref="Outputs"/> ← end-node 的 <c>inputBindings</c> 值（对外产出的变量名）</item>
/// </list>
/// </summary>
public sealed class WorkflowSignature
{
    /// <summary>工作流输入变量名（执行前从外部/Redis 暂存注入）。</summary>
    public required IReadOnlyList<string> Inputs { get; init; }

    /// <summary>工作流输出变量名（执行后对外产出/写回 Redis 暂存）。</summary>
    public required IReadOnlyList<string> Outputs { get; init; }
}

/// <summary>从工作流图提取 <see cref="WorkflowSignature"/>。</summary>
public static class WorkflowSignatureExtractor
{
    /// <summary>从持久化内容（WorkflowPayload / 裸 graphData）提取签名。</summary>
    public static WorkflowSignature Extract(string contentJson)
    {
        (_, GraphDataModel graph) = WorkflowGraphCompiler.ParseContent(contentJson);
        return Extract(graph);
    }

    /// <summary>从已解析的顶层图提取签名。</summary>
    public static WorkflowSignature Extract(GraphDataModel graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        List<string> inputs = CollectBindingValues(graph, NodeTypeTokens.StartNode, output: true);
        List<string> outputs = CollectBindingValues(graph, NodeTypeTokens.EndNode, output: false);

        return new WorkflowSignature { Inputs = inputs, Outputs = outputs };
    }

    private static List<string> CollectBindingValues(
        GraphDataModel graph,
        string nodeType,
        bool output
    )
    {
        return graph
            .Nodes.Where(n => n.Type == nodeType)
            .SelectMany(n =>
            {
                Dictionary<string, string>? bindings = output
                    ? n.Properties?.OutputBindings
                    : n.Properties?.InputBindings;
                return bindings?.Values ?? Enumerable.Empty<string>();
            })
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
