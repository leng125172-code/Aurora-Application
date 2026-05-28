using AuroraStruct3D.ProductModels;
using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.ProductModels.Dtos;

/// <summary>
/// 查询产品三维数模列表的输入 DTO（支持多条件过滤 + 分页 + 排序）
/// </summary>
public class GetProductModelListInput : PagedAndSortedResultRequestDto
{
    /// <summary>名称模糊搜索（为空时不过滤）</summary>
    public string? Filter { get; set; }

    /// <summary>按文件格式过滤（为空时不过滤）</summary>
    public ProductModelFormat? FileFormat { get; set; }

    /// <summary>按转换状态过滤（为空时不过滤）</summary>
    public ProductModelConversionStatus? ConversionStatus { get; set; }

    /// <summary>上传时间起始（UTC，为空时不过滤）</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>上传时间截止（UTC，为空时不过滤）</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>上传人用户 ID（为空时不过滤）</summary>
    public Guid? UploaderUserId { get; set; }
}
