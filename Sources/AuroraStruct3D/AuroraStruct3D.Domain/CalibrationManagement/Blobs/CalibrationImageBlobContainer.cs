using Volo.Abp.BlobStoring;

namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 标定采集图像 BLOB 存储容器标记类。
/// BLOB 键名约定：{projectId}/frame-{frameIndex:D4}/camera-{cameraId}.{ext}
/// </summary>
[BlobContainerName(CalibrationManagementConsts.BlobContainerName)]
public class CalibrationImageBlobContainer { }
