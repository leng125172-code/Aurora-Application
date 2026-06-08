using Volo.Abp.BlobStoring;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 标定照片 BLOB 存储容器标记类（Step 5）。
/// 使用 FileSystem provider 时，文件存储在 appsettings.json 配置的 basePath 下。
/// 容器名：calib-photos
/// BLOB Key 格式：{projectId}/{cameraId}/{type}/{yyyyMMddHHmmss_fff}.jpg
/// </summary>
[BlobContainerName("calib-photos")]
public class CalibPhotoBlobContainer { }
