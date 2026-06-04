using System.ComponentModel;
using System.Diagnostics;
using AuroraStruct3D.DeviceState;
using AuroraStruct3D.ProductModels.Dtos;
using AuroraStruct3D.ProductModels.Jobs;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace AuroraStruct3D.ProductModels;

/// <summary>
/// 产品三维数模应用服务实现。
/// 提供上传、重命名、删除、下载、转换重试等操作，
/// 所有操作均由 ABP 审计日志自动记录（创建人/时间、修改人/时间等）。
/// </summary>
[Authorize(ProductModelPermissions.Default)]
public class ProductModelAppService : AuroraStruct3DAppService, IProductModelAppService
{
    private readonly IProductModelRepository _productModelRepository;
    private readonly IProductModelOperationLogRepository _operationLogRepository;
    private readonly IBlobContainer<ProductModelBlobContainer> _blobContainer;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IIdentityUserRepository _identityUserRepository;
    private readonly IDeviceStateManager _deviceStateManager;

    public ProductModelAppService(
        IProductModelRepository productModelRepository,
        IProductModelOperationLogRepository operationLogRepository,
        IBlobContainer<ProductModelBlobContainer> blobContainer,
        IBackgroundJobClient backgroundJobClient,
        IIdentityUserRepository identityUserRepository,
        IDeviceStateManager deviceStateManager
    )
    {
        _productModelRepository = productModelRepository;
        _operationLogRepository = operationLogRepository;
        _blobContainer = blobContainer;
        _backgroundJobClient = backgroundJobClient;
        _identityUserRepository = identityUserRepository;
        _deviceStateManager = deviceStateManager;
    }

    // ─────────────────────────── 查询 ───────────────────────────

    /// <inheritdoc/>
    public async Task<PagedResultDto<ProductModelDto>> GetListAsync(GetProductModelListInput input)
    {
        int totalCount = await _productModelRepository.GetCountAsync(
            filter: input.Filter,
            fileFormat: input.FileFormat,
            conversionStatus: input.ConversionStatus,
            startTime: input.StartTime,
            endTime: input.EndTime,
            uploaderUserId: input.UploaderUserId
        );

        List<ProductModel> models = await _productModelRepository.GetListAsync(
            filter: input.Filter,
            fileFormat: input.FileFormat,
            conversionStatus: input.ConversionStatus,
            startTime: input.StartTime,
            endTime: input.EndTime,
            uploaderUserId: input.UploaderUserId,
            skipCount: input.SkipCount,
            maxResultCount: input.MaxResultCount,
            sorting: input.Sorting
        );

        List<ProductModelDto> dtos = await MapToDtoListAsync(models);

        return new PagedResultDto<ProductModelDto>(totalCount, dtos);
    }

    /// <inheritdoc/>
    public async Task<ProductModelDto> GetAsync(Guid id)
    {
        ProductModel productModel = await _productModelRepository.GetAsync(id);
        return await MapToDtoAsync(productModel);
    }

    /// <inheritdoc/>
    public async Task<PagedResultDto<ProductModelOperationLogDto>> GetLogsAsync(
        GetProductModelLogListInput input
    )
    {
        long totalCount = await _operationLogRepository.GetCountAsync(
            input.ProductModelId,
            input.Filter,
            input.OperationType,
            input.IsFailedOnly ?? false,
            input.StartTime,
            input.EndTime
        );

        List<ProductModelOperationLog> logs = await _operationLogRepository.GetPagedListAsync(
            input.ProductModelId,
            input.Filter,
            input.OperationType,
            input.IsFailedOnly ?? false,
            input.StartTime,
            input.EndTime,
            input.SkipCount,
            input.MaxResultCount
        );

        return new PagedResultDto<ProductModelOperationLogDto>(
            totalCount,
            logs.Select(x => new ProductModelOperationLogDto
                {
                    Id = x.Id,
                    ProductModelId = x.ProductModelId,
                    ModelName = x.ModelName,
                    OriginalFileName = x.OriginalFileName,
                    OperationType = x.OperationType,
                    OccurredAt = x.OccurredAt,
                    IsSuccess = x.IsSuccess,
                    ParameterSummary = x.ParameterSummary,
                    ErrorMessage = x.ErrorMessage,
                    DurationMs = x.DurationMs,
                })
                .ToList()
        );
    }

