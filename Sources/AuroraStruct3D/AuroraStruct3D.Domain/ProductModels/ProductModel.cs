using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.ProductModels;

/// <summary>
/// 产品三维数模聚合根。
/// 记录上传的三维文件元数据、BLOB 存储键名、格式转换状态等信息。
/// 审计日志（创建人/时间、修改人/时间、删除人/时间）由 FullAuditedAggregateRoot 自动记录。
///
/// 数据库表：AbpProProductModels
/// </summary>
public class ProductModel : FullAuditedAggregateRoot<Guid>
{
    // ─────────────────────────── 基本信息 ───────────────────────────

    /// <summary>数模显示名称（由用户指定，支持重命名）</summary>
    public string Name { get; private set; } = null!;

    /// <summary>原始上传文件名（含扩展名，只读，不可修改）</summary>
    public string OriginalFileName { get; private set; } = null!;

    /// <summary>文件格式（由后端根据扩展名推断）</summary>
    public ProductModelFormat FileFormat { get; private set; }

    /// <summary>文件大小（字节数）</summary>
    public long FileSizeBytes { get; private set; }

    /// <summary>原始数模坐标的长度单位</summary>
    public ProductModelLengthUnit LengthUnit { get; private set; }

    /// <summary>网格转换为参考点云时使用的表面采样间距（毫米）</summary>
    public double SurfaceSamplingSpacingMm { get; private set; }

    // ─────────────────────────── BLOB 存储 ───────────────────────────

    /// <summary>
    /// 原始文件在 BLOB 存储中的键名（上传成功后立即写入）。
    /// 格式为 "original/{Guid}"，与转换后的 PLY 文件键名区分。
    /// </summary>
    public string OriginalBlobName { get; private set; } = null!;

    /// <summary>
    /// 转换后 PLY 文件在 BLOB 存储中的键名。
    /// 仅当 ConversionStatus == Success 时有值；不需转换的格式此字段为 null。
    /// </summary>
    public string? ConvertedBlobName { get; private set; }

    // ─────────────────────────── 转换状态 ───────────────────────────

    /// <summary>格式转换状态</summary>
    public ProductModelConversionStatus ConversionStatus { get; private set; }

    /// <summary>转换失败时的错误信息（ConversionStatus == Failed 时有值）</summary>
    public string? ConversionErrorMessage { get; private set; }

    // ─────────────────────────── 构造函数 ───────────────────────────

    /// <summary>EF Core 所需的无参构造函数（不得直接使用）</summary>
    protected ProductModel() { }

    /// <summary>
    /// 工厂方法：创建新的产品三维数模记录。
    /// </summary>
    /// <param name="id">唯一标识</param>
    /// <param name="name">用户指定的显示名称</param>
    /// <param name="originalFileName">原始上传文件名（含扩展名）</param>
    /// <param name="fileFormat">文件格式</param>
    /// <param name="fileSizeBytes">文件大小（字节）</param>
    /// <param name="originalBlobName">原始文件的 BLOB 键名</param>
    public static ProductModel Create(
        Guid id,
        string name,
        string originalFileName,
        ProductModelFormat fileFormat,
        long fileSizeBytes,
        string originalBlobName,
        ProductModelLengthUnit lengthUnit = ProductModelLengthUnit.Millimeter,
        double surfaceSamplingSpacingMm = ProductModelConsts.DefaultSurfaceSamplingSpacingMm
    )
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), ProductModelConsts.MaxNameLength);
        Check.NotNullOrWhiteSpace(originalFileName, nameof(originalFileName));
        Check.NotNullOrWhiteSpace(originalBlobName, nameof(originalBlobName));
        if (!Enum.IsDefined(lengthUnit))
            throw new BusinessException("ProductModel:InvalidLengthUnit").WithData(
                "lengthUnit",
                lengthUnit
            );
        if (
            !double.IsFinite(surfaceSamplingSpacingMm)
            || surfaceSamplingSpacingMm < ProductModelConsts.MinSurfaceSamplingSpacingMm
            || surfaceSamplingSpacingMm > ProductModelConsts.MaxSurfaceSamplingSpacingMm
        )
            throw new BusinessException("ProductModel:InvalidSurfaceSamplingSpacing").WithData(
                "surfaceSamplingSpacingMm",
                surfaceSamplingSpacingMm
            );

        ProductModelConversionStatus initialStatus = ProductModelConsts.NeedsConversion(fileFormat)
            ? ProductModelConversionStatus.Pending
            : ProductModelConversionStatus.NotRequired;

        return new ProductModel
        {
            Id = id,
            Name = name,
            OriginalFileName = originalFileName,
            FileFormat = fileFormat,
            FileSizeBytes = fileSizeBytes,
            LengthUnit = lengthUnit,
            SurfaceSamplingSpacingMm = surfaceSamplingSpacingMm,
            OriginalBlobName = originalBlobName,
            ConversionStatus = initialStatus,
        };
    }

    // ─────────────────────────── 领域方法 ───────────────────────────

    /// <summary>
    /// 更新数模的显示名称。
    /// </summary>
    /// <param name="name">新名称（不得为空）</param>
    public void UpdateName(string name)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), ProductModelConsts.MaxNameLength);
    }

    /// <summary>
    /// 标记为"转换中"状态（Hangfire Job 开始执行时调用）。
    /// </summary>
    public void SetConverting()
    {
        ConversionStatus = ProductModelConversionStatus.Converting;
        ConversionErrorMessage = null;
    }

    /// <summary>
    /// 标记为"转换成功"，记录转换后的 PLY 文件 BLOB 键名。
    /// </summary>
    /// <param name="convertedBlobName">PLY 文件在 BLOB 存储中的键名</param>
    public void SetSuccess(string convertedBlobName)
    {
        Check.NotNullOrWhiteSpace(convertedBlobName, nameof(convertedBlobName));
        ConversionStatus = ProductModelConversionStatus.Success;
        ConvertedBlobName = convertedBlobName;
        ConversionErrorMessage = null;
    }

    /// <summary>
    /// 标记为"转换失败"，记录错误信息（支持手动重试）。
    /// </summary>
    /// <param name="errorMessage">错误详情（截断到最大长度）</param>
    public void SetFailed(string errorMessage)
    {
        ConversionStatus = ProductModelConversionStatus.Failed;
        ConversionErrorMessage =
            errorMessage.Length > ProductModelConsts.MaxErrorMessageLength
                ? errorMessage[..ProductModelConsts.MaxErrorMessageLength]
                : errorMessage;
    }

    /// <summary>
    /// 当转换产物文件丢失时，清理失效引用并恢复为可重试状态。
    /// </summary>
    /// <param name="errorMessage">提示信息</param>
    public void MarkConvertedBlobMissing(string errorMessage)
    {
        ConvertedBlobName = null;
        SetFailed(errorMessage);
    }

    /// <summary>
    /// 重置为"待转换"状态（手动重试时调用）。
    /// </summary>
    public void ResetForRetry()
    {
        if (ConversionStatus != ProductModelConversionStatus.Failed)
        {
            throw new BusinessException("ProductModel:ConversionNotFailed").WithData(
                "status",
                ConversionStatus
            );
        }

        ConversionStatus = ProductModelConversionStatus.Pending;
        ConversionErrorMessage = null;
    }
}
