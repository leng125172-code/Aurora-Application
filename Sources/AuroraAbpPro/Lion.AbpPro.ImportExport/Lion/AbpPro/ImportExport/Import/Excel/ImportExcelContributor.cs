namespace Lion.AbpPro.ImportExport.Import.Excel;

/// <summary>
/// 同步导入贡献者
/// </summary>
/// <typeparam name="TExcelTemplate"></typeparam>
public abstract class ImportExcelContributor<TExcelTemplate>
    : IImportExcelContributor,
        ITransientDependency
    where TExcelTemplate : class, new()
{
    public IAbpLazyServiceProvider LazyServiceProvider { get; set; } = null!;
    protected ILogger Logger =>
        LazyServiceProvider.LazyGetRequiredService<
            ILogger<ImportExcelContributor<TExcelTemplate>>
        >();
    protected IBlobContainer BlobContainer =>
        LazyServiceProvider.LazyGetRequiredService<IBlobContainer<AbpProFileManagementContainer>>();
    protected IExcelImporter ExcelImporter =>
        LazyServiceProvider.LazyGetRequiredService<IExcelImporter>();
    protected IImportRecordManager ImportManager =>
        LazyServiceProvider.LazyGetRequiredService<IImportRecordManager>();
    protected IGuidGenerator GuidGenerator =>
        LazyServiceProvider.LazyGetRequiredService<IGuidGenerator>();
    protected IUnitOfWorkManager UnitOfWorkManager =>
        LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>();
    protected IMessageManager MessageManager =>
        LazyServiceProvider.LazyGetRequiredService<IMessageManager>();
    protected IdentityUserManager IdentityUserManager =>
        LazyServiceProvider.LazyGetRequiredService<IdentityUserManager>();

    public virtual string Name => string.Empty;

    [UnitOfWork]
    public virtual async Task ExecuteAsync(Guid id)
    {
        // 获取导入记录
        var importRecord = await ImportManager.FindAsync(id);

        if (importRecord == null)
        {
            Logger.LogWarning($"{id}导入记录不存在");
            return;
        }

        using (CultureHelper.Use(importRecord.CultureName))
        {
            MemoryStream backStream = null;
            try
            {
                // 通过blobId获取文件
                var stream = await ReadBlobContentAsync(importRecord.BlobId);
                backStream = await stream.CloneAsync();

                // 读取excel内容
                var contentResult = await ReadExcelContentAsync(id, importRecord.BlobName, stream);
                if (!contentResult.Success)
                {
                    if (importRecord.CreatorId.HasValue)
                    {
                        await NotificationAsync(
                            importRecord.BlobName,
                            false,
                            importRecord.CreatorId.Value,
                            importRecord.CreatorId.Value,
                            importRecord.TenantId
                        );
                    }

                    return;
                }

                // 执行导入逻辑
                var importResult = await ImportAsync(id, contentResult.Data);
                if (importResult.Success)
                {
                    await ImportManager.UpdateStatusAsync(id, ImportStatus.Success, "导入成功");
                }
                else
                {
                    if (importResult.RowErrors.Any())
                    {
                        var errorFileId = GuidGenerator.Create().ToString();
                        //using var newStream = new MemoryStream(backStream);
                        ExcelImporter.OutputBussinessErrorData<TExcelTemplate>(
                            backStream,
                            importResult.RowErrors,
                            out var errorBytes
                        );
                        await BlobContainer.SaveAsync(errorFileId, errorBytes);
                        await ImportManager.UpdateStatusAsync(
                            id,
                            ImportStatus.Failed,
                            errorFileId,
                            $"异常{importRecord.BlobName}",
                            "导入业务数据存在问题,请查看错误文件"
                        );
                    }
                    else
                    {
                        await ImportManager.UpdateStatusAsync(
                            id,
                            ImportStatus.Failed,
                            remark: "导入失败"
                        );
                    }
                }

                if (importRecord.CreatorId.HasValue)
                {
                    await NotificationAsync(
                        importRecord.BlobName,
                        importResult.Success,
                        importRecord.CreatorId.Value,
                        importRecord.CreatorId.Value,
                        importRecord.TenantId
                    );
                }
            }
            catch (Exception e)
            {
                Logger.LogError($"导入异常，导入Id:{id},异常:{e.Message}.{e.StackTrace}");
                using var uow = UnitOfWorkManager.Begin();
                await ImportManager.UpdateStatusAsync(
                    importRecord.Id,
                    ImportStatus.Failed,
                    remark: $"导入异常:{e.Message}"
                );
                await uow.CompleteAsync();
            }
            finally
            {
                if (backStream != null)
                {
                    await backStream.DisposeAsync();
                }
            }
        }
    }

    /// <summary>
    /// 导入逻辑执行
    /// </summary>
    /// <param name="id">导入记录id</param>
    /// <param name="list">导入数据</param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    protected virtual Task<ImportExportResult> ImportAsync(
        Guid id,
        ICollection<TExcelTemplate> list
    )
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// 获取导入文件
    /// </summary>
    /// <param name="name">文件名称</param>
    /// <returns></returns>
    /// <exception cref="UserFriendlyException"></exception>
    protected virtual async Task<Stream> ReadBlobContentAsync(string name)
    {
        var stream = await BlobContainer.GetAsync(name);
        if (stream == null)
        {
            throw new UserFriendlyException($"文件不存在");
        }

        return stream;
    }

    /// <summary>
    /// 获取excel内容
    /// </summary>
    /// <param name="id">导入记录id</param>
    /// <param name="fileName">文件名称</param>
    /// <param name="stream">文件流</param>
    protected virtual async Task<(
        bool Success,
        ICollection<TExcelTemplate> Data
    )> ReadExcelContentAsync(Guid id, string fileName, Stream stream)
    {
        using var labelingFileStream = new MemoryStream();
        var result = await ExcelImporter.Import<TExcelTemplate>(stream, labelingFileStream);

        // 模板有问题
        if (result.HasError)
        {
            var errorMessage = string.Empty;
            //模板有问题
            if (result.TemplateErrors?.Any() == true)
            {
                errorMessage += string.Join(
                    ";",
                    result
                        .TemplateErrors.GroupBy(p => p.Message)
                        .Select(p =>
                            $"{p.Key}:({string.Join(";", p.Select(pp => pp.RequireColumnName))})"
                        )
                );
                await ImportManager.UpdateStatusAsync(
                    id,
                    ImportStatus.Failed,
                    remark: errorMessage
                );
                return (false, new List<TExcelTemplate>());
            }

            if (result.RowErrors?.Any() == true)
            {
                var errorFileId = GuidGenerator.Create().ToString();
                errorMessage += "导入数据存在问题,请查看错误文件";
                await BlobContainer.SaveAsync(errorFileId, labelingFileStream.GetBuffer());
                await ImportManager.UpdateStatusAsync(
                    id,
                    ImportStatus.Failed,
                    blobErrorId: errorFileId,
                    $"异常{fileName}",
                    errorMessage
                );
            }

            return (false, new List<TExcelTemplate>());
        }

        if (!result.Data.Any())
        {
            await ImportManager.UpdateStatusAsync(
                id,
                ImportStatus.Failed,
                remark: "导入数据内容为空"
            );
            return (false, new List<TExcelTemplate>());
        }

        return (true, result.Data);
    }

    protected virtual async Task NotificationAsync(
        string fileName,
        bool success,
        Guid senderUserId,
        Guid receiverUserId,
        Guid? tenantId
    )
    {
        var sendUser = await IdentityUserManager.FindByIdAsync(senderUserId.ToString());
        var sendUserName = sendUser?.UserName ?? string.Empty;
        var receiverUser = await IdentityUserManager.FindByIdAsync(receiverUserId.ToString());
        var receiverUserName = receiverUser?.UserName ?? string.Empty;
        await MessageManager.SendMessageAsync(
            "导入通知",
            success ? $"{fileName}导入成功" : $"{fileName}导入失败",
            MessageType.Common,
            success ? MessageLevel.Information : MessageLevel.Error,
            senderUserId,
            sendUserName,
            receiverUserId,
            receiverUserName,
            tenantId
        );
    }

    /// <summary>
    /// 获取导入模板
    /// </summary>
    public virtual async Task<ExcelTemplateResult> GetTemplateAsync()
    {
        var templateBytes = await ExcelImporter.GenerateTemplateBytes<TExcelTemplate>();
        return new ExcelTemplateResult()
        {
            TemplateUrl = string.Empty,
            TemplateBytes = templateBytes,
        };
    }
}