    // ─────────────────────────── 上传 ───────────────────────────

    /// <inheritdoc/>
    [Authorize(ProductModelPermissions.Upload)]
    public async Task<ProductModelDto> UploadAsync(IRemoteStreamContent file, string? name)
    {
        Check.NotNull(file, nameof(file));

        Stopwatch stopwatch = Stopwatch.StartNew();
        ProductModel? productModel = null;
        string originalFileName = file.FileName ?? "unknown";
        string displayName = string.IsNullOrWhiteSpace(name)
            ? Path.GetFileNameWithoutExtension(originalFileName)
                + "_"
                + Clock.Now.ToString("yyyyMMddHHmmss")
            : name.Trim();

        try
        {
            ProductModelFormat format = DetectFormat(originalFileName);

            Guid id = GuidGenerator.Create();
            string originalBlobName =
                $"original/{id:N}{Path.GetExtension(originalFileName).ToLowerInvariant()}";

            await using (Stream stream = file.GetStream())
            {
                await _blobContainer.SaveAsync(originalBlobName, stream, overrideExisting: false);
            }

            long fileSizeBytes = file.ContentLength ?? 0;
            productModel = ProductModel.Create(
                id,
                displayName,
                originalFileName,
                format,
                fileSizeBytes,
                originalBlobName
            );

            await _productModelRepository.InsertAsync(productModel, autoSave: true);

            bool needsConversion = ProductModelConsts.NeedsConversion(format);
            if (needsConversion)
            {
                Guid capturedId = productModel.Id;
                UnitOfWorkManager.Current?.OnCompleted(() =>
                {
                    _backgroundJobClient.Enqueue<ProductModelConversionJob>(job =>
                        job.ExecuteAsync(capturedId)
                    );
                    return Task.CompletedTask;
                });
            }

            ProductModelDto dto = await MapToDtoAsync(productModel);
            dto.NeedsConversion = needsConversion;

            await TryRecordOperationLogAsync(
                ProductModelOperationLog.Success(
                    GuidGenerator.Create(),
                    productModel.Id,
                    productModel.Name,
                    productModel.OriginalFileName,
                    ProductModelOperationType.Upload,
                    $"格式={GetFormatDisplay(format)}，大小={fileSizeBytes} B，需转换={(needsConversion ? "是" : "否")}",
                    (int)stopwatch.ElapsedMilliseconds
                )
            );

            return dto;
        }
        catch (Exception ex)
        {
            await TryRecordOperationLogAsync(
                ProductModelOperationLog.Failure(
                    GuidGenerator.Create(),
                    productModel?.Id,
                    productModel?.Name ?? displayName,
                    productModel?.OriginalFileName ?? originalFileName,
                    ProductModelOperationType.Upload,
                    ex.Message,
                    null,
                    (int)stopwatch.ElapsedMilliseconds
                )
            );
            throw;
        }
    }

    // ─────────────────────────── 修改名称 ───────────────────────────

