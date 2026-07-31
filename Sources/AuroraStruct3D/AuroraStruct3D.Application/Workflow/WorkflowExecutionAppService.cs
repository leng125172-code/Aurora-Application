using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuroraStruct3D.OpenCV.File.PointCloud;
using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.OpenCV.Workflow.Compilation;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.OpenCV.Workflow.Values;
using AuroraStruct3D.OpenCV.Workflow.Scripting;
using AuroraStruct3D.OperatorFile;
using AuroraStruct3D.ProductModels;
using AuroraStruct3D.Variables;
using AuroraStruct3D.Variables.Dtos;
using AuroraStruct3D.Workflow.Dtos;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;
using RuntimeWorkflow = AuroraStruct3D.OpenCV.Workflow.WorkflowDefinition;
using AuroraStruct3D.Workflow.Runtime;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 工作流执行应用服务实现。
/// 串联：加载持久化定义 → 编译为可执行语句 → 从 Redis 暂存注入初始变量 → 执行 →
/// 写回指定结果变量 → 返回变量摘要。编译 / 执行异常转为 <see cref="UserFriendlyException"/>。
/// </summary>
[Authorize]
public class WorkflowExecutionAppService : AuroraStruct3DAppService, IWorkflowExecutionAppService
{
    // 注意：此处 WorkflowDefinition 为 Domain 持久化实体（当前命名空间），
    // 运行时模型用别名 RuntimeWorkflow 区分。
    private readonly IRepository<WorkflowDefinition, Guid> _repository;
    private readonly IOperatorRegistry _registry;
    private readonly IWorkflowProgramCache _programCache;
    private readonly IWorkflowVariableBridge _bridge;
    private readonly IOnlineVariablePoolAppService _onlineVariablePool;
    private readonly IBlobContainer<OperatorFileBlobContainer> _operatorFileBlobContainer;
    private readonly IRepository<ProductModel, Guid> _productModelRepository;
    private readonly IBlobContainer<ProductModelBlobContainer> _productModelBlobContainer;

    public WorkflowExecutionAppService(
        IRepository<WorkflowDefinition, Guid> repository,
        IOperatorRegistry registry,
        IWorkflowProgramCache programCache,
        IWorkflowVariableBridge bridge,
        IOnlineVariablePoolAppService onlineVariablePool,
        IBlobContainer<OperatorFileBlobContainer> operatorFileBlobContainer,
        IRepository<ProductModel, Guid> productModelRepository,
        IBlobContainer<ProductModelBlobContainer> productModelBlobContainer
    )
    {
        _repository = repository;
        _registry = registry;
        _programCache = programCache;
        _bridge = bridge;
        _onlineVariablePool = onlineVariablePool;
        _operatorFileBlobContainer = operatorFileBlobContainer;
        _productModelRepository = productModelRepository;
        _productModelBlobContainer = productModelBlobContainer;
    }

