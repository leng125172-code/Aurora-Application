using AuroraStruct3D.Variables.Dtos;
using Volo.Abp;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Variables;

/// <summary>
/// 在线变量池应用服务实现。
/// </summary>
public class OnlineVariablePoolAppService : AuroraStruct3DAppService, IOnlineVariablePoolAppService
{
    private static readonly TimeSpan DefaultLockAcquireTimeout = TimeSpan.FromSeconds(5);

    private readonly IRepository<VariableDefinition, Guid> _variableDefinitionRepository;
    private readonly IRepository<VariableInstanceValue, Guid> _variableInstanceRepository;
    private readonly IAbpDistributedLock _distributedLock;

    public OnlineVariablePoolAppService(
        IRepository<VariableDefinition, Guid> variableDefinitionRepository,
        IRepository<VariableInstanceValue, Guid> variableInstanceRepository,
        IAbpDistributedLock distributedLock
    )
    {
        _variableDefinitionRepository = variableDefinitionRepository;
        _variableInstanceRepository = variableInstanceRepository;
        _distributedLock = distributedLock;
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(InitializeVariablePoolInput input)
    {
        ValidateInitializeInput(input);

        IQueryable<VariableDefinition> definitionQuery =
            await _variableDefinitionRepository.GetQueryableAsync();
        IQueryable<VariableInstanceValue> instanceQuery =
            await _variableInstanceRepository.GetQueryableAsync();

        List<VariableDefinition> definitions = await AsyncExecuter.ToListAsync(
            definitionQuery.Where(x =>
                x.ProjectId == input.ProjectId
                && (input.SnapshotVersion <= 0 || x.SnapshotVersion <= input.SnapshotVersion)
            )
        );

        List<VariableInstanceValue> existing = await AsyncExecuter.ToListAsync(
            instanceQuery.Where(x =>
                x.ProjectId == input.ProjectId && x.InstanceId == input.InstanceId
            )
        );

        foreach (VariableDefinition definition in definitions)
        {
            bool existed = existing.Any(x =>
                x.OwnerWorkflowId == definition.OwnerWorkflowId && x.VariableName == definition.Name
            );
            if (existed)
            {
                continue;
            }

            VariableValueState state = string.IsNullOrWhiteSpace(definition.DefaultValueJson)
                ? VariableValueState.Uninitialized
                : VariableValueState.Ready;

            VariableInstanceValue created = VariableInstanceValue.Create(
                GuidGenerator.Create(),
                input.ProjectId,
                input.InstanceId,
                definition.Id,
                definition.OwnerWorkflowId,
                definition.Name,
                definition.TypeName,
                state,
                definition.DefaultValueJson,
                valueVersion: state == VariableValueState.Ready ? 1 : 0
            );

            await _variableInstanceRepository.InsertAsync(created, autoSave: true);
            existing.Add(created);
        }

        foreach (VariableRuntimeSeedDto seed in input.Seeds)
        {
            WriteVariableInput writeInput = new()
            {
                ProjectId = input.ProjectId,
                InstanceId = input.InstanceId,
                WriterWorkflowId = seed.OwnerWorkflowId,
                OwnerWorkflowId = seed.OwnerWorkflowId,
                VariableName = seed.VariableName,
                TypeName = seed.TypeName,
                ValueJson = seed.ValueJson,
            };

            await WriteAsync(writeInput);
        }

        // 必填初始化变量做快速检查：要求执行前赋值的变量仍为未初始化时给出阻断错误。
        IQueryable<VariableInstanceValue> postInitQuery =
            await _variableInstanceRepository.GetQueryableAsync();
        List<VariableInstanceValue> uninitializedRequired = await AsyncExecuter.ToListAsync(
            from value in postInitQuery
            join definition in definitionQuery on value.VariableDefinitionId equals definition.Id
            where
                value.ProjectId == input.ProjectId
                && value.InstanceId == input.InstanceId
                && definition.IsRequiredInit
                && value.State != VariableValueState.Ready
            select value
        );

        if (uninitializedRequired.Count > 0)
        {
            string names = string.Join(
                ", ",
                uninitializedRequired.Select(x => $"{x.OwnerWorkflowId}/{x.VariableName}")
            );
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableUninitializedRead}: 存在未初始化的必填变量: {names}。"
            );
        }
    }

    /// <inheritdoc/>
    public async Task<ReadVariableResultDto> ReadAsync(ReadVariableInput input)
    {
        ValidateReadInput(input);

        VariableDefinition definition = await GetDefinitionAsync(
            input.ProjectId,
            input.OwnerWorkflowId,
            input.VariableName
        );

        EnsureReadPermission(input.ReaderWorkflowId, definition);

        int timeoutMs = Math.Max(input.TimeoutMs, 0);
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (true)
        {
            VariableInstanceValue value = await GetOrCreateInstanceValueAsync(
                input.ProjectId,
                input.InstanceId,
                definition
            );

            if (value.State == VariableValueState.Ready)
            {
                return new ReadVariableResultDto
                {
                    State = value.State,
                    TypeName = value.TypeName,
                    ValueJson = value.ValueJson,
                    ValueVersion = value.ValueVersion,
                };
            }

            if (input.WaitPolicy == VariableWaitPolicy.NoWait)
            {
                throw new UserFriendlyException(
                    $"{AuroraStruct3DDomainErrorCodes.VariableUninitializedRead}: 变量 '{definition.Name}' 尚未初始化。"
                );
            }

            if (DateTime.UtcNow >= deadline)
            {
                if (
                    input.WaitPolicy == VariableWaitPolicy.WaitOrDefault
                    && !string.IsNullOrWhiteSpace(definition.DefaultValueJson)
                )
                {
                    return new ReadVariableResultDto
                    {
                        State = VariableValueState.Ready,
                        TypeName = definition.TypeName,
                        ValueJson = definition.DefaultValueJson,
                        ValueVersion = value.ValueVersion,
                    };
                }

                throw new UserFriendlyException(
                    $"{AuroraStruct3DDomainErrorCodes.VariableWaitTimeout}: 读取变量 '{definition.Name}' 超时。"
                );
            }

            await Task.Delay(100);
        }
    }

    /// <inheritdoc/>
    public async Task<WriteVariableResultDto> WriteAsync(WriteVariableInput input)
    {
        ValidateWriteInput(input);

        VariableDefinition definition = await GetDefinitionAsync(
            input.ProjectId,
            input.OwnerWorkflowId,
            input.VariableName
        );

        EnsureWritePermission(input.WriterWorkflowId, definition);

        string lockKey = BuildLockKey(
            input.ProjectId,
            input.InstanceId,
            input.OwnerWorkflowId,
            input.VariableName
        );

        await using IAbpDistributedLockHandle? distributedHandle =
            await _distributedLock.TryAcquireAsync(lockKey, DefaultLockAcquireTimeout);
        if (distributedHandle is null)
        {
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableWaitTimeout}: 获取变量 '{definition.Name}' 写锁超时。"
            );
        }

        VariableInstanceValue value = await GetOrCreateInstanceValueAsync(
            input.ProjectId,
            input.InstanceId,
            definition
        );

        if (input.ExpectedVersion.HasValue && input.ExpectedVersion.Value != value.ValueVersion)
        {
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableConcurrencyConflict}: 变量 '{definition.Name}' 版本冲突。"
            );
        }

        if (!string.Equals(definition.TypeName, input.TypeName, StringComparison.Ordinal))
        {
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableTypeMismatch}: 变量 '{definition.Name}' 类型不匹配，期望 {definition.TypeName}，实际 {input.TypeName}。"
            );
        }

        if (
            definition.Mutability == VariableMutability.ConstInit
            && value.ValueVersion > 0
            && value.State == VariableValueState.Ready
        )
        {
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableReadonlyWriteForbidden}: 变量 '{definition.Name}' 为 ConstInit，仅允许初始化一次。"
            );
        }

        value.Write(input.TypeName, input.ValueJson, input.WriterNodeId);
        await _variableInstanceRepository.UpdateAsync(value, autoSave: true);

        return new WriteVariableResultDto
        {
            State = value.State,
            ValueVersion = value.ValueVersion,
        };
    }

    private static void ValidateInitializeInput(InitializeVariablePoolInput input)
    {
        if (input.ProjectId == Guid.Empty)
        {
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableBadRequest}: ProjectId 不能为空。"
            );
        }

        if (input.InstanceId == Guid.Empty)
        {
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableBadRequest}: InstanceId 不能为空。"
            );
        }
    }

    private static void ValidateReadInput(ReadVariableInput input)
    {
        if (input.ProjectId == Guid.Empty || input.InstanceId == Guid.Empty)
        {
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableBadRequest}: ProjectId/InstanceId 不能为空。"
            );
        }

        if (input.ReaderWorkflowId == Guid.Empty || input.OwnerWorkflowId == Guid.Empty)
        {
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableBadRequest}: ReaderWorkflowId/OwnerWorkflowId 不能为空。"
            );
        }

        if (string.IsNullOrWhiteSpace(input.VariableName))
        {
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableBadRequest}: VariableName 不能为空。"
            );
        }
    }

    private static void ValidateWriteInput(WriteVariableInput input)
    {
        if (input.ProjectId == Guid.Empty || input.InstanceId == Guid.Empty)
        {
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableBadRequest}: ProjectId/InstanceId 不能为空。"
            );
        }

        if (input.WriterWorkflowId == Guid.Empty || input.OwnerWorkflowId == Guid.Empty)
        {
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableBadRequest}: WriterWorkflowId/OwnerWorkflowId 不能为空。"
            );
        }

        if (
            string.IsNullOrWhiteSpace(input.VariableName)
            || string.IsNullOrWhiteSpace(input.TypeName)
        )
        {
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableBadRequest}: VariableName/TypeName 不能为空。"
            );
        }

        if (string.IsNullOrWhiteSpace(input.ValueJson))
        {
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableBadRequest}: ValueJson 不能为空。"
            );
        }
    }

    private async Task<VariableDefinition> GetDefinitionAsync(
        Guid projectId,
        Guid ownerWorkflowId,
        string variableName
    )
    {
        IQueryable<VariableDefinition> queryable =
            await _variableDefinitionRepository.GetQueryableAsync();
        VariableDefinition? found = await AsyncExecuter.FirstOrDefaultAsync(
            queryable.Where(x =>
                x.ProjectId == projectId
                && x.OwnerWorkflowId == ownerWorkflowId
                && x.Name == variableName
            )
        );

        if (found is null)
        {
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableUndefinedReference}: 变量 '{ownerWorkflowId}/{variableName}' 不存在。"
            );
        }

        return found;
    }

    private void EnsureReadPermission(Guid readerWorkflowId, VariableDefinition definition)
    {
        if (definition.OwnerWorkflowId == readerWorkflowId)
        {
            return;
        }

        if (definition.Visibility == VariableVisibility.PublicRead)
        {
            return;
        }

        throw new UserFriendlyException(
            $"{AuroraStruct3DDomainErrorCodes.VariableVisibilityDenied}: 变量 '{definition.Name}' 非 PublicRead，禁止跨工作流读取。"
        );
    }

    private static void EnsureWritePermission(Guid writerWorkflowId, VariableDefinition definition)
    {
        if (definition.OwnerWorkflowId != writerWorkflowId)
        {
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableCrossWorkflowWriteForbidden}: 仅变量所属工作流可写。"
            );
        }

        if (definition.Mutability == VariableMutability.Readonly)
        {
            throw new UserFriendlyException(
                $"{AuroraStruct3DDomainErrorCodes.VariableReadonlyWriteForbidden}: 变量 '{definition.Name}' 为 Readonly。"
            );
        }
    }

    private async Task<VariableInstanceValue> GetOrCreateInstanceValueAsync(
        Guid projectId,
        Guid instanceId,
        VariableDefinition definition
    )
    {
        IQueryable<VariableInstanceValue> queryable =
            await _variableInstanceRepository.GetQueryableAsync();
        VariableInstanceValue? found = await AsyncExecuter.FirstOrDefaultAsync(
            queryable.Where(x =>
                x.ProjectId == projectId
                && x.InstanceId == instanceId
                && x.OwnerWorkflowId == definition.OwnerWorkflowId
                && x.VariableName == definition.Name
            )
        );

        if (found is not null)
        {
            return found;
        }

        VariableValueState state = string.IsNullOrWhiteSpace(definition.DefaultValueJson)
            ? VariableValueState.Uninitialized
            : VariableValueState.Ready;

        VariableInstanceValue created = VariableInstanceValue.Create(
            GuidGenerator.Create(),
            projectId,
            instanceId,
            definition.Id,
            definition.OwnerWorkflowId,
            definition.Name,
            definition.TypeName,
            state,
            definition.DefaultValueJson,
            state == VariableValueState.Ready ? 1 : 0
        );

        await _variableInstanceRepository.InsertAsync(created, autoSave: true);
        return created;
    }

    private static string BuildLockKey(
        Guid projectId,
        Guid instanceId,
        Guid ownerWorkflowId,
        string variableName
    )
    {
        return $"{projectId:N}:{instanceId:N}:{ownerWorkflowId:N}:{variableName}";
    }
}
