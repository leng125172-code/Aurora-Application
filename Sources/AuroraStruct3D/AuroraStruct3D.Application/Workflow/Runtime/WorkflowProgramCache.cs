using System.Collections.Concurrent;
using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.OpenCV.Workflow.Values;
using AuroraStruct3D.OpenCV.Workflow.Compilation;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.OpenCV.Workflow.Scripting;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Volo.Abp.DependencyInjection;
using Microsoft.Extensions.Options;
using RuntimeWorkflowDefinition = AuroraStruct3D.OpenCV.Workflow.WorkflowDefinition;

namespace AuroraStruct3D.Workflow.Runtime;

/// <summary>
/// 已解析、校验、排序并编译的工作流程序。所有成员在构造后只读；
/// OperatorCallStatement 会在 Execute 时创建并释放算子实例，因此定义可跨执行复用。
/// </summary>
public sealed class WorkflowCompiledProgram
{
    public required string ProgramHash { get; init; }
    public required string Name { get; init; }
    public required GraphDataModel Graph { get; init; }
    public required RuntimeWorkflowDefinition Workflow { get; init; }
    public required WorkflowSignature Signature { get; init; }
    public required IReadOnlyList<string> StatementNodeIds { get; init; }
    public required IReadOnlyDictionary<string, WorkflowOutputAccessor> OutputAccessors { get; init; }
    public required IReadOnlyDictionary<string, string> OutputDisplayNames { get; init; }
}

public sealed class WorkflowOutputAccessor
{
    private readonly IReadOnlyList<object> _segments;
    public string Path { get; }
    public string RootVariableName { get; }

    private WorkflowOutputAccessor(string path, string rootVariableName, IReadOnlyList<object> segments)
    {
        Path = path;
        RootVariableName = rootVariableName;
        _segments = segments;
    }

    public static WorkflowOutputAccessor Compile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        int rootEnd = path.IndexOfAny(['.', '[']);
        string root = rootEnd < 0 ? path : path[..rootEnd];
        List<object> segments = new();
        int offset = root.Length;
        while (offset < path.Length)
        {
            if (path[offset] == '.')
            {
                int start = ++offset;
                while (offset < path.Length && path[offset] is not ('.' or '['))
                    offset++;
                if (start == offset)
                    throw new FormatException($"输出路径 '{path}' 包含空属性名。");
                segments.Add(path[start..offset]);
            }
            else if (path[offset] == '[')
            {
                int end = path.IndexOf(']', offset + 1);
                if (
                    end < 0
                    || !int.TryParse(path[(offset + 1)..end], out int index)
                    || index < 0
                )
                    throw new FormatException($"输出路径 '{path}' 的数组下标无效。");
                segments.Add(index);
                offset = end + 1;
            }
            else
                throw new FormatException($"输出路径 '{path}' 在位置 {offset} 无效。");
        }
        return new WorkflowOutputAccessor(path, root, segments);
    }

    public bool TryGetValue(object? rootValue, out object? value)
    {
        value = rootValue;
        if (_segments.Count == 0)
            return true;
        JsonNode? current;
        if (rootValue is string text)
        {
            try
            {
                current = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                return false;
            }
        }
        else if (rootValue is JsonNode node)
        {
            current = node;
        }
        else if (!WorkflowValueSerializer.TrySerializeToJsonNode(rootValue, out current))
        {
            return false;
        }
        foreach (object segment in _segments)
        {
            current = segment switch
            {
                string property when current is JsonObject obj => obj[property],
                int index when current is JsonArray array && index < array.Count => array[index],
                _ => null,
            };
            if (current is null)
            {
                value = null;
                return true;
            }
        }
        value = current;
        return true;
    }
}

public interface IWorkflowProgramCache
{
    Task<WorkflowCompiledProgram> GetOrAddAsync(
        string programHash,
        string sourceCode,
        CancellationToken cancellationToken = default
    );

    void Remove(string programHash);
    WorkflowProgramCacheMetrics GetMetrics();
}

public sealed record WorkflowProgramCacheMetrics(
    long Hits,
    long Misses,
    long Compilations,
    long CompileFailures,
    long TotalCompileMilliseconds,
    int EntryCount
);