    /// <inheritdoc/>
    public async Task<WorkflowRunResultDto> RunAsync(RunWorkflowInput input)
    {
        // ① 加载定义 + 归属校验。
        WorkflowDefinition entity = await _repository.GetAsync(input.WorkflowId);
        if (entity.ProjectId != input.ProjectId)
            throw new UserFriendlyException("工作流不存在或不属于该项目。");

        // ② 从脚本程序缓存加载。旧数据只在首次迁移时解析一次 GraphData。
        RuntimeWorkflow compiled;
        GraphDataModel graph;
        try
        {
            string sourceCode;
            string programHash;
            if (string.IsNullOrWhiteSpace(entity.SourceCode)
                || string.IsNullOrWhiteSpace(entity.ProgramHash))
                throw new UserFriendlyException(
                    "工作流尚未迁移为脚本程序，正式执行已禁止从 GraphData 临时编译；请先执行工作流迁移并重新保存。"
                );
            sourceCode = entity.SourceCode;
            programHash = entity.ProgramHash;

            WorkflowCompiledProgram program = await _programCache.GetOrAddAsync(
                programHash,
                sourceCode
            );
            graph = program.Graph;
            compiled = program.Workflow;
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
        IReadOnlyList<string> outputVarNames = input.OutputVariableNames
            is { Count: > 0 } explicitOut
            ? explicitOut
            : signature.Outputs;

        List<WorkflowVariableBindingKeyDto> inputBindings = BuildBindingKeys(
            input.InputVariableBindings,
            inputKeys,
            input.WorkflowId
        );
        List<WorkflowVariableBindingKeyDto> outputBindings = BuildBindingKeys(
            input.OutputVariableBindings,
            outputVarNames,
            input.WorkflowId
        );

        ValidateBindingKeys(inputBindings, "input");
        ValidateBindingKeys(outputBindings, "output", uniqueByVariableName: true);

        const bool useOnlineVariablePool = true;
        Guid? runtimeInstanceId = input.RuntimeInstanceId ?? GuidGenerator.Create();

        // ④ 加载初始变量（在线变量池 / Redis 暂存二选一）。
        Dictionary<string, object?> initialVariables = await LoadFromOnlineVariablePoolAsync(
            input,
            runtimeInstanceId!.Value,
            inputBindings
        );

        // ④ 执行。
        var stopwatch = Stopwatch.StartNew();
        WorkflowContext context;
        try
        {
            using IDisposable blobStoreScope = CreateOperatorFileBlobStoreScope();
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
            Dictionary<string, WorkflowVariableBindingKeyDto> outputBindingMap = outputBindings
                .GroupBy(x => x.VariableName, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

            var variables = new List<WorkflowVariableResultDto>();
            List<RegionMeasurementResultDto>? regions = null;

            foreach (string name in context.VariableNames)
            {
                object? value = context.Get(name);
                string valueType = WorkflowValueSerializer.InferValueType(value);

                string? scalar = WorkflowValueTypes.IsScalar(valueType)
                    ? WorkflowValueSerializer.ScalarToString(value)
                    : null;

                if (name == "result_json" && scalar != null)
                {
                    try
                    {
                        var result = JsonSerializer.Deserialize<HeightDiffResultDto>(scalar);
                        regions = result?.Regions;
                    }
                    catch (JsonException) { }
                }

                string? stagedKey = null;
                if (outputNames.Contains(name))
                {
                    stagedKey = useOnlineVariablePool
                        ? await SaveToOnlineVariablePoolAsync(
                            input,
                            runtimeInstanceId!.Value,
                            ResolveOwnerWorkflowId(name, outputBindingMap, input.WorkflowId),
                            name,
                            value,
                            valueType
                        )
                        : await _bridge.SaveAsync(name, value);
                }

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
                Name = ResolveResponseWorkflowName(entity.Name, compiled.Name),
                VariableCount = variables.Count,
                DurationMs = stopwatch.ElapsedMilliseconds,
                RuntimeInstanceId = runtimeInstanceId,
                Variables = variables,
                Regions = regions,
            };
        }
        finally
        {
            context.Dispose();
        }
    }

    private static string ComputeProgramHash(string source) =>
        WorkflowProgramHash.ComputeProgramHash(source);

    /// <summary>
    /// 从在线变量池读取工作流初始变量并反序列化为执行上下文字典。
    /// </summary>
    private async Task<Dictionary<string, object?>> LoadFromOnlineVariablePoolAsync(
        RunWorkflowInput input,
        Guid runtimeInstanceId,
        IReadOnlyList<WorkflowVariableBindingKeyDto> inputBindings
    )
    {
        await _onlineVariablePool.InitializeAsync(
            new InitializeVariablePoolInput
            {
                ProjectId = input.ProjectId,
                InstanceId = runtimeInstanceId,
                SnapshotVersion = 0,
            }
        );

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (
            WorkflowVariableBindingKeyDto binding in inputBindings.DistinctBy(x =>
                (x.OwnerWorkflowId, x.VariableName)
            )
        )
        {
            ReadVariableResultDto read = await _onlineVariablePool.ReadAsync(
                new ReadVariableInput
                {
                    ProjectId = input.ProjectId,
                    InstanceId = runtimeInstanceId,
                    ReaderWorkflowId = input.WorkflowId,
                    OwnerWorkflowId = binding.OwnerWorkflowId,
                    VariableName = binding.VariableName,
                    WaitPolicy = VariableWaitPolicy.WaitOrDefault,
                    TimeoutMs = Math.Max(input.VariableReadTimeoutMs, 0),
                }
            );

            if (string.IsNullOrWhiteSpace(read.ValueJson))
            {
                result[binding.VariableName] = null;
                continue;
            }

            byte[] bytes = Convert.FromBase64String(read.ValueJson);
            result[binding.VariableName] = WorkflowValueSerializer.Deserialize(
                bytes,
                read.TypeName
            );
        }

        return result;
    }

    /// <summary>
    /// 将执行结果写回在线变量池。
    /// </summary>
    private async Task<string> SaveToOnlineVariablePoolAsync(
        RunWorkflowInput input,
        Guid runtimeInstanceId,
        Guid ownerWorkflowId,
        string variableName,
        object? value,
        string valueType
    )
    {
        byte[] bytes = WorkflowValueSerializer.Serialize(value, valueType);
        string valueJson = Convert.ToBase64String(bytes);

        await _onlineVariablePool.WriteAsync(
            new WriteVariableInput
            {
                ProjectId = input.ProjectId,
                InstanceId = runtimeInstanceId,
                WriterWorkflowId = input.WorkflowId,
                OwnerWorkflowId = ownerWorkflowId,
                VariableName = variableName,
                TypeName = valueType,
                ValueJson = valueJson,
            }
        );

        return $"pool:{runtimeInstanceId:N}:{ownerWorkflowId:N}:{variableName}";
    }

    private static List<WorkflowVariableBindingKeyDto> BuildBindingKeys(
        List<WorkflowVariableBindingKeyDto>? explicitBindings,
        IReadOnlyList<string> fallbackVariableNames,
        Guid fallbackOwnerWorkflowId
    )
    {
        if (explicitBindings is { Count: > 0 })
        {
            return explicitBindings
                .Where(x =>
                    x.OwnerWorkflowId != Guid.Empty && !string.IsNullOrWhiteSpace(x.VariableName)
                )
                .ToList();
        }

        return fallbackVariableNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => new WorkflowVariableBindingKeyDto
            {
                OwnerWorkflowId = fallbackOwnerWorkflowId,
                VariableName = x,
            })
            .ToList();
    }

    private static void ValidateBindingKeys(
        List<WorkflowVariableBindingKeyDto> bindings,
        string direction,
        bool uniqueByVariableName = false
    )
    {
        if (bindings.Count == 0)
        {
            return;
        }

        IEnumerable<WorkflowVariableBindingKeyDto> invalid = bindings.Where(x =>
            x.OwnerWorkflowId == Guid.Empty || string.IsNullOrWhiteSpace(x.VariableName)
        );
        if (invalid.Any())
        {
            throw new UserFriendlyException(
                $"变量绑定无效：{direction} 绑定存在空 OwnerWorkflowId 或 VariableName。"
            );
        }

        bool hasDuplicate = uniqueByVariableName
            ? bindings.GroupBy(x => x.VariableName, StringComparer.Ordinal).Any(x => x.Count() > 1)
            : bindings.GroupBy(x => (x.OwnerWorkflowId, x.VariableName)).Any(x => x.Count() > 1);

        if (hasDuplicate)
        {
            string rule = uniqueByVariableName ? "变量名唯一" : "归属+变量名唯一";
            throw new UserFriendlyException($"变量绑定重复：{direction} 绑定需满足 {rule}。 ");
        }
    }

    private static Guid ResolveOwnerWorkflowId(
        string variableName,
        Dictionary<string, WorkflowVariableBindingKeyDto> outputBindingMap,
        Guid fallbackOwnerWorkflowId
    )
    {
        if (outputBindingMap.TryGetValue(variableName, out WorkflowVariableBindingKeyDto? binding))
        {
            return binding.OwnerWorkflowId;
        }

        return fallbackOwnerWorkflowId;
    }

    private static string ResolveResponseWorkflowName(string persistedWorkflowName, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(persistedWorkflowName))
        {
            return persistedWorkflowName;
        }

        return fallback;
    }

    private IDisposable CreateOperatorFileBlobStoreScope()
    {
        return new CombinedScope(
            OperatorFileBlobStoreAmbient.Push(
                new WorkflowOperatorFileBlobStore(_operatorFileBlobContainer)
            ),
            ProductModelStoreAmbient.Push(
                new WorkflowProductModelPointCloudStore(
                    _productModelRepository,
                    _productModelBlobContainer
                )
            )
        );
    }

    private sealed class CombinedScope(params IDisposable[] scopes) : IDisposable
    {
        public void Dispose()
        {
            for (int i = scopes.Length - 1; i >= 0; i--)
                scopes[i].Dispose();
        }
    }

    private class HeightDiffResultDto
    {
        public List<RegionMeasurementResultDto>? Regions { get; set; }
    }
}
