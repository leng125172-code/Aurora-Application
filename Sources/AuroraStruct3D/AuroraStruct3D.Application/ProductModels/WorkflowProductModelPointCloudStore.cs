using System.Security.Cryptography;
using AuroraStruct3D.OpenCV.File.PointCloud;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.ProductModels;

internal sealed class WorkflowProductModelPointCloudStore(
    IRepository<ProductModel, Guid> repository,
    IBlobContainer<ProductModelBlobContainer> blobContainer
) : IProductModelPointCloudStore
{
    private static readonly HashSet<string> SupportedExtensions = new(
        [".ply", ".pcd", ".xyz", ".txt", ".pts", ".asc", ".obj"],
        StringComparer.OrdinalIgnoreCase
    );

    public async Task<string> MaterializeAsync(Guid modelId)
    {
        ProductModel model = await repository.GetAsync(modelId);
        if (
            model.ConversionStatus
            is not ProductModelConversionStatus.NotRequired
                and not ProductModelConversionStatus.Success
        )
            throw new UserFriendlyException($"工艺模型“{model.Name}”尚未转换完成。");

        string blobName = model.ConvertedBlobName ?? model.OriginalBlobName;
        string fileName = model.ConvertedBlobName is not null
            ? Path.GetFileNameWithoutExtension(model.OriginalFileName) + ".ply"
            : model.OriginalFileName;
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension))
            throw new UserFriendlyException(
                $"工艺模型格式 {extension} 不能用于点云比较，请转换为 PLY 或 OBJ。"
            );

        string cacheRoot = Path.Combine(Path.GetTempPath(), "aurora-product-model-cache");
        Directory.CreateDirectory(cacheRoot);
        string hash = Convert
            .ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(blobName)))
            .ToLowerInvariant();
        string localPath = Path.Combine(cacheRoot, $"{hash}{extension}");
        if (File.Exists(localPath))
            return localPath;

        await using Stream source = await blobContainer.GetAsync(blobName);
        await using FileStream target = new(
            localPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous
        );
        await source.CopyToAsync(target);
        return localPath;
    }
}
