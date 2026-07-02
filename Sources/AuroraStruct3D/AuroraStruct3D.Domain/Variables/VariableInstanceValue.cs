using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Variables;

/// <summary>
/// 在线变量池中的实例变量值。
/// </summary>
public class VariableInstanceValue : FullAuditedAggregateRoot<Guid>
{
    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>执行实例 ID。</summary>
    public Guid InstanceId { get; private set; }

    /// <summary>变量定义 ID。</summary>
    public Guid VariableDefinitionId { get; private set; }

    /// <summary>变量所属工作流 ID。</summary>
    public Guid OwnerWorkflowId { get; private set; }

    /// <summary>变量名（冗余存储用于快速查询）。</summary>
    public string VariableName { get; private set; } = string.Empty;

    /// <summary>变量类型名。</summary>
    public string TypeName { get; private set; } = string.Empty;

    /// <summary>值状态。</summary>
    public VariableValueState State { get; private set; }

    /// <summary>变量值 JSON。</summary>
    public string? ValueJson { get; private set; }

    /// <summary>值版本号（乐观并发）。</summary>
    public long ValueVersion { get; private set; }

    /// <summary>最后写入节点标识。</summary>
    public string? LastWriterNodeId { get; private set; }

    /// <summary>EF Core 无参构造。</summary>
    protected VariableInstanceValue() { }

    /// <summary>
    /// 创建实例变量槽位。
    /// </summary>
    public static VariableInstanceValue Create(
        Guid id,
        Guid projectId,
        Guid instanceId,
        Guid variableDefinitionId,
        Guid ownerWorkflowId,
        string variableName,
        string typeName,
        VariableValueState state,
        string? valueJson,
        long valueVersion
    )
    {
        Check.NotNullOrWhiteSpace(
            variableName,
            nameof(variableName),
            VariableDefinitionConsts.MaxVariableNameLength
        );
        Check.NotNullOrWhiteSpace(
            typeName,
            nameof(typeName),
            VariableDefinitionConsts.MaxTypeNameLength
        );

        return new VariableInstanceValue
        {
            Id = id,
            ProjectId = projectId,
            InstanceId = instanceId,
            VariableDefinitionId = variableDefinitionId,
            OwnerWorkflowId = ownerWorkflowId,
            VariableName = variableName,
            TypeName = typeName,
            State = state,
            ValueJson = valueJson,
            ValueVersion = valueVersion,
        };
    }

    /// <summary>
    /// 标记为初始化中。
    /// </summary>
    public void MarkInitializing()
    {
        State = VariableValueState.Initializing;
    }

    /// <summary>
    /// 写入变量值并推进版本。
    /// </summary>
    public void Write(string typeName, string valueJson, string? writerNodeId)
    {
        Check.NotNullOrWhiteSpace(
            typeName,
            nameof(typeName),
            VariableDefinitionConsts.MaxTypeNameLength
        );
        Check.NotNull(valueJson, nameof(valueJson));

        TypeName = typeName;
        ValueJson = valueJson;
        State = VariableValueState.Ready;
        ValueVersion++;
        LastWriterNodeId = writerNodeId;
    }

    /// <summary>
    /// 标记为故障状态。
    /// </summary>
    public void MarkFaulted()
    {
        State = VariableValueState.Faulted;
    }
}
