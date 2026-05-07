using Lion.AbpPro.ImportExport.Import.Excel;
using Volo.Abp.EventBus;
using Volo.Abp.Guids;

namespace Lion.AbpPro.FileManagement.Files.LocalEventHandler;

public class ImportExcelFailedCreatedLocalEventHandler
    : ILocalEventHandler<ImportExcelFailedEto>,
        ITransientDependency
{
    private readonly IFileObjectRepository _fileObjectRepository;
    private readonly IGuidGenerator _guidGenerator;

    public ImportExcelFailedCreatedLocalEventHandler(
        IFileObjectRepository fileObjectRepository,
        IGuidGenerator guidGenerator
    )
    {
        _fileObjectRepository = fileObjectRepository;
        _guidGenerator = guidGenerator;
    }

    public virtual async Task HandleEventAsync(ImportExcelFailedEto eventData)
    {
        var entity = await _fileObjectRepository.FindAsync(Guid.Parse(eventData.BlobId));
        if (entity != null)
        {
            await _fileObjectRepository.InsertAsync(
                new FileObject(
                    Guid.Parse(eventData.BlobErrorId),
                    eventData.BlobErrorName,
                    entity.FileSize,
                    entity.ContentType
                )
            );
        }
    }
}
