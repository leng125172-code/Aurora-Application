using System.Text.RegularExpressions;
using AuroraStruct3D.Variables.Dtos;

namespace AuroraStruct3D.Variables;

/// <summary>
/// Offline Def-Use analyzer for potential uninitialized-read diagnostics.
/// </summary>
public static class OfflineVariableDefUseAnalyzer
{
    private static readonly Regex SequenceRegex = new("\\d+", RegexOptions.Compiled);

    /// <summary>
    /// Analyze local required-init variables for potential read-before-init risks.
    /// </summary>
    /// <param name="input">Compile input.</param>
    /// <param name="declarationMap">Local declaration map by variable name.</param>
    /// <returns>Generated diagnostics for potential uninitialized reads.</returns>
    public static List<VariableDiagnosticDto> AnalyzePotentialUninitializedReads(
        VariableCompileRequestDto input,
        Dictionary<string, VariableDeclarationDto> declarationMap
    )
    {
        List<VariableDiagnosticDto> diagnostics = new();

        if (input.DefUseAnalysisMode == VariableDefUseAnalysisMode.Disabled)
        {
            return diagnostics;
        }
        HashSet<string> trackedVariables = declarationMap
            .Where(x =>
                x.Value.IsRequiredInit && string.IsNullOrWhiteSpace(x.Value.DefaultValueJson)
            )
            .Select(x => x.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (trackedVariables.Count == 0)
        {
            return diagnostics;
        }

        ControlFlowGraph graph = BuildControlFlowGraph(input);
        Dictionary<string, HashSet<string>> inFacts = ComputeDefiniteInitializationFacts(
            graph,
            trackedVariables
        );

        foreach (GraphNode node in graph.Nodes.Values)
        {
            if (!inFacts.TryGetValue(node.NodeId, out HashSet<string>? inState))
            {
                continue;
            }

            HashSet<string> state = new(inState, StringComparer.Ordinal);
            List<OperationRef> operations = node
                .Operations.OrderBy(x => x.Sequence ?? long.MaxValue)
                .ThenBy(x => x.Order)
                .ToList();

            foreach (OperationRef operation in operations)
            {
                if (!trackedVariables.Contains(operation.VariableName))
                {
                    continue;
                }

                if (operation.Kind == OperationKind.Write)
                {
                    state.Add(operation.VariableName);
                    continue;
                }

                if (state.Contains(operation.VariableName))
                {
                    continue;
                }

                diagnostics.Add(
                    CreateDiagnostic(
                        AuroraStruct3DDomainErrorCodes.VariableUninitializedRead,
                        $"变量 '{operation.VariableName}' 在位置 '{operation.Location}' 可能发生未初始化读取。",
                        input.WorkflowId,
                        operation.VariableName,
                        operation.Location
                    )
                );
            }
        }

        if (input.DefUseAnalysisMode == VariableDefUseAnalysisMode.Strict)
        {
            EmitStrictUnresolvedOrderDiagnostics(input, trackedVariables, diagnostics);
        }

        return diagnostics;
    }

    private static void EmitStrictUnresolvedOrderDiagnostics(
        VariableCompileRequestDto input,
        HashSet<string> trackedVariables,
        List<VariableDiagnosticDto> diagnostics
    )
    {
        List<VariableReferenceDto> localReads = input
            .Reads.Where(x => x.OwnerWorkflowId == input.WorkflowId)
            .ToList();
        List<VariableReferenceDto> localWrites = input
            .Writes.Where(x => x.OwnerWorkflowId == input.WorkflowId)
            .ToList();

        foreach (string variableName in trackedVariables)
        {
            int unresolvedWrites = localWrites
                .Where(x => x.VariableName == variableName)
                .Count(x => !TryResolveSequence(x).HasValue);
            if (unresolvedWrites > 0)
            {
                diagnostics.Add(
                    CreateDiagnostic(
                        AuroraStruct3DDomainErrorCodes.VariableUninitializedRead,
                        $"变量 '{variableName}' 存在无法排序的写入位置，严格模式下判定为潜在未初始化读取风险。",
                        input.WorkflowId,
                        variableName,
                        null
                    )
                );
            }

            foreach (
                VariableReferenceDto unresolvedRead in localReads.Where(x =>
                    x.VariableName == variableName && !TryResolveSequence(x).HasValue
                )
            )
            {
                diagnostics.Add(
                    CreateDiagnostic(
                        AuroraStruct3DDomainErrorCodes.VariableUninitializedRead,
                        $"变量 '{variableName}' 在位置 '{unresolvedRead.Location}' 无法确定读取顺序，严格模式下判定为潜在未初始化读取。",
                        input.WorkflowId,
                        variableName,
                        unresolvedRead.Location
                    )
                );
            }
        }
    }

    private static Dictionary<string, HashSet<string>> ComputeDefiniteInitializationFacts(
        ControlFlowGraph graph,
        HashSet<string> trackedVariables
    )
    {
        Dictionary<string, HashSet<string>> inFacts = new(StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> outFacts = new(StringComparer.Ordinal);

        foreach (GraphNode node in graph.Nodes.Values)
        {
            if (node.NodeId == graph.EntryNodeId)
            {
                inFacts[node.NodeId] = new HashSet<string>(StringComparer.Ordinal);
                outFacts[node.NodeId] = ApplyTransfer(node, inFacts[node.NodeId]);
                continue;
            }

            inFacts[node.NodeId] = new HashSet<string>(trackedVariables, StringComparer.Ordinal);
            outFacts[node.NodeId] = new HashSet<string>(trackedVariables, StringComparer.Ordinal);
        }

        Queue<string> worklist = new(graph.Nodes.Keys);
        HashSet<string> queued = new(graph.Nodes.Keys, StringComparer.Ordinal);

        while (worklist.Count > 0)
        {
            string nodeId = worklist.Dequeue();
            queued.Remove(nodeId);

            GraphNode node = graph.Nodes[nodeId];
            if (node.NodeId == graph.EntryNodeId)
            {
                continue;
            }

            HashSet<string> mergedInput = MergePredecessors(
                graph,
                outFacts,
                node.NodeId,
                trackedVariables
            );

            if (!SetEquals(inFacts[node.NodeId], mergedInput))
            {
                inFacts[node.NodeId] = mergedInput;
            }

            HashSet<string> newOut = ApplyTransfer(node, inFacts[node.NodeId]);
            if (SetEquals(outFacts[node.NodeId], newOut))
            {
                continue;
            }

            outFacts[node.NodeId] = newOut;
            foreach (string successor in node.Successors)
            {
                if (queued.Contains(successor))
                {
                    continue;
                }

                worklist.Enqueue(successor);
                queued.Add(successor);
            }
        }

        return inFacts;
    }

    private static HashSet<string> MergePredecessors(
        ControlFlowGraph graph,
        Dictionary<string, HashSet<string>> outFacts,
        string nodeId,
        HashSet<string> trackedVariables
    )
    {
        List<string> predecessors = graph.Predecessors.TryGetValue(nodeId, out List<string>? values)
            ? values
            : [];

        if (predecessors.Count == 0)
        {
            return new HashSet<string>(trackedVariables, StringComparer.Ordinal);
        }

        HashSet<string> result = new(outFacts[predecessors[0]], StringComparer.Ordinal);
        for (int i = 1; i < predecessors.Count; i++)
        {
            result.IntersectWith(outFacts[predecessors[i]]);
        }

        return result;
    }

    private static HashSet<string> ApplyTransfer(GraphNode node, HashSet<string> input)
    {
        HashSet<string> result = new(input, StringComparer.Ordinal);

        foreach (OperationRef operation in node.Operations)
        {
            if (operation.Kind != OperationKind.Write)
            {
                continue;
            }

            result.Add(operation.VariableName);
        }

        return result;
    }

    private static bool SetEquals(HashSet<string> left, HashSet<string> right)
    {
        return left.SetEquals(right);
    }

    private static ControlFlowGraph BuildControlFlowGraph(VariableCompileRequestDto input)
    {
        Dictionary<string, GraphNode> nodes = new(StringComparer.Ordinal);
        int opOrder = 0;

        foreach (
            VariableReferenceDto read in input.Reads.Where(x =>
                x.OwnerWorkflowId == input.WorkflowId
            )
        )
        {
            AddOperation(nodes, OperationKind.Read, read, opOrder++);
        }

        foreach (
            VariableReferenceDto write in input.Writes.Where(x =>
                x.OwnerWorkflowId == input.WorkflowId
            )
        )
        {
            AddOperation(nodes, OperationKind.Write, write, opOrder++);
        }

        foreach (GraphNode node in nodes.Values)
        {
            node.Operations.Sort(
                (a, b) =>
                {
                    int seqCompare = Nullable.Compare(a.Sequence, b.Sequence);
                    if (seqCompare != 0)
                    {
                        return seqCompare;
                    }

                    return a.Order.CompareTo(b.Order);
                }
            );
        }

        string entryNodeId = "__ENTRY__";
        nodes.TryAdd(entryNodeId, new GraphNode(entryNodeId));

        List<(string From, string To)> edges = input
            .ControlFlowEdges.Where(x =>
                !string.IsNullOrWhiteSpace(x.FromNodeId) && !string.IsNullOrWhiteSpace(x.ToNodeId)
            )
            .Select(x => (x.FromNodeId.Trim(), x.ToNodeId.Trim()))
            .ToList();

        if (edges.Count == 0)
        {
            List<string> linearNodeIds = nodes
                .Values.Where(x => x.NodeId != entryNodeId)
                .OrderBy(x =>
                    x.Operations.Select(op => op.Sequence)
                        .Where(seq => seq.HasValue)
                        .Select(seq => seq!.Value)
                        .DefaultIfEmpty(long.MaxValue)
                        .Min()
                )
                .ThenBy(x => x.NodeId, StringComparer.Ordinal)
                .Select(x => x.NodeId)
                .ToList();

            for (int i = 0; i < linearNodeIds.Count - 1; i++)
            {
                edges.Add((linearNodeIds[i], linearNodeIds[i + 1]));
            }

            if (linearNodeIds.Count > 0)
            {
                edges.Add((entryNodeId, linearNodeIds[0]));
            }
        }
        else
        {
            foreach ((string from, string to) in edges)
            {
                nodes.TryAdd(from, new GraphNode(from));
                nodes.TryAdd(to, new GraphNode(to));
            }

            HashSet<string> targets = edges.Select(x => x.To).ToHashSet(StringComparer.Ordinal);
            List<string> entryCandidates = nodes
                .Keys.Where(x => x != entryNodeId && !targets.Contains(x))
                .ToList();

            if (
                !string.IsNullOrWhiteSpace(input.EntryNodeId)
                && nodes.ContainsKey(input.EntryNodeId)
            )
            {
                edges.Add((entryNodeId, input.EntryNodeId));
            }
            else
            {
                foreach (string candidate in entryCandidates)
                {
                    edges.Add((entryNodeId, candidate));
                }
            }
        }

        Dictionary<string, List<string>> predecessors = new(StringComparer.Ordinal);
        foreach (GraphNode node in nodes.Values)
        {
            predecessors[node.NodeId] = [];
        }

        foreach ((string from, string to) in edges)
        {
            if (!nodes.ContainsKey(from) || !nodes.ContainsKey(to))
            {
                continue;
            }

            if (!nodes[from].Successors.Contains(to))
            {
                nodes[from].Successors.Add(to);
                predecessors[to].Add(from);
            }
        }

        return new ControlFlowGraph(nodes, predecessors, entryNodeId);
    }

    private static void AddOperation(
        Dictionary<string, GraphNode> nodes,
        OperationKind kind,
        VariableReferenceDto reference,
        int order
    )
    {
        string nodeId = ResolveNodeId(reference, order);
        if (!nodes.TryGetValue(nodeId, out GraphNode? node))
        {
            node = new GraphNode(nodeId);
            nodes[nodeId] = node;
        }

        node.Operations.Add(
            new OperationRef(
                kind,
                reference.VariableName,
                reference.Location,
                TryResolveSequence(reference),
                order
            )
        );
    }

    private static string ResolveNodeId(VariableReferenceDto reference, int fallbackOrder)
    {
        if (!string.IsNullOrWhiteSpace(reference.Location))
        {
            return reference.Location.Trim();
        }

        if (reference.Sequence.HasValue)
        {
            return $"seq:{reference.Sequence.Value}";
        }

        return $"op:{fallbackOrder}";
    }

    private static long? TryExtractSequence(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        MatchCollection matches = SequenceRegex.Matches(location);
        if (matches.Count == 0)
        {
            return null;
        }

        if (!long.TryParse(matches[0].Value, out long sequence))
        {
            return null;
        }

        return sequence;
    }

    private static long? TryResolveSequence(VariableReferenceDto reference)
    {
        if (reference.Sequence.HasValue)
        {
            return reference.Sequence.Value;
        }

        return TryExtractSequence(reference.Location);
    }

    private static VariableDiagnosticDto CreateDiagnostic(
        string code,
        string message,
        Guid? ownerWorkflowId,
        string? variableName,
        string? location
    )
    {
        return new VariableDiagnosticDto
        {
            Severity = VariableDiagnosticSeverity.Error,
            Code = code,
            Message = message,
            OwnerWorkflowId = ownerWorkflowId,
            VariableName = variableName,
            Location = location,
        };
    }

    private sealed class ControlFlowGraph
    {
        public ControlFlowGraph(
            Dictionary<string, GraphNode> nodes,
            Dictionary<string, List<string>> predecessors,
            string entryNodeId
        )
        {
            Nodes = nodes;
            Predecessors = predecessors;
            EntryNodeId = entryNodeId;
        }

        public Dictionary<string, GraphNode> Nodes { get; }

        public Dictionary<string, List<string>> Predecessors { get; }

        public string EntryNodeId { get; }
    }

    private sealed class GraphNode
    {
        public GraphNode(string nodeId)
        {
            NodeId = nodeId;
        }

        public string NodeId { get; }

        public List<OperationRef> Operations { get; } = [];

        public List<string> Successors { get; } = [];
    }

    private sealed record OperationRef(
        OperationKind Kind,
        string VariableName,
        string? Location,
        long? Sequence,
        int Order
    );

    private enum OperationKind
    {
        Read = 0,
        Write = 1,
    }
}
