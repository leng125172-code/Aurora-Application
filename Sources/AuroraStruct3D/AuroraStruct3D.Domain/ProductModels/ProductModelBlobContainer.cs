using Volo.Abp.BlobStoring;

namespace AuroraStruct3D.ProductModels;

/// <summary>
/// 产品三维数模 BLOB 存储容器标记类。
/// 使用 FileSystem provider 时，文件存储在 appsettings.json 配置的 basePath 下。
/// </summary>
[BlobContainerName(ProductModelConsts.BlobContainerName)]
public class ProductModelBlobContainer { }
