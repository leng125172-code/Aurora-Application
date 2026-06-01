using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 标定采集图像：单台相机在某一帧的拍摄结果，原始字节流存放于 BLOB 容器
/// <see cref="CalibrationImageBlobContainer"/>，本表仅存元数据。
/// </summary>
public class CalibrationCaptureImage : Entity<Guid>
{
    /// <summary>所属采集帧 ID</summary>
    public Guid CalibrationCaptureFrameId { get; private set; }

    /// <summary>拍摄相机的设备 ID</summary>
    public Guid CameraDeviceId { get; private set; }

    /// <summary>该相机角色（冗余，便于查询）</summary>
    public CameraRole CameraRole { get; private set; }

    /// <summary>
    /// BLOB 键名：约定 "{projectId}/frame-{frameIndex:D4}/camera-{cameraId}.{ext}"。
    /// </summary>
    public string BlobName { get; private set; } = null!;

    /// <summary>图像宽度（像素）</summary>
    public int Width { get; private set; }

    /// <summary>图像高度（像素）</summary>
    public int Height { get; private set; }

    /// <summary>文件大小（字节）</summary>
    public long FileSizeBytes { get; private set; }

    /// <summary>单图重投影误差（Step 5 计算后回填）</summary>
    public double? ReprojectionError { get; private set; }

    /// <summary>EF Core 构造函数</summary>
    protected CalibrationCaptureImage() { }

    /// <summary>创建采集图像</summary>
    public CalibrationCaptureImage(
        Guid id,
        Guid calibrationCaptureFrameId,
        Guid cameraDeviceId,
        CameraRole cameraRole,
        string blobName,
        int width,
        int height,
        long fileSizeBytes
    )
        : base(id)
    {
        CalibrationCaptureFrameId = calibrationCaptureFrameId;
        CameraDeviceId = cameraDeviceId;
        CameraRole = cameraRole;
        BlobName = Check.NotNullOrWhiteSpace(
            blobName,
            nameof(blobName),
            CalibrationManagementConsts.MaxBlobNameLength
        );
        Width = width;
        Height = height;
        FileSizeBytes = fileSizeBytes;
    }

    /// <summary>写入单图重投影误差</summary>
    public void SetReprojectionError(double error) => ReprojectionError = error;
}
