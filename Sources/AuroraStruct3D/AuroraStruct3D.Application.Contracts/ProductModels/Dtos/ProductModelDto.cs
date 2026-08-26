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

    /// <summary>原始数模坐标的长度单位</summary>
    public ProductModelLengthUnit LengthUnit { get; set; }

    /// <summary>长度单位显示名称</summary>
    public string LengthUnitDisplay { get; set; } = string.Empty;

    /// <summary>网格表面采样间距（毫米）</summary>
    public double SurfaceSamplingSpacingMm { get; set; }

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

/// <summary>
/// 三维数模操作日志输出 DTO。
/// </summary>
public class ProductModelOperationLogDto : EntityDto<Guid>
{
    /// <summary>关联的数模 ID</summary>
    public Guid? ProductModelId { get; set; }

    /// <summary>数模名称快照</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>原始文件名快照</summary>
    public string? OriginalFileName { get; set; }

    /// <summary>操作类型</summary>
    public ProductModelOperationType OperationType { get; set; }

    /// <summary>操作发生时间（UTC）</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>是否成功</summary>
    public bool IsSuccess { get; set; }

    /// <summary>操作参数摘要</summary>
    public string? ParameterSummary { get; set; }

    /// <summary>失败时的错误消息</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>操作耗时（毫秒）</summary>
    public long DurationMs { get; set; }
}

/// <summary>
/// 查询三维数模操作日志请求 DTO。
/// </summary>
public class GetProductModelLogListInput : PagedResultRequestDto
{
    /// <summary>按数模 ID 过滤（可选）</summary>
    public Guid? ProductModelId { get; set; }

    /// <summary>关键字过滤（匹配数模名称、文件名、参数摘要）</summary>
    public string? Filter { get; set; }

    /// <summary>按操作类型过滤（可选）</summary>
    public ProductModelOperationType? OperationType { get; set; }

    /// <summary>仅返回失败记录</summary>
    public bool? IsFailedOnly { get; set; }

    /// <summary>开始时间（UTC）</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>结束时间（UTC）</summary>
    public DateTime? EndTime { get; set; }
}
