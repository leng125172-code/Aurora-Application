using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型标识主数据。
/// </summary>
public class AiModelIdentifier : FullAuditedAggregateRoot<Guid>
{
    /// <summary>标识名称。</summary>
    public string Name { get; private set; } = null!;

    /// <summary>规范化名称。</summary>
    public string NormalizedName { get; private set; } = null!;

    /// <summary>EF Core 使用的无参构造函数。</summary>
    protected AiModelIdentifier() { }

    /// <summary>
    /// 创建模型标识。
    /// </summary>
    public static AiModelIdentifier Create(Guid id, string name)
    {
        AiModelIdentifier identifier = new() { Id = id };

        identifier.SetName(name);
        return identifier;
    }

    /// <summary>
    /// 规范化标识名称。
    /// </summary>
    public static string NormalizeName(string name)
    {
        return Check
            .NotNullOrWhiteSpace(name, nameof(name), AiModelConsts.MaxIdentifierNameLength)
            .Trim()
            .ToUpperInvariant();
    }

    private void SetName(string name)
    {
        string trimmed = Check
            .NotNullOrWhiteSpace(name, nameof(name), AiModelConsts.MaxIdentifierNameLength)
            .Trim();

        Name = trimmed;
        NormalizedName = NormalizeName(trimmed);
    }
}
