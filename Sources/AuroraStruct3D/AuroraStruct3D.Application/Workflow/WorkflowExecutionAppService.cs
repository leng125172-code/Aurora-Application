using System.Diagnostics;
using System.Text.Json;
using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.OpenCV.Workflow.Compilation;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.OpenCV.Workflow.Values;
using AuroraStruct3D.Workflow.Dtos;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using RuntimeWorkflow = AuroraStruct3D.OpenCV.Workflow.WorkflowDefinition;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 工作流执行应用服务实现。
/// 串联：加载持久化定义 → 编译为可执行语句 → 从 Redis 暂存注入初始变量 → 执行 →
/// 写回指定结果变量 → 返回变量摘要。编译 / 执行异常转为 <see cref="UserFriendlyException"/>。
/// </summary>
public class WorkflowExecutionAppService
    : AuroraStruct3DAppService,
        IWorkflowExecutionAppService
{
    // 注意：此处 WorkflowDefinition 为 Domain 持久化实体（当前命名空间），
    // 运行时模型用别名 RuntimeWorkflow 区分。
    private readonly IRepository<WorkflowDefinition, Guid> _repository;
    private readonly IOperatorRegistry _registry;
    private readonly IWorkflowVariableBridge _bridge;

    public WorkflowExecutionAppService(
        IRepository<WorkflowDefinition, Guid> repository,
        IOperatorRegistry registry,
        IWorkflowVariableBridge bridge
    )
    {
        _repository = repository;
        _registry = registry;
        _bridge = bridge;
    }

    /// <inheritdoc/>
    public async Task<WorkflowRunResultDto> RunAsync(RunWorkflowInput input)
    {
        // ① 加载定义 + 归属校验。
        WorkflowDefinition entity = await _repository.GetAsync(input.WorkflowId);
        if (entity.ProjectId != input.ProjectId)
            throw new UserFriendlyException("工作流不存在或不属于该项目。");

        // ② 解析（一次）→ 编译为可执行语句。
        RuntimeWorkflow compiled;
        GraphDataModel graph;
        try
        {
            string name;
            (name, graph) = WorkflowGraphCompiler.ParseContent(entity.Content);
            compiled = await new WorkflowGraphCompiler(_registry).CompileAsync(graph, name);
        }
        catch (WorkflowCompilationException ex)
        {
            throw new UserFriendlyException("工作流编译失败：" + ex.Message);
        }
        catch (JsonException ex)
        {
            throw new UserFriendlyException("工作流内容 JSON 解析失败：" + ex.Message);
        }

        // ③ I/O 签名：显式指定优先，否则从 start/end 节点绑定自动推导。
        WorkflowSignature signature = WorkflowSignatureExtractor.Extract(graph);
        IReadOnlyList<string> inputKeys = input.InputVariableKeys is { Count: > 0 } explicitIn
            ? explicitIn
            : signature.Inputs;
        IReadOnlyList<string> outputVarNames = input.OutputVariableNames is { Count: > 0 } explicitOut
            ? explicitOut
            : signature.Outputs;

        // ④ 从 Redis 暂存加载初始变量。
        Dictionary<string, object?> initialVariables = await _bridge.LoadAsync(inputKeys);

        // ④ 执行。
        var stopwatch = Stopwatch.StartNew();
        WorkflowContext context;
        try
        {
            context = new WorkflowExecutor().Execute(compiled, initialVariables);
        }
        catch (WorkflowExecutionException ex)
        {
            throw new UserFriendlyException("工作流执行失败：" + ex.Message);
        }
        stopwatch.Stop();

        try
        {
            // ⑤ 写回结果变量 + 汇总（序列化在 context.Dispose 之前完成）。
            var outputNames = new HashSet<string>(outputVarNames, StringComparer.Ordinal);

            var variables = new List<WorkflowVariableResultDto>();
            foreach (string name in context.VariableNames)
            {
                object? value = context.Get(name);
                string valueType = WorkflowValueSerializer.InferValueType(value);

                string? scalar = WorkflowValueTypes.IsScalar(valueType)
                    ? WorkflowValueSerializer.ScalarToString(value)
                    : null;

                string? stagedKey = outputNames.Contains(name)
                    ? await _bridge.SaveAsync(name, value)
                    : null;

                variables.Add(
                    new WorkflowVariableResultDto
                    {
                        Name = name,
                        ValueType = valueType,
                        ScalarValue = scalar,
                        StagedKey = stagedKey,
                    }
                );
            }

            return new WorkflowRunResultDto
            {
                WorkflowId = input.WorkflowId,
                Name = compiled.Name,
                VariableCount = variables.Count,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Variables = variables,
            };
        }
        finally
        {
            context.Dispose();
        }
    }
}
