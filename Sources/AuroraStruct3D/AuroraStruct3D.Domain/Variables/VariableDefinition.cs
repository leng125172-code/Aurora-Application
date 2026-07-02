using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Variables;

/// <summary>
/// 离线变量定义聚合根。
/// </summary>
public class VariableDefinition : FullAuditedAggregateRoot<Guid>
{
    /// <summary>项目 ID（隔离边界）。</summary>
    public Guid ProjectId { get; private set; }

    /// <summary>所属工作流 ID（所有者）。</summary>
    public Guid OwnerWorkflowId { get; private set; }

    /// <summary>变量名。</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>变量类型名。</summary>
    public string TypeName { get; private set; } = string.Empty;

    /// <summary>可见性（Private/PublicRead）。</summary>
    public VariableVisibility Visibility { get; private set; }

    /// <summary>可变性（Mutable/Readonly/ConstInit）。</summary>
    public VariableMutability Mutability { get; private set; }

    /// <summary>是否要求执行前初始化。</summary>
    public bool IsRequiredInit { get; private set; }

    /// <summary>默认值 JSON。</summary>
    public string? DefaultValueJson { get; private set; }

    /// <summary>编译快照版本号。</summary>
    public long SnapshotVersion { get; private set; }

    /// <summary>EF Core 无参构造。</summary>
    protected VariableDefinition() { }

    /// <summary>
    /// 创建变量定义。
    /// </summary>
    public static VariableDefinition Create(
        Guid id,
        Guid projectId,
        Guid ownerWorkflowId,
        string name,
        string typeName,
        VariableVisibility visibility,
        VariableMutability mutability,
        bool isRequiredInit,
        string? defaultValueJson,
        long snapshotVersion
    )
    {
        Check.NotNullOrWhiteSpace(
            name,
            nameof(name),
            VariableDefinitionConsts.MaxVariableNameLength
        );
        Check.NotNullOrWhiteSpace(
            typeName,
            nameof(typeName),
            VariableDefinitionConsts.MaxTypeNameLength
        );
        if (defaultValueJson is not null)
        {
            Check.Length(
                defaultValueJson,
                nameof(defaultValueJson),
                VariableDefinitionConsts.MaxDefaultValueLength
            );
        }

        return new VariableDefinition
        {
            Id = id,
            ProjectId = projectId,
            OwnerWorkflowId = ownerWorkflowId,
            Name = name,
            TypeName = typeName,
            Visibility = visibility,
            Mutability = mutability,
            IsRequiredInit = isRequiredInit,
            DefaultValueJson = defaultValueJson,
            SnapshotVersion = snapshotVersion,
        };
    }

    /// <summary>
    /// 更新变量定义。
    /// </summary>
    public void Update(
        string typeName,
        VariableVisibility visibility,
        VariableMutability mutability,
        bool isRequiredInit,
        string? defaultValueJson,
        long snapshotVersion
    )
    {
        Check.NotNullOrWhiteSpace(
            typeName,
            nameof(typeName),
            VariableDefinitionConsts.MaxTypeNameLength
        );
        if (defaultValueJson is not null)
        {
            Check.Length(
                defaultValueJson,
                nameof(defaultValueJson),
                VariableDefinitionConsts.MaxDefaultValueLength
            );
        }

        TypeName = typeName;
        Visibility = visibility;
        Mutability = mutability;
        IsRequiredInit = isRequiredInit;
        DefaultValueJson = defaultValueJson;
        SnapshotVersion = snapshotVersion;
    }
}
