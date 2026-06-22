using Volo.Abp.BlobStoring;

namespace AuroraStruct3D.OperatorFile;

/// <summary>
/// 算子文件上传 BLOB 存储容器标记类。
/// 容器名：operator-files
/// BLOB Key 格式：{operatorId:N}/{yyyyMMddHHmmss_fff}_{originalFileName}
/// </summary>
[BlobContainerName("operator-files")]
public class OperatorFileBlobContainer { }