public sealed class WorkflowProgramCacheOptions
{
    public int Capacity { get; set; } = 128;
    public TimeSpan SlidingExpiration { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan CompileTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// 进程内程序缓存。相同哈希的并发首次请求只编译一次；容量超限时移除最久未访问项。
/// </summary>
public sealed class WorkflowProgramCache : IWorkflowProgramCache, ISingletonDependency
{
    private readonly IOperatorRegistry _registry;
    private readonly WorkflowProgramCacheOptions _options;
    private readonly ConcurrentDictionary<string, CacheEntry> _entries =
        new(StringComparer.Ordinal);
    private long _hits;
    private long _misses;
    private long _compilations;
    private long _compileFailures;
    private long _totalCompileMilliseconds;

    public WorkflowProgramCache(IOperatorRegistry registry)
        : this(registry, Options.Create(new WorkflowProgramCacheOptions())) { }

    public WorkflowProgramCache(
        IOperatorRegistry registry,
        IOptions<WorkflowProgramCacheOptions> options
    )
    {
        _registry = registry;
        _options = options.Value;
    }

    public async Task<WorkflowCompiledProgram> GetOrAddAsync(
        string programHash,
        string sourceCode,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceCode);

        bool existed = _entries.TryGetValue(programHash, out CacheEntry? entry);
        entry ??= _entries.GetOrAdd(
            programHash,
            _ => new CacheEntry(
                new Lazy<Task<WorkflowCompiledProgram>>(
                    () => CompileWithTimeoutAsync(programHash, sourceCode),
                    LazyThreadSafetyMode.ExecutionAndPublication
                )
            )
        );
        if (existed) Interlocked.Increment(ref _hits);
        else Interlocked.Increment(ref _misses);
        entry.Touch();
        try
        {
            WorkflowCompiledProgram program = await entry.Program.Value.WaitAsync(cancellationToken);
            Trim();
            return program;
        }
        catch
        {
            _entries.TryRemove(programHash, out _);
            throw;
        }
    }

    public void Remove(string programHash)
    {
        if (!string.IsNullOrWhiteSpace(programHash))
            _entries.TryRemove(programHash, out _);
    }

    public WorkflowProgramCacheMetrics GetMetrics() => new(
        Interlocked.Read(ref _hits),
        Interlocked.Read(ref _misses),
        Interlocked.Read(ref _compilations),
        Interlocked.Read(ref _compileFailures),
        Interlocked.Read(ref _totalCompileMilliseconds),
        _entries.Count);

    private async Task<WorkflowCompiledProgram> CompileAsync(
        string programHash,
        string sourceCode,
        CancellationToken cancellationToken
    )
    {
        if (!Regex.IsMatch(sourceCode,
            "^\\s*Workflow\\s*\\(\\s*\"(?:\\\\.|[^\"])*\"\\s*,\\s*3\\s*\\)\\s*;",
            RegexOptions.Multiline))
            throw new WorkflowCompilationException(
                "V1/V2 工作流已禁止执行，请先迁移到 V3。");
        (string name, GraphDataModel graph) = CSharpWorkflowScript.Parse(sourceCode);
        RuntimeWorkflowDefinition workflow = await new WorkflowGraphCompiler(_registry)
            .CompileAsync(graph, name, cancellationToken);
        WorkflowSignature signature = WorkflowSignatureExtractor.Extract(graph);
        return new WorkflowCompiledProgram
        {
            ProgramHash = programHash,
            Name = name,
            Graph = graph,
            Workflow = workflow,
            Signature = signature,
            StatementNodeIds = WorkflowNodeScheduleBuilder.BuildExecutableNodeOrder(graph),
            OutputAccessors = signature.Outputs.ToDictionary(
                    x => x,
                    WorkflowOutputAccessor.Compile,
                    StringComparer.Ordinal
                ),
            OutputDisplayNames = BuildOutputDisplayNames(graph),
        };
    }

    private async Task<WorkflowCompiledProgram> CompileWithTimeoutAsync(
        string programHash,
        string sourceCode
    )
    {
        using CancellationTokenSource timeout = new(_options.CompileTimeout);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Interlocked.Increment(ref _compilations);
        try { return await CompileAsync(programHash, sourceCode, timeout.Token); }
        catch
        {
            Interlocked.Increment(ref _compileFailures);
            throw;
        }
        finally
        {
            Interlocked.Add(ref _totalCompileMilliseconds, stopwatch.ElapsedMilliseconds);
        }
    }

    private static IReadOnlyDictionary<string, string> BuildOutputDisplayNames(
        GraphDataModel graph
    )
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        Collect(graph, result);
        return result;

        static void Collect(GraphDataModel scope, IDictionary<string, string> target)
        {
            foreach (NodeModel node in scope.Nodes)
            {
                if (
                    node.Type == "end-node"
                    && node.Properties?.InputBindings is { } bindings
                )
                {
                    foreach ((string portName, string path) in bindings)
                        if (!string.IsNullOrWhiteSpace(path))
                            target[path] =
                                node.Properties.InputBindingDisplayNames?.GetValueOrDefault(portName)
                                    ?.Trim()
                                is { Length: > 0 } displayName
                                    ? displayName
                                    : path;
                }
                if (node.Properties?.InnerGraphData is { } inner)
                    Collect(inner, target);
            }
        }
    }

    private void Trim()
    {
        long expiredBefore = DateTime.UtcNow.Subtract(_options.SlidingExpiration).Ticks;
        foreach (string key in _entries.Where(x => x.Value.LastAccessTicks < expiredBefore).Select(x => x.Key).ToList())
            _entries.TryRemove(key, out _);
        int removeCount = _entries.Count - Math.Max(1, _options.Capacity);
        if (removeCount <= 0)
            return;
        foreach (
            string key in _entries
                .OrderBy(x => x.Value.LastAccessTicks)
                .Take(removeCount)
                .Select(x => x.Key)
        )
            _entries.TryRemove(key, out _);
    }

    private sealed class CacheEntry
    {
        private long _lastAccessTicks = DateTime.UtcNow.Ticks;
        public Lazy<Task<WorkflowCompiledProgram>> Program { get; }
        public long LastAccessTicks => Interlocked.Read(ref _lastAccessTicks);

        public CacheEntry(Lazy<Task<WorkflowCompiledProgram>> program)
        {
            Program = program;
        }

        public void Touch() => Interlocked.Exchange(ref _lastAccessTicks, DateTime.UtcNow.Ticks);
    }
}
