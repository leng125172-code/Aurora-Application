namespace Lion.AbpPro.ImportExport.Import;

public class ImportRecordCreatedDistributedEventHandler
    : IDistributedEventHandler<StartImportRecordEto>,
        ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ImportExcelContributorOptions _options;
    private readonly ILogger<ImportRecordCreatedDistributedEventHandler> _logger;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly IImportRecordManager _importRecordManager;
    private readonly ICurrentTenant _currentTenant;

    public ImportRecordCreatedDistributedEventHandler(
        IServiceProvider serviceProvider,
        IOptions<ImportExcelContributorOptions> options,
        ILogger<ImportRecordCreatedDistributedEventHandler> logger,
        IUnitOfWorkManager unitOfWorkManager,
        IJsonSerializer jsonSerializer,
        IImportRecordManager importRecordManager,
        ICurrentTenant currentTenant
    )
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _unitOfWorkManager = unitOfWorkManager;
        _jsonSerializer = jsonSerializer;
        _importRecordManager = importRecordManager;
        _currentTenant = currentTenant;
        _options = options.Value;
    }

    [UnitOfWork]
    public virtual async Task HandleEventAsync(StartImportRecordEto eventData)
    {
        try
        {
            using (_currentTenant.Change(eventData.TenantId))
            {
                if (eventData.Contributor.IsNullOrWhiteSpace())
                {
                    throw new UserFriendlyException("导入贡献者不能为空");
                }

                _options.Contributors.TryGetValue(eventData.Contributor, out var contributor);

                if (contributor == null)
                {
                    throw new UserFriendlyException("导入贡献者不存在");
                }

                var import = (IImportExcelContributor)
                    _serviceProvider.GetRequiredService(contributor);
                await import.ExecuteAsync(eventData.Id);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(
                $"导入异常，内容:{_jsonSerializer.Serialize(eventData)},异常:{e.Message}.{e.StackTrace}"
            );
            using var uow = _unitOfWorkManager.Begin();
            await _importRecordManager.UpdateStatusAsync(
                eventData.Id,
                ImportStatus.Failed,
                remark: $"导入异常:{e.Message}"
            );
            await uow.CompleteAsync();
        }
    }
}
