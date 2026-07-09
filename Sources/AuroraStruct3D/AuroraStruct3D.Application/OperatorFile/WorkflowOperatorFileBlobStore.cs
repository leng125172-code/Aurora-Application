using AuroraStruct3D.OpenCV.File.PointCloud;
using Volo.Abp.BlobStoring;

namespace AuroraStruct3D.OperatorFile;

internal sealed class WorkflowOperatorFileBlobStore : IOperatorFileBlobStore
{
    private readonly IBlobContainer<OperatorFileBlobContainer> _blobContainer;

    public WorkflowOperatorFileBlobStore(IBlobContainer<OperatorFileBlobContainer> blobContainer)
    {
        _blobContainer = blobContainer;
    }

    public async Task<string> SavePointCloudPlyAsync(
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        string normalizedFileName = Path.GetFileName(fileName.Trim());
        string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss_fff");
        string blobName =
            $"{save_point_cloud_to_blob.OperatorId:N}/{timestamp}_{normalizedFileName}";

        await using MemoryStream stream = new(content, writable: false);
        await _blobContainer.SaveAsync(blobName, stream, overrideExisting: true, cancellationToken);
        return blobName;
    }

    public string BuildDownloadUrl(string blobName) =>
        $"/api/app/operator-file/download?blobName={Uri.EscapeDataString(blobName)}";
}
