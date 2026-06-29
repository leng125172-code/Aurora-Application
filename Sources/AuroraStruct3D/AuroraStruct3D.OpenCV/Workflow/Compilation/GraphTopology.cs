using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;

namespace AuroraStruct3D.OpenCV.Workflow.Compilation;

/// <summary>
/// 对单层 <see cref="GraphDataModel"/> 的节点做拓扑排序（Kahn 算法）。
/// <para>
/// 仅依据本层 edges 排序；跨层 / 悬空的边被忽略（连线隔离规则）。
/// 同入度可选时按节点声明顺序选取，保证结果稳定。检测到环则抛出
/// <see cref="WorkflowCompilationException"/>。
/// </para>
/// </summary>
internal static class GraphTopology
{
    public static IReadOnlyList<NodeModel> Order(GraphDataModel graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        List<NodeModel> nodes = graph.Nodes;
        if (nodes.Count == 0)
            return Array.Empty<NodeModel>();

        // id → 声明顺序索引；重复 id 视为非法图。
        var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < nodes.Count; i++)
        {
            if (!indexById.TryAdd(nodes[i].Id, i))
            {
                throw new WorkflowCompilationException(
                    $"图中存在重复节点 ID '{nodes[i].Id}'，无法编译。"
                );
            }
        }

        var indegree = new int[nodes.Count];
        var adjacency = new List<int>[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
            adjacency[i] = new List<int>();

        foreach (EdgeModel edge in graph.Edges)
        {
            if (edge.SourceNodeId is null || edge.TargetNodeId is null)
                continue;
            if (
                !indexById.TryGetValue(edge.SourceNodeId, out int from)
                || !indexById.TryGetValue(edge.TargetNodeId, out int to)
            )
            {
                // 跨层 / 悬空边：忽略。
                continue;
            }

            adjacency[from].Add(to);
            indegree[to]++;
        }

        // 入度为 0 的节点按声明顺序入选；SortedSet<int> 保证每次取最小索引（稳定）。
        var available = new SortedSet<int>();
        for (int i = 0; i < nodes.Count; i++)
        {
            if (indegree[i] == 0)
                available.Add(i);
        }

        var ordered = new List<NodeModel>(nodes.Count);
        while (available.Count > 0)
        {
            int idx = available.Min;
            available.Remove(idx);
            ordered.Add(nodes[idx]);

            foreach (int next in adjacency[idx])
            {
                if (--indegree[next] == 0)
                    available.Add(next);
            }
        }

        if (ordered.Count != nodes.Count)
        {
            throw new WorkflowCompilationException(
                "图中存在环（无法拓扑排序），请检查连线方向。"
            );
        }

        return ordered;
    }
}
