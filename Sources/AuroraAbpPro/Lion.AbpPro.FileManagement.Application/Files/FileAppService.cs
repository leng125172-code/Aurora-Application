using Mapster;
using Volo.Abp.EventBus.Distributed;

namespace Lion.AbpPro.FileManagement.Files;

/// <summary>
/// 文件
/// </summary>
[Authorize(FileManagementPermissions.FileManagement.Default)]
public class FileAppService : ApplicationService, IFileAppService
{
    private readonly FileObjectManager _fileObjectManager;
    private readonly FileManager _fileManager;
    private readonly IBlobContainer<AbpProFileManagementContainer> _blobContainer;
    private readonly IDistributedEventBus _distributedEventBus;

    public FileAppService(
        FileObjectManager fileObjectManager,
        IBlobContainer<AbpProFileManagementContainer> blobContainer,
        FileManager fileManager,
        IDistributedEventBus distributedEventBus
    )
    {
        _fileObjectManager = fileObjectManager;
        _blobContainer = blobContainer;
        _fileManager = fileManager;
        _distributedEventBus = distributedEventBus;
    }

    /// <summary>
    /// 分页查询文件
    /// </summary>
    public async Task<PagedResultDto<PageFileObjectOutput>> PageAsync(PageFileObjectInput input)
    {
        var result = new PagedResultDto<PageFileObjectOutput>();
        var totalCount = await _fileObjectManager.GetCountAsync(
            input.FileName,
            input.StartCreationTime,
            input.EndCreationTime
        );
        result.TotalCount = totalCount;
        if (totalCount <= 0)
            return result;
        var list = await _fileObjectManager.GetListAsync(
            input.FileName,
            input.StartCreationTime,
            input.EndCreationTime,
            input.PageSize,
            input.SkipCount
        );
        result.Items = list.Adapt<List<PageFileObjectOutput>>();
        return result;
    }

    [Authorize(FileManagementPermissions.FileManagement.Upload)]
    public async Task UploadAsync(UpdateFileInput input)
    {
        foreach (var formFile in input.Files)
        {
            // 获取文件的二进制数据
            using var memoryStream = new MemoryStream();
            await formFile.CopyToAsync(memoryStream);
            var fileBytes = memoryStream.ToArray();
            await _fileManager.CreateAsync(
                GuidGenerator.Create(),
                formFile.FileName,
                formFile.Length,
                formFile.ContentType,
                fileBytes
            );
        }
    }

    /// <summary>
    /// 删除文件
    /// </summary>
    [Authorize(FileManagementPermissions.FileManagement.Delete)]
    public Task DeleteAsync(DeleteFileObjectInput input)
    {
        return _fileManager.DeleteAsync(input.Id);
    }

    [Authorize(FileManagementPermissions.FileManagement.Download)]
    public async Task<RemoteStreamContent> DownloadAsync(DownloadFileObjectInput input)
    {
        var fileObject = await _fileObjectManager.GetAsync(input.Id);
        var file = await _blobContainer.GetAsync(input.Id.ToString());
        return new RemoteStreamContent(file, fileObject.FileName, fileObject.ContentType);
    }
}
