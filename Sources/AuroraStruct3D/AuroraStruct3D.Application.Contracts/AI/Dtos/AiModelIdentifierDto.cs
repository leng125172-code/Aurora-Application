using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.AI.Dtos;

/// <summary>
/// AI 模型标识输出 DTO。
/// </summary>
public class AiModelIdentifierDto : EntityDto<Guid>
{
    /// <summary>标识名称。</summary>
    public string Name { get; set; } = string.Empty;
}