    /// <inheritdoc/>
    [Authorize(ProductModelPermissions.Rename)]
    public async Task<ProductModelDto> UpdateNameAsync(Guid id, UpdateProductModelNameInput input)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        ProductModel? productModel = null;
        try
        {
            productModel = await _productModelRepository.GetAsync(id);
            string oldName = productModel.Name;
            productModel.UpdateName(input.Name);
            await _productModelRepository.UpdateAsync(productModel, autoSave: true);

            await TryRecordOperationLogAsync(
                ProductModelOperationLog.Success(
                    GuidGenerator.Create(),
                    productModel.Id,
                    productModel.Name,
                    productModel.OriginalFileName,
                    ProductModelOperationType.Rename,
                    $"旧名称={oldName}，新名称={productModel.Name}",
                    (int)stopwatch.ElapsedMilliseconds
                )
            );

            return await MapToDtoAsync(productModel);
        }
        catch (Exception ex)
        {
            await TryRecordOperationLogAsync(
                ProductModelOperationLog.Failure(
                    GuidGenerator.Create(),
                    productModel?.Id ?? id,
                    productModel?.Name ?? "未命名数模",
                    productModel?.OriginalFileName,
                    ProductModelOperationType.Rename,
                    ex.Message,
                    $"目标名称={input.Name}",
                    (int)stopwatch.ElapsedMilliseconds
                )
            );
            throw;
        }
    }

    // ─────────────────────────── 删除 ───────────────────────────

    /// <inheritdoc/>
    [Authorize(ProductModelPermissions.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        ProductModel? productModel = null;
        try
        {
            EnsureManualOrMaintenanceMode();
            productModel = await _productModelRepository.GetAsync(id);

            await TryDeleteBlobAsync(productModel.OriginalBlobName);

            if (!string.IsNullOrEmpty(productModel.ConvertedBlobName))
            {
                await TryDeleteBlobAsync(productModel.ConvertedBlobName);
            }

            await _productModelRepository.DeleteAsync(productModel, autoSave: true);

            await TryRecordOperationLogAsync(
                ProductModelOperationLog.Success(
                    GuidGenerator.Create(),
                    productModel.Id,
                    productModel.Name,
                    productModel.OriginalFileName,
                    ProductModelOperationType.Delete,
                    $"已删除原始文件={productModel.OriginalBlobName}",
                    (int)stopwatch.ElapsedMilliseconds
                )
            );
        }
        catch (Exception ex)
        {
            await TryRecordOperationLogAsync(
                ProductModelOperationLog.Failure(
                    GuidGenerator.Create(),
                    productModel?.Id ?? id,
                    productModel?.Name ?? "未命名数模",
                    productModel?.OriginalFileName,
                    ProductModelOperationType.Delete,
                    ex.Message,
                    null,
                    (int)stopwatch.ElapsedMilliseconds
                )
            );
            throw;
        }
    }

    // ─────────────────────────── 转换重试 ───────────────────────────

    /// <inheritdoc/>
    [Authorize(ProductModelPermissions.RetryConversion)]
    public async Task RetryConversionAsync(Guid id)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        ProductModel? productModel = null;
        try
        {
            productModel = await _productModelRepository.GetAsync(id);

            productModel.ResetForRetry();
            await _productModelRepository.UpdateAsync(productModel, autoSave: true);

            _backgroundJobClient.Enqueue<ProductModelConversionJob>(job =>
                job.ExecuteAsync(productModel.Id)
            );

            await TryRecordOperationLogAsync(
                ProductModelOperationLog.Success(
                    GuidGenerator.Create(),
                    productModel.Id,
                    productModel.Name,
                    productModel.OriginalFileName,
                    ProductModelOperationType.RetryConversion,
                    "已重新提交后台转换任务",
                    (int)stopwatch.ElapsedMilliseconds
                )
            );
        }
        catch (Exception ex)
        {
            await TryRecordOperationLogAsync(
                ProductModelOperationLog.Failure(
                    GuidGenerator.Create(),
                    productModel?.Id ?? id,
                    productModel?.Name ?? "未命名数模",
                    productModel?.OriginalFileName,
                    ProductModelOperationType.RetryConversion,
                    ex.Message,
                    null,
                    (int)stopwatch.ElapsedMilliseconds
                )
            );
            throw;
        }
    }

    // ─────────────────────────── 清理孤立记录 ───────────────────────────

    /// <inheritdoc/>
    [Authorize(ProductModelPermissions.CleanUp)]
    public async Task<int> CleanUpOrphanedRecordsAsync()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            EnsureManualOrMaintenanceMode();

            List<ProductModel> all = await _productModelRepository.GetListAsync(
                maxResultCount: int.MaxValue
            );

            if (all.Count == 0)
            {
                return 0;
            }

            List<ProductModel> orphaned = [];
            foreach (ProductModel model in all)
            {
                bool exists = await ExistsBlobWithCompatAsync(model.OriginalBlobName);
                if (!exists)
                {
                    orphaned.Add(model);
                }
            }

            if (orphaned.Count == all.Count)
            {
                Logger.LogError(
                    "[ProductModelAppService] 清理孤立记录已中止：共 {Total} 条记录，全部判定为原始 BLOB 不存在。请检查 BLOB 存储路径与跨平台路径分隔符。",
                    all.Count
                );

                throw new UserFriendlyException(
                    "检测到全部数模记录均被判定为孤立记录，已自动中止清理以防止误删。请先检查 Linux 服务器上的 BLOB 存储路径与文件可见性。"
                );
            }

            int cleaned = 0;
            foreach (ProductModel model in orphaned)
            {
                await _productModelRepository.DeleteAsync(model, autoSave: true);
                Logger.LogInformation(
                    "[ProductModelAppService] 清理孤立数模记录：{Id}（{Name}），原始 BLOB: {BlobName}",
                    model.Id,
                    model.Name,
                    model.OriginalBlobName
                );
                cleaned++;

                await TryRecordOperationLogAsync(
                    ProductModelOperationLog.Success(
                        GuidGenerator.Create(),
                        model.Id,
                        model.Name,
                        model.OriginalFileName,
                        ProductModelOperationType.CleanUpOrphanedRecords,
                        $"原始 BLOB 缺失：{model.OriginalBlobName}",
                        (int)stopwatch.ElapsedMilliseconds
                    )
                );
            }

            return cleaned;
        }
        catch (Exception ex)
        {
            await TryRecordOperationLogAsync(
                ProductModelOperationLog.Failure(
                    GuidGenerator.Create(),
                    null,
                    "孤立记录清理",
                    null,
                    ProductModelOperationType.CleanUpOrphanedRecords,
                    ex.Message,
                    null,
                    (int)stopwatch.ElapsedMilliseconds
                )
            );
            throw;
        }
    }

    // ─────────────────────────── 下载 ───────────────────────────

    /// <inheritdoc/>
    [Authorize(ProductModelPermissions.Download)]
    public async Task<IRemoteStreamContent> GetDownloadAsync(Guid id)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        ProductModel? productModel = null;
        try
        {
            productModel = await _productModelRepository.GetAsync(id);

            string blobName = productModel.ConvertedBlobName ?? productModel.OriginalBlobName;

            if (string.IsNullOrEmpty(blobName))
            {
                throw new BusinessException("ProductModel:FileNotReady")
                    .WithData("id", id)
                    .WithData("status", productModel.ConversionStatus);
            }

            Stream stream = await GetBlobStreamWithCompatAsync(blobName);
            string fileName = string.IsNullOrEmpty(productModel.ConvertedBlobName)
                ? productModel.OriginalFileName
                : Path.GetFileNameWithoutExtension(productModel.OriginalFileName) + ".ply";

            GetMimeType(fileName, out string contentType);

            await TryRecordOperationLogAsync(
                ProductModelOperationLog.Success(
                    GuidGenerator.Create(),
                    productModel.Id,
                    productModel.Name,
                    productModel.OriginalFileName,
                    ProductModelOperationType.Download,
                    $"下载文件={fileName}",
                    (int)stopwatch.ElapsedMilliseconds
                )
            );

            return new RemoteStreamContent(stream, fileName, contentType);
        }
        catch (Exception ex)
        {
            await TryRecordOperationLogAsync(
                ProductModelOperationLog.Failure(
                    GuidGenerator.Create(),
                    productModel?.Id ?? id,
                    productModel?.Name ?? "未命名数模",
                    productModel?.OriginalFileName,
                    ProductModelOperationType.Download,
                    ex.Message,
                    null,
                    (int)stopwatch.ElapsedMilliseconds
                )
            );
            throw;
        }
    }

    // ─────────────────────────── 私有方法 ───────────────────────────

    /// <summary>
    /// 将实体列表映射为 DTO 列表（批量解析上传人用户名）。
    /// </summary>
    private async Task<List<ProductModelDto>> MapToDtoListAsync(List<ProductModel> models)
    {
        // 收集所有不重复的上传人 ID，批量查询用户名
        List<Guid> uploaderIds = models
            .Where(m => m.CreatorId.HasValue)
            .Select(m => m.CreatorId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, string> userNameMap = [];
        if (uploaderIds.Count > 0)
        {
            List<IdentityUser> users = await _identityUserRepository.GetListByIdsAsync(uploaderIds);
            userNameMap = users.ToDictionary(u => u.Id, u => u.UserName);
        }

        return models.Select(m => MapToDto(m, userNameMap)).ToList();
    }

    /// <summary>
    /// 将单个实体映射为 DTO（同时查询上传人用户名）。
    /// </summary>
    private async Task<ProductModelDto> MapToDtoAsync(ProductModel productModel)
    {
        Dictionary<Guid, string> userNameMap = [];
        if (productModel.CreatorId.HasValue)
        {
            IdentityUser? user = await _identityUserRepository.FindAsync(
                productModel.CreatorId.Value
            );
            if (user is not null)
            {
                userNameMap[user.Id] = user.UserName;
            }
        }

        return MapToDto(productModel, userNameMap);
    }

    /// <summary>
    /// 使用独立工作单元写入操作日志，避免业务事务回滚时丢失记录。
    /// </summary>
    private async Task TryRecordOperationLogAsync(ProductModelOperationLog log)
    {
        try
        {
            using IUnitOfWork unitOfWork = UnitOfWorkManager.Begin(
                requiresNew: true,
                isTransactional: false
            );
            await _operationLogRepository.InsertAsync(log, autoSave: true);
            await unitOfWork.CompleteAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                ex,
                "[ProductModelAppService] 写入三维数模操作日志失败：{Message}",
                ex.Message
            );
        }
    }

    /// <summary>
    /// 将实体映射为 DTO（内联，不查询数据库）。
    /// </summary>
    private static ProductModelDto MapToDto(
        ProductModel productModel,
        Dictionary<Guid, string> userNameMap
    )
    {
        bool isReady =
            productModel.ConversionStatus == ProductModelConversionStatus.NotRequired
            || productModel.ConversionStatus == ProductModelConversionStatus.Success;

        string formatDisplay = GetFormatDisplay(productModel.FileFormat);

        string? uploaderUserName = productModel.CreatorId.HasValue
            ? userNameMap.GetValueOrDefault(productModel.CreatorId.Value)
            : null;

        return new ProductModelDto
        {
            Id = productModel.Id,
            CreationTime = productModel.CreationTime,
            CreatorId = productModel.CreatorId,
            LastModificationTime = productModel.LastModificationTime,
            LastModifierId = productModel.LastModifierId,
            IsDeleted = productModel.IsDeleted,
            DeletionTime = productModel.DeletionTime,
            DeleterId = productModel.DeleterId,
            Name = productModel.Name,
            OriginalFileName = productModel.OriginalFileName,
            FileFormat = productModel.FileFormat,
            FileFormatDisplay = formatDisplay,
            FileSizeBytes = productModel.FileSizeBytes,
            ConversionStatus = productModel.ConversionStatus,
            ConversionErrorMessage = productModel.ConversionErrorMessage,
            IsReady = isReady,
            UploaderUserName = uploaderUserName,
        };
    }

    /// <summary>
    /// 根据文件扩展名推断三维文件格式，不支持的扩展名抛出业务异常。
    /// </summary>
    private static ProductModelFormat DetectFormat(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();

        return ext switch
        {
            ".ply" => ProductModelFormat.PLY,
            ".obj" => ProductModelFormat.OBJ,
            ".step" or ".stp" => ProductModelFormat.STEP,
            ".iges" or ".igs" => ProductModelFormat.IGES,
            ".stl" => ProductModelFormat.STL,
            ".glb" => ProductModelFormat.GLB,
            ".gltf" => ProductModelFormat.GLTF,
            ".pcd" => ProductModelFormat.PCD,
            _ => throw new BusinessException("ProductModel:UnsupportedFormat")
                .WithData("extension", ext)
                .WithData(
                    "supportedFormats",
                    ".ply, .obj, .step, .stp, .iges, .igs, .stl, .glb, .gltf, .pcd"
                ),
        };
    }

    /// <summary>
    /// 获取枚举格式的中文描述（来自 [Description] 特性）。
    /// </summary>
    private static string GetFormatDisplay(ProductModelFormat format)
    {
        System.Reflection.FieldInfo? field = typeof(ProductModelFormat).GetField(format.ToString());
        DescriptionAttribute? attr = field?.GetCustomAttributes(typeof(DescriptionAttribute), false)
            is DescriptionAttribute[] { Length: > 0 } attrs
            ? attrs[0]
            : null;

        return attr?.Description ?? format.ToString();
    }

    /// <summary>
    /// 根据文件名推断 MIME 类型。
    /// </summary>
    private static void GetMimeType(string fileName, out string contentType)
    {
        FileExtensionContentTypeProvider provider = new();
        if (!provider.TryGetContentType(fileName, out string? found))
        {
            found = "application/octet-stream";
        }

        contentType = found;
    }

    /// <summary>
    /// 校验当前运行模式必须为手动或检修，否则抛出业务异常。
    /// </summary>
    private void EnsureManualOrMaintenanceMode()
    {
        DeviceRunMode mode = _deviceStateManager.RunMode;
        if (mode is not (DeviceRunMode.Manual or DeviceRunMode.Maintenance))
        {
            throw new UserFriendlyException(
                $"当前运行模式为【{mode switch {
                    DeviceRunMode.Online => "联机",
                    DeviceRunMode.Auto   => "自动",
                    _                    => mode.ToString()
                }}】，数模管理仅允许在手动模式或检修模式下执行"
            );
        }
    }

    /// <summary>
    /// 尝试删除 BLOB 文件，若文件不存在则静默忽略。
    /// </summary>
    private async Task TryDeleteBlobAsync(string blobName)
    {
        HashSet<string> tried = [];
        foreach (string candidate in GetBlobNameCandidates(blobName))
        {
            if (!tried.Add(candidate))
            {
                continue;
            }

            try
            {
                await _blobContainer.DeleteAsync(candidate);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    ex,
                    "[ProductModelAppService] 删除 BLOB 文件 {BlobName} 失败（可能已不存在）：{Message}",
                    candidate,
                    ex.Message
                );
            }
        }
    }

    /// <summary>
    /// 兼容跨平台路径分隔符，生成 BLOB 键名候选列表。
    /// </summary>
    private static IEnumerable<string> GetBlobNameCandidates(string blobName)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            yield break;
        }

        yield return blobName;

        string normalized = NormalizeBlobName(blobName);
        if (!string.Equals(blobName, normalized, StringComparison.Ordinal))
        {
            yield return normalized;
        }
    }

    /// <summary>
    /// 统一 BLOB 键名格式：反斜杠改为正斜杠，并去除前导斜杠。
    /// </summary>
    private static string NormalizeBlobName(string blobName)
    {
        return blobName.Replace('\\', '/').TrimStart('/');
    }

    /// <summary>
    /// 兼容跨平台路径分隔符进行 BLOB 存在性检查。
    /// </summary>
    private async Task<bool> ExistsBlobWithCompatAsync(string blobName)
    {
        foreach (string candidate in GetBlobNameCandidates(blobName))
        {
            if (await _blobContainer.ExistsAsync(candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 兼容跨平台路径分隔符读取 BLOB 流。
    /// </summary>
    private async Task<Stream> GetBlobStreamWithCompatAsync(string blobName)
    {
        foreach (string candidate in GetBlobNameCandidates(blobName))
        {
            if (await _blobContainer.ExistsAsync(candidate))
            {
                return await _blobContainer.GetAsync(candidate);
            }
        }

        throw new BusinessException("ProductModel:FileNotFound").WithData("blobName", blobName);
    }
}
