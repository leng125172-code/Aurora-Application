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
    private const string NormalizerVersion = "v2-mm-surface";
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
        string cacheIdentity = string.Join(
            '|',
            NormalizerVersion,
            blobName,
            (int)model.LengthUnit,
            model.SurfaceSamplingSpacingMm.ToString(
                "R",
                System.Globalization.CultureInfo.InvariantCulture
            )
        );
        string hash = Convert
            .ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(cacheIdentity)))
            .ToLowerInvariant();
        string localPath = Path.Combine(cacheRoot, $"{hash}.ply");
        if (File.Exists(localPath) && new FileInfo(localPath).Length > 0)
            return localPath;

        await using Stream source = await blobContainer.GetAsync(blobName);
        string temporaryPath = Path.Combine(cacheRoot, $"{hash}.{Guid.NewGuid():N}.tmp");
        try
        {
            ProductModelPointCloudNormalizer.Normalize(
                source,
                extension,
                temporaryPath,
                model.LengthUnit,
                model.SurfaceSamplingSpacingMm,
                ProductModelConsts.MaxReferencePointCount
            );
            File.Move(temporaryPath, localPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
        return localPath;
    }
}
