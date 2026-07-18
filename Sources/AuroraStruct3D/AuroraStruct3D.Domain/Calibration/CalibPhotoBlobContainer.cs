using Volo.Abp.BlobStoring;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 标定照片 BLOB 存储容器标记类（Step 5）。
/// 使用 FileSystem provider 时，文件存储在 appsettings.json 配置的 basePath 下。
/// 容器名：calib-photos
/// BLOB Key 格式：{projectId}/{cameraId}/{type}/{yyyyMMddHHmmss_fff}[_相位后缀][_f帧号].jpg
/// 相位后缀：_ws=白屏 / _cb=棋盘格；帧号如 _f000。
/// </summary>
[BlobContainerName("calib-photos")]
public class CalibPhotoBlobContainer { }
