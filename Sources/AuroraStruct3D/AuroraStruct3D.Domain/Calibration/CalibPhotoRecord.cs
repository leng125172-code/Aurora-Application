using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 标定照片记录实体（Step 5）。
/// 每次内参或外参拍照后，将照片存入 BLOB，并在此记录元数据。
///
/// 数据库表：AbpProCalibPhotoRecords
/// </summary>
public class CalibPhotoRecord : AuditedEntity<Guid>
{
    /// <summary>所属标定项目ID</summary>
    public Guid CalibProjectId { get; private set; }

    /// <summary>对应的相机设备ID</summary>
    public Guid CameraDeviceId { get; private set; }

    /// <summary>照片类型（内参 / 外参）</summary>
    public CalibPhotoType PhotoType { get; private set; }

    /// <summary>双目成对拍照分组ID（仅 StereoExtrinsicPair 使用）</summary>
    public Guid? PairGroupId { get; private set; }

    /// <summary>双目成对拍照角色（仅 StereoExtrinsicPair 使用）</summary>
    public StereoPhotoRole? StereoRole { get; private set; }

    /// <summary>投影外参双拍阶段（仅 Extrinsic 使用）</summary>
    public ExtrinsicPhotoPhase? ExtrinsicPhase { get; private set; }

    /// <summary>BLOB 存储键（calib-photos 容器内的相对路径）</summary>
    public string BlobKey { get; private set; } = null!;

    /// <summary>原图缩略图（Base64 JPEG，~400px 宽，用于前端预览，可为 null 表示尚未生成）</summary>
    public string? ThumbnailBase64 { get; private set; }

    /// <summary>是否检测到有效棋盘格角点（OpenCV FindChessboardCorners 结果）</summary>
    public bool IsValid { get; private set; }

    /// <summary>检测到的角点数量（内角点总数 = rows × cols，无效时为 0）</summary>
    public int CornerCountDetected { get; private set; }

    /// <summary>图像差分分数（0-1，与同组另一张照片的差异程度）</summary>
    public double? ImageDiffScore { get; private set; }

    /// <summary>图像差分是否显著（两次拍摄有明显差异）</summary>
    public bool? ImageDiffSignificant { get; private set; }

    /// <summary>拍摄时间</summary>
    public DateTime CapturedAt { get; private set; }

    // EF Core 所需的无参构造函数
    protected CalibPhotoRecord() { }

    /// <summary>
    /// 创建标定照片记录
    /// </summary>
    /// <param name="id">实体ID</param>
    /// <param name="calibProjectId">标定项目ID</param>
    /// <param name="cameraDeviceId">相机设备ID</param>
    /// <param name="photoType">照片类型</param>
    /// <param name="blobKey">BLOB 存储键</param>
    /// <param name="isValid">角点检测是否成功</param>
    /// <param name="cornerCountDetected">检测到的角点数量</param>
    /// <param name="thumbnailBase64">缩略图 Base64（可为 null）</param>
    public CalibPhotoRecord(
        Guid id,
        Guid calibProjectId,
        Guid cameraDeviceId,
        CalibPhotoType photoType,
        string blobKey,
        bool isValid,
        int cornerCountDetected,
        string? thumbnailBase64 = null,
        Guid? pairGroupId = null,
        StereoPhotoRole? stereoRole = null,
        ExtrinsicPhotoPhase? extrinsicPhase = null,
        double? imageDiffScore = null,
        bool? imageDiffSignificant = null
    )
        : base(id)
    {
        CalibProjectId = calibProjectId;
        CameraDeviceId = cameraDeviceId;
        PhotoType = photoType;
        SetBlobKey(blobKey);
        IsValid = isValid;
        CornerCountDetected = cornerCountDetected;
        ThumbnailBase64 = thumbnailBase64;
        PairGroupId = pairGroupId;
        StereoRole = stereoRole;
        ExtrinsicPhase = extrinsicPhase;
        ImageDiffScore = imageDiffScore;
        ImageDiffSignificant = imageDiffSignificant;
        CapturedAt = DateTime.UtcNow;
    }

    /// <summary>设置 BLOB 键</summary>
    private void SetBlobKey(string blobKey)
    {
        Check.NotNullOrWhiteSpace(blobKey, nameof(blobKey), CalibConsts.MaxBlobKeyLength);
        BlobKey = blobKey;
    }

    /// <summary>更新缩略图</summary>
    public CalibPhotoRecord SetThumbnail(string? thumbnailBase64)
    {
        ThumbnailBase64 = thumbnailBase64;
        return this;
    }

    /// <summary>设置图像差分结果</summary>
    public CalibPhotoRecord SetImageDiffResult(double? diffScore, bool? isSignificant)
    {
        ImageDiffScore = diffScore;
        ImageDiffSignificant = isSignificant;
        return this;
    }
}
