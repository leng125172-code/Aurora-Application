using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型与模型标识的关联实体。
/// </summary>
public class AiModelIdentifierLink : Entity<Guid>
{
    /// <summary>模型 ID。</summary>
    public Guid AiModelId { get; private set; }

    /// <summary>标识 ID。</summary>
    public Guid IdentifierId { get; private set; }

    /// <summary>EF Core 使用的无参构造函数。</summary>
    protected AiModelIdentifierLink() { }

    /// <summary>
    /// 创建模型标识关联。
    /// </summary>
    public AiModelIdentifierLink(Guid id, Guid aiModelId, Guid identifierId)
    {
        Id = id;
        AiModelId = Check.NotNull(aiModelId, nameof(aiModelId));
        IdentifierId = Check.NotNull(identifierId, nameof(identifierId));
    }
}
