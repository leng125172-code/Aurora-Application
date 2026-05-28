using AuroraStruct3D.ProductModels;
using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.ProductModels.Dtos;

/// <summary>
/// 产品三维数模输出 DTO
/// </summary>
public class ProductModelDto : FullAuditedEntityDto<Guid>
{
    /// <summary>数模显示名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>原始上传文件名（含扩展名）</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>文件格式</summary>
    public ProductModelFormat FileFormat { get; set; }

    /// <summary>文件格式的中文描述（如 "STEP/STP"）</summary>
    public string FileFormatDisplay { get; set; } = string.Empty;

    /// <summary>文件大小（字节数）</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>格式转换状态</summary>
    public ProductModelConversionStatus ConversionStatus { get; set; }

    /// <summary>转换失败时的错误信息（ConversionStatus == Failed 时有值）</summary>
    public string? ConversionErrorMessage { get; set; }

    /// <summary>
    /// 可下载/预览的文件是否就绪。
    /// 当格式无需转换（PLY/OBJ）或转换已成功时为 true。
    /// </summary>
    public bool IsReady { get; set; }

    /// <summary>
    /// 本次上传是否需要格式转换（即已入队 Hangfire 转换任务）。
    /// 仅在上传响应中有实际意义；列表查询中此字段始终为 false。
    /// </summary>
    public bool NeedsConversion { get; set; }

    /// <summary>上传人用户名（审计用）</summary>
    public string? UploaderUserName { get; set; }
}
