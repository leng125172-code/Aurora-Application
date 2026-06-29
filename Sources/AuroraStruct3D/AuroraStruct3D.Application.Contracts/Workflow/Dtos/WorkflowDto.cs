using System.Text.Json;
using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.Workflow.Dtos;

/// <summary>
/// 工作流输出 DTO。
/// 作为新建 / 回显 / 修改保存接口的统一响应。
/// <see cref="Content"/> 以原生 JSON 形式内联返回，前端可直接取 graphData 渲染画布。
/// </summary>
public class WorkflowDto : FullAuditedEntityDto<Guid>
{
    /// <summary>所属项目唯一标识。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>工作流名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>完整 WorkflowPayload JSON（原生对象，非转义字符串）。</summary>
    public JsonElement Content { get; set; }
}
