using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using AuroraStruct3D.AI.Dtos;
using AuroraStruct3D.AI.Jobs;
using AuroraStruct3D.BlobStoring;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型应用服务实现。
/// </summary>
[Authorize]
public class AiModelAppService : AuroraStruct3DAppService, IAiModelAppService
{
    private readonly IAiModelRepository _aiModelRepository;
    private readonly IAiModelFileRepository _aiModelFileRepository;
    private readonly IAiModelIdentifierRepository _identifierRepository;
    private readonly IAiModelIdentifierLinkRepository _identifierLinkRepository;
    private readonly IAiModelOperationLogRepository _operationLogRepository;
    private readonly IBlobContainer<AiModelBlobContainer> _blobContainer;
    private readonly FileSystemBlobContainerMaintenanceService _blobMaintenanceService;
    private readonly IIdentityUserRepository _identityUserRepository;
    private readonly IAiRuntimeService _aiRuntimeService;
    private readonly IAiModelConversionService _aiModelConversionService;
    private readonly IAiModelConversionNotifier _aiModelConversionNotifier;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IAiModelUploadSessionManager _uploadSessionManager;

    public AiModelAppService(
        IAiModelRepository aiModelRepository,
        IAiModelFileRepository aiModelFileRepository,
        IAiModelIdentifierRepository identifierRepository,
        IAiModelIdentifierLinkRepository identifierLinkRepository,
        IAiModelOperationLogRepository operationLogRepository,
        IBlobContainer<AiModelBlobContainer> blobContainer,
        FileSystemBlobContainerMaintenanceService blobMaintenanceService,
        IIdentityUserRepository identityUserRepository,
        IAiRuntimeService aiRuntimeService,
        IAiModelConversionService aiModelConversionService,
        IAiModelConversionNotifier aiModelConversionNotifier,
        IBackgroundJobClient backgroundJobClient,
        IAiModelUploadSessionManager uploadSessionManager
    )
    {
        _aiModelRepository = aiModelRepository;
        _aiModelFileRepository = aiModelFileRepository;
        _identifierRepository = identifierRepository;
        _identifierLinkRepository = identifierLinkRepository;
        _operationLogRepository = operationLogRepository;
        _blobContainer = blobContainer;
        _blobMaintenanceService = blobMaintenanceService;
        _identityUserRepository = identityUserRepository;
        _aiRuntimeService = aiRuntimeService;
        _aiModelConversionService = aiModelConversionService;
        _aiModelConversionNotifier = aiModelConversionNotifier;
        _backgroundJobClient = backgroundJobClient;
        _uploadSessionManager = uploadSessionManager;
    }

    /// <inheritdoc/>
    public async Task<AiRuntimePlatformInfoDto> GetRuntimePlatformInfoAsync()
    {
        AiRuntimePlatformInfo info = await _aiRuntimeService.GetPlatformInfoAsync();
        return new AiRuntimePlatformInfoDto
        {
            IsSupported = info.IsSupported,
            OperatingSystem = info.OperatingSystem,
            Architecture = info.Architecture,
            PlatformName = info.PlatformName,
            UnsupportedReason = info.UnsupportedReason,
            CompatibilityDescription = info.CompatibilityDescription,
            SupportedExtensions = info.SupportedExtensions.ToList(),
            SupportsOnnxConversion = info.SupportsOnnxConversion,
        };
    }

    /// <inheritdoc/>
    public async Task<List<AiModelIdentifierDto>> GetIdentifierLookupAsync(string? filter = null)
    {
        List<AiModelIdentifier> identifiers = await _identifierRepository.GetListAsync(filter);
        return identifiers
            .Select(x => new AiModelIdentifierDto { Id = x.Id, Name = x.Name })
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<AiModelFileMd5CheckResultDto> CheckFileMd5Async(
        CheckAiModelFileMd5Input input
    )
    {
        string normalizedMd5 = NormalizeMd5(input.Md5);
        AiModel? existing = await _aiModelRepository.FindByMd5Async(normalizedMd5);
        AiModelFile? existingFile = existing is null
            ? null
            : (await _aiModelFileRepository.GetListByModelIdAsync(existing.Id)).FirstOrDefault(x =>
                x.Md5 == normalizedMd5
            );

        return new AiModelFileMd5CheckResultDto
        {
            Md5 = normalizedMd5,
            Exists = existing is not null,
            ExistingModelId = existing?.Id,
            ExistingModelName = existing?.Name,
            ExistingOriginalFileName = existingFile?.OriginalFileName,
        };
    }

    /// <inheritdoc/>
    public async Task<PagedResultDto<AiModelDto>> GetListAsync(GetAiModelListInput input)
    {
        if (input.LoadStatus.HasValue)
        {
            List<AiModel> allModels = await _aiModelRepository.GetListAsync(
                input.Filter,
                input.StartTime,
                input.EndTime,
                input.CreatorId,
                input.LocationKey,
                input.GenerationCondition,
                input.ConversionPreference,
                input.ResolvedConversionType,
                input.FileRole,
                input.FileFormat,
                0,
                int.MaxValue,
                input.Sorting
            );

            Dictionary<Guid, AiModelLoadStatus> loadStatuses = await ResolveLoadStatusesAsync(
                allModels
            );
            List<AiModel> filteredModels = allModels
                .Where(x =>
                    loadStatuses.GetValueOrDefault(x.Id, AiModelLoadStatus.Unloaded)
                    == input.LoadStatus.Value
                )
                .Skip(input.SkipCount)
                .Take(input.MaxResultCount)
                .ToList();

            List<AiModelDto> filteredDtos = await MapToDtoListAsync(filteredModels, loadStatuses);
            int filteredCount = allModels.Count(x =>
                loadStatuses.GetValueOrDefault(x.Id, AiModelLoadStatus.Unloaded)
                == input.LoadStatus.Value
            );
            return new PagedResultDto<AiModelDto>(filteredCount, filteredDtos);
        }

        int totalCount = await _aiModelRepository.GetCountAsync(
            input.Filter,
            input.StartTime,
            input.EndTime,
            input.CreatorId,
            input.LocationKey,
            input.GenerationCondition,
            input.ConversionPreference,
            input.ResolvedConversionType,
            input.FileRole,
            input.FileFormat
        );

        List<AiModel> models = await _aiModelRepository.GetListAsync(
            input.Filter,
            input.StartTime,
            input.EndTime,
            input.CreatorId,
            input.LocationKey,
            input.GenerationCondition,
            input.ConversionPreference,
            input.ResolvedConversionType,
            input.FileRole,
            input.FileFormat,
            input.SkipCount,
            input.MaxResultCount,
            input.Sorting
        );

        List<AiModelDto> dtos = await MapToDtoListAsync(models);
        return new PagedResultDto<AiModelDto>(totalCount, dtos);
    }

    /// <inheritdoc/>
    public async Task<AiModelDto> GetAsync(Guid id)
    {
        AiModel model = await _aiModelRepository.GetAsync(id);
        return await MapToDtoAsync(model);
    }

    /// <inheritdoc/>
    public async Task<PagedResultDto<AiModelOperationLogDto>> GetLogsAsync(
        GetAiModelLogListInput input
    )
    {
        long totalCount = await _operationLogRepository.GetCountAsync(
            input.AiModelId,
            input.Filter,
            input.OperationType,
            input.IsFailedOnly ?? false,
            input.StartTime,
            input.EndTime
        );

        List<AiModelOperationLog> logs = await _operationLogRepository.GetPagedListAsync(
            input.AiModelId,
            input.Filter,
            input.OperationType,
            input.IsFailedOnly ?? false,
            input.StartTime,
            input.EndTime,
            input.SkipCount,
            input.MaxResultCount
        );

        return new PagedResultDto<AiModelOperationLogDto>(
            totalCount,
            logs.Select(x => new AiModelOperationLogDto
                {
                    Id = x.Id,
                    AiModelId = x.AiModelId,
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

    /// <inheritdoc/>
    public async Task<AiModelDto> UploadAsync(IRemoteStreamContent file, UploadAiModelInput input)
    {
        Check.NotNull(file, nameof(file));

        Stopwatch stopwatch = Stopwatch.StartNew();
        AiModel? model = null;
        List<AiModelFile> files = [];
        List<AiModelIdentifier> identifiers = [];
        string? tempFilePath = null;
        string originalFileName = file.FileName ?? "unknown";
        string displayName = Path.GetFileNameWithoutExtension(originalFileName);
        string md5 = string.Empty;
        AiRuntimePlatformInfo? platformInfo = null;

        try
        {
            platformInfo = await EnsureUploadSupportedAsync(originalFileName);

            await using (Stream stream = file.GetStream())
            {
                (tempFilePath, md5, long fileSizeBytes) = await CopyToTempFileAndComputeMd5Async(
                    stream
                );
                (model, files, identifiers) = await SaveUploadedModelAsync(
                    input,
                    platformInfo,
                    [
                        new PendingUploadedFile(
                            Guid.Empty,
                            tempFilePath,
                            originalFileName,
                            md5,
                            fileSizeBytes,
                            AiModelFileRole.SingleWholeModel,
                            0
                        ),
                    ]
                );
            }

            await TryRecordOperationLogAsync(
                AiModelOperationLog.Success(
                    GuidGenerator.Create(),
                    model!.Id,
                    model.Name,
                    PickPrimaryFile(files)?.OriginalFileName,
                    AiModelOperationType.Upload,
                    BuildUploadSummary(model, files, identifiers, platformInfo),
                    (int)stopwatch.ElapsedMilliseconds
                )
            );

            return await MapToDtoAsync(model!);
        }
        catch (Exception ex)
        {
            await TryRecordOperationLogAsync(
                AiModelOperationLog.Failure(
                    GuidGenerator.Create(),
                    model?.Id,
                    model?.Name ?? displayName,
                    PickPrimaryFile(files)?.OriginalFileName ?? originalFileName,
                    AiModelOperationType.Upload,
                    ex.Message,
                    string.IsNullOrWhiteSpace(md5) ? null : $"MD5={md5}",
                    (int)stopwatch.ElapsedMilliseconds
                )
            );

            throw;
        }
        finally
        {
            DeleteTempFileQuietly(tempFilePath);
        }
    }

    /// <inheritdoc/>
    public async Task<InitializeAiModelUploadResultDto> InitializeUploadAsync(
        InitializeAiModelUploadInput input
    )
    {
        Check.NotNull(input, nameof(input));

        string originalFileName = (input.OriginalFileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw new UserFriendlyException("原始文件名不能为空。");
        }

        _ = await EnsureUploadSupportedAsync(originalFileName);

        string md5 = NormalizeMd5(input.Md5);
        AiModel? duplicated = await _aiModelRepository.FindByMd5Async(md5);
        if (duplicated is not null)
        {
            throw new UserFriendlyException($"检测到重复上传，已有模型：{duplicated.Name}。");
        }

        AiModelUploadSessionSnapshot session = await _uploadSessionManager.CreateAsync(
            Path.GetFileName(originalFileName),
            md5,
            input.FileSizeBytes,
            input.ChunkSizeBytes,
            input.TotalChunks
        );

        return new InitializeAiModelUploadResultDto
        {
            SessionId = session.SessionId,
            OriginalFileName = session.OriginalFileName,
            FileSizeBytes = session.FileSizeBytes,
            ChunkSizeBytes = session.ChunkSizeBytes,
            TotalChunks = session.TotalChunks,
            UploadedChunks = session.UploadedChunks,
            UploadedBytes = session.UploadedBytes,
        };
    }

    /// <inheritdoc/>
    public async Task<AiModelUploadChunkResultDto> UploadChunkAsync(
        Guid sessionId,
        int chunkIndex,
        IRemoteStreamContent chunk
    )
    {
        Check.NotNull(chunk, nameof(chunk));

        await using Stream stream = chunk.GetStream();
        AiModelUploadAppendResult result = await _uploadSessionManager.AppendChunkAsync(
            sessionId,
            chunkIndex,
            stream
        );

        return new AiModelUploadChunkResultDto
        {
            SessionId = result.Session.SessionId,
            UploadedChunks = result.Session.UploadedChunks,
            TotalChunks = result.Session.TotalChunks,
            UploadedBytes = result.Session.UploadedBytes,
            FileSizeBytes = result.Session.FileSizeBytes,
            NextChunkIndex = result.Session.UploadedChunks,
            Accepted = result.Accepted,
        };
    }

    /// <inheritdoc/>
    public async Task<AiModelUploadSessionStatusDto> GetUploadSessionStatusAsync(Guid sessionId)
    {
        AiModelUploadSessionSnapshot session = await _uploadSessionManager.GetAsync(sessionId);
        return MapToUploadSessionStatusDto(session);
    }

    /// <inheritdoc/>
    public async Task<AiModelUploadSessionStatusDto?> GetFindUploadSessionAsync(
        FindAiModelUploadSessionInput input
    )
    {
        Check.NotNull(input, nameof(input));

        string originalFileName = (input.OriginalFileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw new UserFriendlyException("原始文件名不能为空。");
        }

        string md5 = NormalizeMd5(input.Md5);
        AiModelUploadSessionSnapshot? session = await _uploadSessionManager.FindAsync(
            originalFileName,
            md5,
            input.FileSizeBytes,
            input.ChunkSizeBytes,
            input.TotalChunks
        );

        return session is null ? null : MapToUploadSessionStatusDto(session);
    }

    /// <inheritdoc/>
    public async Task<AiModelDto> CompleteUploadAsync(CompleteAiModelUploadInput input)
    {
        Check.NotNull(input, nameof(input));
        ValidateFileUploadInputs(input.Files);

        Stopwatch stopwatch = Stopwatch.StartNew();
        AiModel? model = null;
        List<AiModelFile> files = [];
        List<AiModelIdentifier> identifiers = [];
        string displayName = input.Name;
        AiRuntimePlatformInfo? platformInfo = null;

        try
        {
            List<PendingUploadedFile> pendingFiles = [];
            foreach (AiModelUploadFileInput fileInput in input.Files.OrderBy(x => x.SortOrder))
            {
                AiModelUploadSessionSnapshot session = await _uploadSessionManager.GetAsync(
                    fileInput.SessionId
                );
                platformInfo ??= await EnsureUploadSupportedAsync(session.OriginalFileName);

                if (session.UploadedChunks != session.TotalChunks)
                {
                    throw new UserFriendlyException(
                        $"文件 {session.OriginalFileName} 尚未上传完成，请继续上传剩余分片。"
                    );
                }

                if (session.UploadedBytes != session.FileSizeBytes)
                {
                    throw new UserFriendlyException(
                        $"文件 {session.OriginalFileName} 大小不完整，请重新上传。"
                    );
                }

                string md5 = await ComputeFileMd5Async(session.TempFilePath);
                if (!string.Equals(md5, session.Md5, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UserFriendlyException(
                        $"文件 {session.OriginalFileName} 校验失败，MD5 不一致。"
                    );
                }

                if (
                    !string.Equals(
                        md5,
                        NormalizeMd5(fileInput.Md5),
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    throw new UserFriendlyException(
                        $"文件 {session.OriginalFileName} 的提交 MD5 与上传会话不一致。"
                    );
                }

                pendingFiles.Add(
                    new PendingUploadedFile(
                        fileInput.SessionId,
                        session.TempFilePath,
                        session.OriginalFileName,
                        md5,
                        session.FileSizeBytes,
                        fileInput.FileRole,
                        fileInput.SortOrder
                    )
                );
            }

            platformInfo ??= await _aiRuntimeService.GetPlatformInfoAsync();
            (model, files, identifiers) = await SaveUploadedModelAsync(
                input,
                platformInfo,
                pendingFiles
            );

            await TryRecordOperationLogAsync(
                AiModelOperationLog.Success(
                    GuidGenerator.Create(),
                    model.Id,
                    model.Name,
                    PickPrimaryFile(files)?.OriginalFileName,
                    AiModelOperationType.Upload,
                    BuildUploadSummary(model, files, identifiers, platformInfo),
                    (int)stopwatch.ElapsedMilliseconds
                )
            );

            foreach (AiModelUploadFileInput fileInput in input.Files)
            {
                await _uploadSessionManager.RemoveAsync(fileInput.SessionId);
            }

            return await MapToDtoAsync(model);
        }
        catch (Exception ex)
        {
            await TryRecordOperationLogAsync(
                AiModelOperationLog.Failure(
                    GuidGenerator.Create(),
                    model?.Id,
                    model?.Name ?? displayName,
                    PickPrimaryFile(files)?.OriginalFileName,
                    AiModelOperationType.Upload,
                    ex.Message,
                    $"SessionCount={input.Files.Count}",
                    (int)stopwatch.ElapsedMilliseconds
                )
            );

            throw;
        }
    }

    /// <inheritdoc/>
    public async Task AbortUploadAsync(Guid sessionId)
    {
        await _uploadSessionManager.RemoveAsync(sessionId);
    }

    /// <inheritdoc/>
    public async Task<AiModelDto> UpdateAsync(Guid id, UpdateAiModelInput input)
    {
        Check.NotNull(input, nameof(input));

        Stopwatch stopwatch = Stopwatch.StartNew();
        AiModel? model = null;

        try
        {
            model = await _aiModelRepository.GetAsync(id);
            List<AiModelFile> files = await _aiModelFileRepository.GetListByModelIdAsync(id);
            Dictionary<Guid, AiModelFile> originalFileMap = files
                .Where(x => x.IsOriginalFile)
                .ToDictionary(x => x.Id);
            AiModelResolvedConversionType resolvedConversionType =
                ResolveUpdatedResolvedConversionType(model, input.ConversionPreference);

            model.UpdateMetadata(
                input.Name,
                input.Description,
                input.Version,
                input.LocationKey,
                input.GenerationCondition,
                input.ConversionPreference,
                resolvedConversionType
            );
            await _aiModelRepository.UpdateAsync(model, autoSave: true);

            foreach (AiModelFileUpdateInput fileInput in input.Files)
            {
                if (!originalFileMap.TryGetValue(fileInput.Id, out AiModelFile? file))
                {
                    throw new UserFriendlyException($"未找到可编辑的原始模型文件：{fileInput.Id}");
                }

                file.UpdateFileRole(fileInput.FileRole);
                file.UpdateSortOrder(fileInput.SortOrder);
                await _aiModelFileRepository.UpdateAsync(file, autoSave: true);
            }

            List<AiModelIdentifier> identifiers = await GetOrCreateIdentifiersAsync(
                input.IdentifierNames
            );
            await _identifierLinkRepository.ReplaceAsync(model.Id, identifiers.Select(x => x.Id));

            string summary =
                $"Name={model.Name}; Files={input.Files.Count}; Identifiers={identifiers.Count}; Preference={model.ConversionPreference}; Resolved={model.ResolvedConversionType}";
            await TryRecordOperationLogAsync(
                AiModelOperationLog.Success(
                    GuidGenerator.Create(),
                    model.Id,
                    model.Name,
                    PickPrimaryFile(files)?.OriginalFileName,
                    AiModelOperationType.Update,
                    summary,
                    (int)stopwatch.ElapsedMilliseconds
                )
            );

            return await MapToDtoAsync(model);
        }
        catch (Exception ex)
        {
            await TryRecordOperationLogAsync(
                AiModelOperationLog.Failure(
                    GuidGenerator.Create(),
                    model?.Id ?? id,
                    model?.Name ?? input.Name,
                    null,
                    AiModelOperationType.Update,
                    ex.Message,
                    null,
                    (int)stopwatch.ElapsedMilliseconds
                )
            );

            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<AiModelConversionStartResultDto> StartConversionAsync(Guid id)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        AiModel? model = null;

        try
        {
            model = await _aiModelRepository.GetAsync(id);
            List<AiModelFile> allFiles = await _aiModelFileRepository.GetListByModelIdAsync(id);
            AiRuntimePlatformInfo platformInfo = await _aiRuntimeService.GetPlatformInfoAsync();
            List<AiModelFile> sourceFiles = GetSourceFilesForConversion(allFiles);

            EnsureConversionSupported(model, sourceFiles, platformInfo);

            AiModelResolvedConversionType targetType;
            string message;
            bool canConvert;

            if (model.ConversionPreference == AiModelConversionPreference.Auto)
            {
                AiModelConversionAnalysisResult analysisResult =
                    await _aiModelConversionService.AnalyzeOnnxModelGroupAsync(model, sourceFiles);

                if (model.ResolvedConversionType != analysisResult.ResolvedConversionType)
                {
                    model.UpdateMetadata(
                        model.Name,
                        model.Description,
                        model.Version,
                        model.LocationKey,
                        model.GenerationCondition,
                        model.ConversionPreference,
                        analysisResult.ResolvedConversionType
                    );
                    await _aiModelRepository.UpdateAsync(model, autoSave: true);
                }

                targetType = analysisResult.ResolvedConversionType;
                message = analysisResult.Message;
                canConvert = analysisResult.CanConvert;
            }
            else
            {
                if (model.ConversionPreference == AiModelConversionPreference.ToRkllm)
                {
                    targetType = AiModelResolvedConversionType.ToRkllm;
                    message =
                        "当前版本不支持由 ONNX 直接转换为 RKLLM。若该模型需要 RKLLM/NPU 加速运行，请直接上传对应的 RKLLM 文件；如需生成 RKLLM，请在外部使用原始 HF 结构完成转换。";
                    canConvert = false;
                }
                else
                {
                    targetType = ResolveTargetTypeFromPreference(model.ConversionPreference);
                    message = $"已根据转换偏好直接选择目标类型：{targetType}。";
                    canConvert = targetType == AiModelResolvedConversionType.ToRknn;
                }
            }

            if (!canConvert || targetType == AiModelResolvedConversionType.DirectOnnx)
            {
                await TryRecordOperationLogAsync(
                    AiModelOperationLog.Success(
                        GuidGenerator.Create(),
                        model.Id,
                        model.Name,
                        sourceFiles.FirstOrDefault()?.OriginalFileName,
                        AiModelOperationType.StartConversion,
                        message,
                        (int)stopwatch.ElapsedMilliseconds
                    )
                );

                return new AiModelConversionStartResultDto
                {
                    ModelId = model.Id,
                    ConversionPreference = model.ConversionPreference,
                    ResolvedConversionType = targetType,
                    CanConvert = false,
                    Queued = false,
                    Status = AiModelFileConversionStatus.None,
                    Message = message,
                };
            }

            if (allFiles.Any(x => x.IsConvertedFile && x.ConversionTargetType == targetType))
            {
                string duplicateMessage = $"已存在 {targetType} 转换产物，请先删除后再重新转换。";
                return new AiModelConversionStartResultDto
                {
                    ModelId = model.Id,
                    ConversionPreference = model.ConversionPreference,
                    ResolvedConversionType = targetType,
                    CanConvert = false,
                    Queued = false,
                    Status = AiModelFileConversionStatus.Completed,
                    Message = duplicateMessage,
                };
            }

            bool hasActiveConversion = sourceFiles.Any(sourceFile =>
                sourceFile.ConversionTargetType == targetType
                && (
                    sourceFile.ConversionStatus == AiModelFileConversionStatus.Pending
                    || sourceFile.ConversionStatus == AiModelFileConversionStatus.Converting
                )
            );
            if (hasActiveConversion)
            {
                AiModelFileConversionStatus currentStatus = sourceFiles.Any(sourceFile =>
                    sourceFile.ConversionTargetType == targetType
                    && sourceFile.ConversionStatus == AiModelFileConversionStatus.Converting
                )
                    ? AiModelFileConversionStatus.Converting
                    : AiModelFileConversionStatus.Pending;

                return new AiModelConversionStartResultDto
                {
                    ModelId = model.Id,
                    ConversionPreference = model.ConversionPreference,
                    ResolvedConversionType = targetType,
                    CanConvert = false,
                    Queued = false,
                    Status = currentStatus,
                    Message = $"模型 {targetType} 转换已在进行中，请稍候。",
                };
            }

            foreach (AiModelFile sourceFile in sourceFiles)
            {
                sourceFile.UpdateConversionState(AiModelFileConversionStatus.Pending, targetType);
                await _aiModelFileRepository.UpdateAsync(sourceFile, autoSave: true);
            }

            Guid capturedModelId = model.Id;
            Guid[] capturedSourceFileIds = sourceFiles.Select(x => x.Id).ToArray();
            AiModelConversionStateDto queuedState = CreateConversionStateDto(
                sourceFiles,
                targetType,
                AiModelFileConversionStatus.Pending,
                null,
                DateTime.UtcNow
            );
            UnitOfWorkManager.Current?.OnCompleted(async () =>
            {
                _backgroundJobClient.Enqueue<AiModelConversionJob>(job =>
                    job.ExecuteAsync(
                        new AiModelConversionJobArgs
                        {
                            ModelId = capturedModelId,
                            TargetType = targetType,
                            SourceFileIds = capturedSourceFileIds,
                        }
                    )
                );
                await _aiModelConversionNotifier.NotifyQueuedAsync(queuedState);
            });

            AiModelConversionDispatchResult dispatchResult = new()
            {
                Queued = true,
                Status = AiModelFileConversionStatus.Pending,
                Message = $"模型转换任务已入队，目标类型：{targetType}。",
            };

            await TryRecordOperationLogAsync(
                AiModelOperationLog.Success(
                    GuidGenerator.Create(),
                    model.Id,
                    model.Name,
                    sourceFiles.FirstOrDefault()?.OriginalFileName,
                    AiModelOperationType.StartConversion,
                    dispatchResult.Message,
                    (int)stopwatch.ElapsedMilliseconds
                )
            );

            return new AiModelConversionStartResultDto
            {
                ModelId = model.Id,
                ConversionPreference = model.ConversionPreference,
                ResolvedConversionType = targetType,
                CanConvert = true,
                Queued = dispatchResult.Queued,
                Status = dispatchResult.Status,
                Message = dispatchResult.Message,
            };
        }
        catch (Exception ex)
        {
            await TryRecordOperationLogAsync(
                AiModelOperationLog.Failure(
                    GuidGenerator.Create(),
                    model?.Id ?? id,
                    model?.Name ?? "未命名模型",
                    null,
                    AiModelOperationType.StartConversion,
                    ex.Message,
                    null,
                    (int)stopwatch.ElapsedMilliseconds
                )
            );

            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid id)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        AiModel? model = null;

        try
        {
            model = await _aiModelRepository.GetAsync(id);
            List<AiModelFile> files = await _aiModelFileRepository.GetListByModelIdAsync(id);
            foreach (AiModelFile file in files)
            {
                await _blobContainer.DeleteAsync(file.BlobName);
            }

            await _aiModelFileRepository.DeleteByModelIdAsync(model.Id);
            await _identifierLinkRepository.DeleteByModelIdAsync(model.Id);
            await _aiModelRepository.DeleteAsync(model, autoSave: true);
            await DeleteUnusedIdentifiersAsync();

            await TryRecordOperationLogAsync(
                AiModelOperationLog.Success(
                    GuidGenerator.Create(),
                    model.Id,
                    model.Name,
                    files.FirstOrDefault()?.OriginalFileName,
                    AiModelOperationType.Delete,
                    $"已删除文件数={files.Count}",
                    (int)stopwatch.ElapsedMilliseconds
                )
            );
        }
        catch (Exception ex)
        {
            await TryRecordOperationLogAsync(
                AiModelOperationLog.Failure(
                    GuidGenerator.Create(),
                    model?.Id ?? id,
                    model?.Name ?? "未命名模型",
                    null,
                    AiModelOperationType.Delete,
                    ex.Message,
                    null,
                    (int)stopwatch.ElapsedMilliseconds
                )
            );
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteConvertedFileAsync(Guid id, Guid fileId)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        AiModel? model = null;
        AiModelFile? file = null;

        try
        {
            model = await _aiModelRepository.GetAsync(id);
            file = await _aiModelFileRepository.GetAsync(fileId);

            if (file.AiModelId != model.Id)
            {
                throw new UserFriendlyException("指定文件不属于当前模型。");
            }

            if (!file.IsConvertedFile)
            {
                throw new UserFriendlyException("仅允许删除转换产物文件。");
            }

            await _blobContainer.DeleteAsync(file.BlobName);
            await _aiModelFileRepository.DeleteAsync(file, autoSave: true);

            await TryRecordOperationLogAsync(
                AiModelOperationLog.Success(
                    GuidGenerator.Create(),
                    model.Id,
                    model.Name,
                    file.OriginalFileName,
                    AiModelOperationType.DeleteConvertedFile,
                    $"已删除转换产物：{file.ConversionTargetType}",
                    (int)stopwatch.ElapsedMilliseconds
                )
            );
        }
        catch (Exception ex)
        {
            await TryRecordOperationLogAsync(
                AiModelOperationLog.Failure(
                    GuidGenerator.Create(),
                    model?.Id ?? id,
                    model?.Name ?? "未命名模型",
                    file?.OriginalFileName,
                    AiModelOperationType.DeleteConvertedFile,
                    ex.Message,
                    null,
                    (int)stopwatch.ElapsedMilliseconds
                )
            );
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<AiModelDto> LoadAsync(Guid id)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        AiModel? model = null;

        try
        {
            model = await _aiModelRepository.GetAsync(id);
            bool loaded = await _aiRuntimeService.LoadModelAsync(model);
            if (!loaded)
            {
                throw new UserFriendlyException("AI 模型加载失败，请检查运行时服务配置。");
            }

            await TryRecordOperationLogAsync(
                AiModelOperationLog.Success(
                    GuidGenerator.Create(),
                    model.Id,
                    model.Name,
                    (await _aiModelFileRepository.GetListByModelIdAsync(model.Id))
                        .FirstOrDefault()
                        ?.OriginalFileName,
                    AiModelOperationType.Load,
                    $"提供程序={_aiRuntimeService.GetProviderName()}",
                    (int)stopwatch.ElapsedMilliseconds
                )
            );

            return await MapToDtoAsync(model);
        }
        catch (Exception ex)
        {
            await TryRecordOperationLogAsync(
                AiModelOperationLog.Failure(
                    GuidGenerator.Create(),
                    model?.Id ?? id,
                    model?.Name ?? "未命名模型",
                    null,
                    AiModelOperationType.Load,
                    ex.Message,
                    $"提供程序={_aiRuntimeService.GetProviderName()}",
                    (int)stopwatch.ElapsedMilliseconds
                )
            );
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<AiModelDto> UnloadAsync(Guid id)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        AiModel? model = null;

        try
        {
            model = await _aiModelRepository.GetAsync(id);
            bool unloaded = await _aiRuntimeService.UnloadModelAsync(model);
            if (!unloaded)
            {
                throw new UserFriendlyException("AI 模型卸载失败，请检查运行时服务配置。");
            }

            await TryRecordOperationLogAsync(
                AiModelOperationLog.Success(
                    GuidGenerator.Create(),
                    model.Id,
                    model.Name,
                    (await _aiModelFileRepository.GetListByModelIdAsync(model.Id))
                        .FirstOrDefault()
                        ?.OriginalFileName,
                    AiModelOperationType.Unload,
                    $"提供程序={_aiRuntimeService.GetProviderName()}",
                    (int)stopwatch.ElapsedMilliseconds
                )
            );

            return await MapToDtoAsync(model);
        }
        catch (Exception ex)
        {
            await TryRecordOperationLogAsync(
                AiModelOperationLog.Failure(
                    GuidGenerator.Create(),
                    model?.Id ?? id,
                    model?.Name ?? "未命名模型",
                    null,
                    AiModelOperationType.Unload,
                    ex.Message,
                    $"提供程序={_aiRuntimeService.GetProviderName()}",
                    (int)stopwatch.ElapsedMilliseconds
                )
            );
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<int> CleanUpOrphanedRecordsAsync()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            string containerRootPath =
                _blobMaintenanceService.GetContainerRootPath<AiModelBlobContainer>();
            List<AiModel> all = await _aiModelRepository.GetListAsync(maxResultCount: int.MaxValue);
            Dictionary<Guid, List<AiModelFile>> fileMap = await ResolveFilesAsync(all);
            List<FileSystemBlobFile> initialPhysicalFiles = _blobMaintenanceService.GetBlobFiles(
                containerRootPath
            );
            HashSet<string> initialPhysicalBlobNames = initialPhysicalFiles
                .Select(x => x.BlobName)
                .ToHashSet(StringComparer.Ordinal);

            if (all.Count > 0 && initialPhysicalBlobNames.Count > 0)
            {
                HashSet<string> referencedBlobNames = BuildReferencedBlobNameSet(
                    fileMap.Values.SelectMany(x => x)
                );
                int matchedReferencedFileCount = referencedBlobNames.Count(x =>
                    initialPhysicalBlobNames.Contains(x)
                );

                if (matchedReferencedFileCount == 0)
                {
                    Logger.LogError(
                        "[AiModelAppService] 清理孤立记录已中止：数据库引用与磁盘文件零交集。请检查容器根目录 {RootPath} 是否指向了正确的 BLOB 存储。",
                        containerRootPath
                    );

                    throw new UserFriendlyException(
                        "当前 AI 模型数据库引用与磁盘文件完全不匹配，已自动中止清理以防止误删。请先检查 BLOB 存储路径与挂载目录。"
                    );
                }
            }

            List<AiModel> missingOriginalModels = [];
            List<AiModelFile> missingConvertedFiles = [];
            foreach (AiModel model in all)
            {
                List<AiModelFile> files = fileMap.GetValueOrDefault(model.Id, []);
                List<AiModelFile> originalFiles = files.Where(x => x.IsOriginalFile).ToList();
                if (
                    originalFiles.Count == 0
                    || originalFiles.Any(x =>
                        !ContainsPhysicalBlob(initialPhysicalBlobNames, x.BlobName)
                    )
                )
                {
                    missingOriginalModels.Add(model);
                    continue;
                }

                missingConvertedFiles.AddRange(
                    files.Where(x =>
                        x.IsConvertedFile
                        && !ContainsPhysicalBlob(initialPhysicalBlobNames, x.BlobName)
                    )
                );
            }

            if (all.Count > 0 && missingOriginalModels.Count == all.Count)
            {
                Logger.LogError(
                    "[AiModelAppService] 清理孤立记录已中止：共 {Total} 条记录，全部判定为原始模型文件不存在。请检查 BLOB 存储路径与跨平台路径分隔符。",
                    all.Count
                );

                throw new UserFriendlyException(
                    "检测到全部 AI 模型记录均被判定为孤立记录，已自动中止清理以防止误删。请先检查 BLOB 存储路径与文件可见性。"
                );
            }

            int cleaned = 0;
            HashSet<Guid> deletedModelIds = [];
            foreach (AiModel model in missingOriginalModels)
            {
                List<AiModelFile> files = fileMap.GetValueOrDefault(model.Id, []);
                foreach (AiModelFile file in files)
                {
                    await TryDeleteBlobAsync(file.BlobName);
                }

                await _aiModelFileRepository.DeleteByModelIdAsync(model.Id);
                await _identifierLinkRepository.DeleteByModelIdAsync(model.Id);
                await _aiModelRepository.DeleteAsync(model, autoSave: true);
                deletedModelIds.Add(model.Id);
                cleaned++;

                await TryRecordOperationLogAsync(
                    AiModelOperationLog.Success(
                        GuidGenerator.Create(),
                        model.Id,
                        model.Name,
                        files.FirstOrDefault()?.OriginalFileName,
                        AiModelOperationType.CleanUpOrphanedRecords,
                        $"原始模型文件缺失数={files.Count(x => x.IsOriginalFile)}",
                        (int)stopwatch.ElapsedMilliseconds
                    )
                );
            }

            Dictionary<Guid, AiModelFile> sourceFileMap = fileMap
                .Values.SelectMany(x => x)
                .Where(x => x.IsOriginalFile)
                .ToDictionary(x => x.Id);
            HashSet<Guid> deletedFileIds = [];
            foreach (AiModelFile file in missingConvertedFiles)
            {
                if (deletedModelIds.Contains(file.AiModelId))
                {
                    continue;
                }

                if (
                    file.SourceFileId.HasValue
                    && sourceFileMap.TryGetValue(
                        file.SourceFileId.Value,
                        out AiModelFile? sourceFile
                    )
                )
                {
                    sourceFile.UpdateConversionState(
                        AiModelFileConversionStatus.Failed,
                        sourceFile.ConversionTargetType ?? file.ConversionTargetType,
                        "转换产物文件缺失，已清理损坏记录，请重新执行转换。"
                    );
                    await _aiModelFileRepository.UpdateAsync(sourceFile, autoSave: true);
                }

                await _aiModelFileRepository.DeleteAsync(file, autoSave: true);
                deletedFileIds.Add(file.Id);
                cleaned++;

                AiModel? model = all.FirstOrDefault(x => x.Id == file.AiModelId);
                await TryRecordOperationLogAsync(
                    AiModelOperationLog.Success(
                        GuidGenerator.Create(),
                        file.AiModelId,
                        model?.Name ?? "未命名模型",
                        file.OriginalFileName,
                        AiModelOperationType.CleanUpOrphanedRecords,
                        $"转换产物缺失，已清理文件记录：{file.BlobName}",
                        (int)stopwatch.ElapsedMilliseconds
                    )
                );
            }

            await DeleteUnusedIdentifiersAsync();

            HashSet<string> referencedBlobNamesAfterCleanup = BuildReferencedBlobNameSet(
                fileMap
                    .Where(x => !deletedModelIds.Contains(x.Key))
                    .SelectMany(x => x.Value)
                    .Where(x => !deletedFileIds.Contains(x.Id))
            );

            int orphanFileCount = 0;
            foreach (
                FileSystemBlobFile physicalFile in _blobMaintenanceService.GetBlobFiles(
                    containerRootPath
                )
            )
            {
                if (referencedBlobNamesAfterCleanup.Contains(physicalFile.BlobName))
                {
                    continue;
                }

                File.Delete(physicalFile.FullPath);
                orphanFileCount++;

                Logger.LogInformation(
                    "[AiModelAppService] 已删除 AI 模型容器中的磁盘孤儿文件：{BlobName}",
                    physicalFile.BlobName
                );
            }

            int emptyDirectoryCount = _blobMaintenanceService.DeleteEmptyDirectories(
                containerRootPath
            );

            if (orphanFileCount > 0 || emptyDirectoryCount > 0)
            {
                await TryRecordOperationLogAsync(
                    AiModelOperationLog.Success(
                        GuidGenerator.Create(),
                        null,
                        "孤立记录清理",
                        null,
                        AiModelOperationType.CleanUpOrphanedRecords,
                        $"已删除磁盘孤儿文件={orphanFileCount}，空目录={emptyDirectoryCount}",
                        (int)stopwatch.ElapsedMilliseconds
                    )
                );
            }

            cleaned += orphanFileCount + emptyDirectoryCount;

            return cleaned;
        }
        catch (Exception ex)
        {
            await TryRecordOperationLogAsync(
                AiModelOperationLog.Failure(
                    GuidGenerator.Create(),
                    null,
                    "孤立记录清理",
                    null,
                    AiModelOperationType.CleanUpOrphanedRecords,
                    ex.Message,
                    null,
                    (int)stopwatch.ElapsedMilliseconds
                )
            );
            throw;
        }
    }

    private async Task<List<AiModelDto>> MapToDtoListAsync(List<AiModel> models)
    {
        return await MapToDtoListAsync(models, null);
    }

    private async Task<List<AiModelDto>> MapToDtoListAsync(
        List<AiModel> models,
        Dictionary<Guid, AiModelLoadStatus>? loadStatusesOverride
    )
    {
        Dictionary<Guid, string> userNames = await ResolveUserNamesAsync(models);
        Dictionary<Guid, List<AiModelIdentifierDto>> identifierMap = await ResolveIdentifiersAsync(
            models
        );
        Dictionary<Guid, List<AiModelFileDto>> fileMap = await ResolveFileDtosAsync(models);
        Dictionary<Guid, AiModelLoadStatus> loadStatuses =
            loadStatusesOverride ?? await ResolveLoadStatusesAsync(models);
        return models
            .Select(x => MapToDto(x, userNames, identifierMap, fileMap, loadStatuses))
            .ToList();
    }

    private async Task<AiModelDto> MapToDtoAsync(AiModel model)
    {
        Dictionary<Guid, string> userNames = await ResolveUserNamesAsync([model]);
        Dictionary<Guid, List<AiModelIdentifierDto>> identifierMap = await ResolveIdentifiersAsync([
            model,
        ]);
        Dictionary<Guid, List<AiModelFileDto>> fileMap = await ResolveFileDtosAsync([model]);
        Dictionary<Guid, AiModelLoadStatus> loadStatuses = await ResolveLoadStatusesAsync([model]);
        return MapToDto(model, userNames, identifierMap, fileMap, loadStatuses);
    }

    private static AiModelDto MapToDto(
        AiModel model,
        IReadOnlyDictionary<Guid, string> userNames,
        IReadOnlyDictionary<Guid, List<AiModelIdentifierDto>> identifierMap,
        IReadOnlyDictionary<Guid, List<AiModelFileDto>> fileMap,
        IReadOnlyDictionary<Guid, AiModelLoadStatus> loadStatuses
    )
    {
        List<AiModelFileDto> files = fileMap.GetValueOrDefault(model.Id, []);

        return new AiModelDto
        {
            Id = model.Id,
            Name = model.Name,
            Description = model.Description,
            ModelIdentifiers = identifierMap.TryGetValue(
                model.Id,
                out List<AiModelIdentifierDto>? identifiers
            )
                ? identifiers
                : [],
            Version = model.Version,
            LocationKey = model.LocationKey,
            GenerationCondition = model.GenerationCondition,
            ConversionPreference = model.ConversionPreference,
            ResolvedConversionType = model.ResolvedConversionType,
            FileCount = model.FileCount,
            Files = files,
            LoadStatus = loadStatuses.GetValueOrDefault(model.Id, AiModelLoadStatus.Unloaded),
            CreationTime = model.CreationTime,
            CreatorId = model.CreatorId,
            LastModificationTime = model.LastModificationTime,
            LastModifierId = model.LastModifierId,
            IsDeleted = model.IsDeleted,
            DeleterId = model.DeleterId,
            DeletionTime = model.DeletionTime,
            CreatorUserName =
                model.CreatorId.HasValue
                && userNames.TryGetValue(model.CreatorId.Value, out string? userName)
                    ? userName
                    : null,
        };
    }

    private async Task<Dictionary<Guid, string>> ResolveUserNamesAsync(IEnumerable<AiModel> models)
    {
        List<Guid> userIds = models
            .Where(x => x.CreatorId.HasValue)
            .Select(x => x.CreatorId!.Value)
            .Distinct()
            .ToList();

        if (userIds.Count == 0)
        {
            return [];
        }

        List<IdentityUser> users = await _identityUserRepository.GetListByIdsAsync(userIds);
        return users
            .GroupBy(x => x.Id)
            .ToDictionary(
                x => x.Key,
                x => x.First().UserName ?? x.First().Name ?? x.Key.ToString()
            );
    }

    private async Task<Dictionary<Guid, List<AiModelIdentifierDto>>> ResolveIdentifiersAsync(
        IEnumerable<AiModel> models
    )
    {
        List<Guid> modelIds = models.Select(x => x.Id).Distinct().ToList();
        if (modelIds.Count == 0)
        {
            return [];
        }

        List<AiModelIdentifierLink> links = await _identifierLinkRepository.GetListByModelIdsAsync(
            modelIds
        );
        if (links.Count == 0)
        {
            return [];
        }

        List<Guid> identifierIds = links.Select(x => x.IdentifierId).Distinct().ToList();
        List<AiModelIdentifier> identifiers = await _identifierRepository.GetByIdsAsync(
            identifierIds
        );
        Dictionary<Guid, AiModelIdentifier> identifierMap = identifiers.ToDictionary(x => x.Id);

        return links
            .GroupBy(x => x.AiModelId)
            .ToDictionary(
                x => x.Key,
                x =>
                    x.Where(link => identifierMap.ContainsKey(link.IdentifierId))
                        .Select(link => new AiModelIdentifierDto
                        {
                            Id = link.IdentifierId,
                            Name = identifierMap[link.IdentifierId].Name,
                        })
                        .OrderBy(item => item.Name)
                        .ToList()
            );
    }

    private async Task<Dictionary<Guid, AiModelLoadStatus>> ResolveLoadStatusesAsync(
        IEnumerable<AiModel> models
    )
    {
        List<Guid> modelIds = models.Select(x => x.Id).Distinct().ToList();
        if (modelIds.Count == 0)
        {
            return [];
        }

        return await _aiRuntimeService.GetLoadStatusesAsync(modelIds);
    }

    private async Task<Dictionary<Guid, List<AiModelFile>>> ResolveFilesAsync(
        IEnumerable<AiModel> models
    )
    {
        List<Guid> modelIds = models.Select(x => x.Id).Distinct().ToList();
        if (modelIds.Count == 0)
        {
            return [];
        }

        List<AiModelFile> files = await _aiModelFileRepository.GetListByModelIdsAsync(modelIds);
        return files
            .GroupBy(x => x.AiModelId)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(y => y.SortOrder).ThenBy(y => y.CreationTime).ToList()
            );
    }

    private async Task<Dictionary<Guid, List<AiModelFileDto>>> ResolveFileDtosAsync(
        IEnumerable<AiModel> models
    )
    {
        Dictionary<Guid, List<AiModelFile>> fileMap = await ResolveFilesAsync(models);
        return fileMap.ToDictionary(
            x => x.Key,
            x =>
                x.Value.Select(file => new AiModelFileDto
                    {
                        Id = file.Id,
                        AiModelId = file.AiModelId,
                        IsOriginalFile = file.IsOriginalFile,
                        IsConvertedFile = file.IsConvertedFile,
                        SourceFileId = file.SourceFileId,
                        OriginalFileName = file.OriginalFileName,
                        DisplayName = file.DisplayName,
                        FileFormat = file.FileFormat,
                        FileSizeBytes = file.FileSizeBytes,
                        Md5 = file.Md5,
                        FileRole = file.FileRole,
                        Md5Verified = file.Md5Verified,
                        SortOrder = file.SortOrder,
                        ConversionTargetType = file.ConversionTargetType,
                        ConversionStatus = file.ConversionStatus,
                        ConversionErrorMessage = file.ConversionErrorMessage,
                        ConversionTime = file.ConversionTime,
                        CreationTime = file.CreationTime,
                        CreatorId = file.CreatorId,
                        LastModificationTime = file.LastModificationTime,
                        LastModifierId = file.LastModifierId,
                        IsDeleted = file.IsDeleted,
                        DeleterId = file.DeleterId,
                        DeletionTime = file.DeletionTime,
                    })
                    .ToList()
        );
    }

    private static AiModelConversionStateDto CreateConversionStateDto(
        IEnumerable<AiModelFile> files,
        AiModelResolvedConversionType? targetType,
        AiModelFileConversionStatus status,
        string? errorMessage,
        DateTime? lastUpdatedTime
    )
    {
        List<AiModelFile> orderedFiles = files.OrderBy(file => file.SortOrder).ToList();
        return new AiModelConversionStateDto
        {
            ModelId = orderedFiles[0].AiModelId,
            SourceFileIds = orderedFiles.Select(file => file.Id).ToList(),
            TargetType =
                targetType
                ?? orderedFiles
                    .Select(file => file.ConversionTargetType)
                    .FirstOrDefault(type => type.HasValue),
            Status = status,
            ConversionErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
                ? null
                : errorMessage.Trim(),
            LastUpdatedTime = lastUpdatedTime,
        };
    }

    private async Task<List<AiModelIdentifier>> GetOrCreateIdentifiersAsync(
        IEnumerable<string> identifierNames
    )
    {
        List<string> cleanedNames = identifierNames
            .Select(NormalizeIdentifierInput)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        if (cleanedNames.Count == 0)
        {
            return [];
        }

        List<string> normalizedNames = cleanedNames
            .Select(AiModelIdentifier.NormalizeName)
            .ToList();
        List<AiModelIdentifier> existing = await _identifierRepository.GetByNamesAsync(
            normalizedNames
        );
        Dictionary<string, AiModelIdentifier> existingMap = existing.ToDictionary(
            x => x.NormalizedName,
            StringComparer.OrdinalIgnoreCase
        );

        List<AiModelIdentifier> created = [];
        foreach (string name in cleanedNames)
        {
            string normalizedName = AiModelIdentifier.NormalizeName(name);
            if (existingMap.ContainsKey(normalizedName))
            {
                continue;
            }

            AiModelIdentifier identifier = AiModelIdentifier.Create(GuidGenerator.Create(), name);
            created.Add(identifier);
            existingMap[normalizedName] = identifier;
        }

        if (created.Count > 0)
        {
            await _identifierRepository.InsertManyAsync(created, autoSave: true);
        }

        return cleanedNames
            .Select(name => existingMap[AiModelIdentifier.NormalizeName(name)])
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .ToList();
    }

    private async Task TryRecordOperationLogAsync(AiModelOperationLog log)
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
            Logger.LogWarning(ex, "记录 AI 模型操作日志失败，已忽略。LogId={LogId}", log.Id);
        }
    }

    private async Task DeleteUnusedIdentifiersAsync()
    {
        await _identifierRepository.DeleteUnusedAsync();
    }

    private static string BuildUploadSummary(
        AiModel model,
        IReadOnlyList<AiModelFile> files,
        IReadOnlyList<AiModelIdentifier> identifiers,
        AiRuntimePlatformInfo platformInfo
    )
    {
        string onnxHint =
            files.Any(x => x.FileFormat.Equals("ONNX", StringComparison.OrdinalIgnoreCase))
            && platformInfo.SupportsOnnxConversion
                ? "，ONNX 需后续转换"
                : string.Empty;

        return $"文件数={files.Count}，文件角色={FormatFileRoleSummary(files)}，标识={FormatIdentifierSummary(identifiers)}，位置={model.LocationKey ?? "未设置"}，转换偏好={model.ConversionPreference}，解析结果={model.ResolvedConversionType}{onnxHint}";
    }

    private static List<AiModelFile> GetSourceFilesForConversion(IReadOnlyList<AiModelFile> files)
    {
        return files
            .Where(x => x.IsOriginalFile && !x.IsConvertedFile)
            .OrderBy(x => x.SortOrder)
            .ToList();
    }

    private static AiModelResolvedConversionType ResolveTargetTypeFromPreference(
        AiModelConversionPreference conversionPreference
    )
    {
        return conversionPreference switch
        {
            AiModelConversionPreference.ToRknn => AiModelResolvedConversionType.ToRknn,
            AiModelConversionPreference.DirectOnnx => AiModelResolvedConversionType.DirectOnnx,
            _ => AiModelResolvedConversionType.Unknown,
        };
    }

    private static AiModelResolvedConversionType ResolveInitialResolvedConversionType(
        AiModelConversionPreference conversionPreference
    )
    {
        EnsureWritableConversionPreference(conversionPreference);
        return AiModelResolvedConversionType.Unknown;
    }

    private static AiModelResolvedConversionType ResolveUpdatedResolvedConversionType(
        AiModel model,
        AiModelConversionPreference conversionPreference
    )
    {
        EnsureWritableConversionPreference(conversionPreference);

        return
            conversionPreference == AiModelConversionPreference.Auto
            && model.ConversionPreference == AiModelConversionPreference.Auto
            ? model.ResolvedConversionType
            : AiModelResolvedConversionType.Unknown;
    }

    private static void EnsureWritableConversionPreference(
        AiModelConversionPreference conversionPreference
    )
    {
        if (conversionPreference == AiModelConversionPreference.ToRkllm)
        {
            throw new UserFriendlyException(
                "当前版本不支持将转换偏好设置为 RKLLM。若模型需要 RKLLM/NPU 加速运行，请直接上传对应的 RKLLM 文件。"
            );
        }
    }

    private static void EnsureConversionSupported(
        AiModel model,
        IReadOnlyList<AiModelFile> files,
        AiRuntimePlatformInfo platformInfo
    )
    {
        if (!platformInfo.SupportsOnnxConversion)
        {
            throw new UserFriendlyException(
                "当前平台不支持 ONNX 模型转换，仅 Linux + 瑞芯微平台可用。"
            );
        }

        if (model.ConversionPreference == AiModelConversionPreference.DirectOnnx)
        {
            throw new UserFriendlyException("当前模型配置为直接使用 ONNX，不允许触发转换。");
        }

        if (files.Count == 0)
        {
            throw new UserFriendlyException("未找到可用于转换的原始 ONNX 文件。");
        }

        if (
            files.Any(x => !string.Equals(x.FileFormat, "ONNX", StringComparison.OrdinalIgnoreCase))
        )
        {
            throw new UserFriendlyException("仅支持对 ONNX 文件组发起模型转换。");
        }
    }

    private static string FormatFileRoleSummary(IReadOnlyList<AiModelFile> files)
    {
        return files.Count == 0
            ? "未设置"
            : string.Join(
                "、",
                files.OrderBy(x => x.SortOrder).Select(x => $"{x.FileRole}:{x.DisplayName}")
            );
    }

    private static string FormatIdentifierSummary(IReadOnlyList<AiModelIdentifier> identifiers)
    {
        return identifiers.Count == 0
            ? "未设置"
            : string.Join("、", identifiers.Select(x => x.Name));
    }

    private static string? NormalizeIdentifierInput(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed.Length <= AiModelConsts.MaxIdentifierNameLength
            ? trimmed
            : trimmed[..AiModelConsts.MaxIdentifierNameLength];
    }

    private static string NormalizeMd5(string value)
    {
        string normalized = value.Trim().ToLowerInvariant();
        if (
            normalized.Length != AiModelConsts.MaxMd5Length
            || normalized.Any(x => !Uri.IsHexDigit(x))
        )
        {
            throw new UserFriendlyException("MD5 格式不正确。");
        }

        return normalized;
    }

    private async Task<AiRuntimePlatformInfo> EnsureUploadSupportedAsync(string originalFileName)
    {
        AiRuntimePlatformInfo platformInfo = await _aiRuntimeService.GetPlatformInfoAsync();
        if (!platformInfo.IsSupported)
        {
            throw new UserFriendlyException(
                platformInfo.UnsupportedReason ?? "当前运行平台不支持 AI 模型上传。"
            );
        }

        string extension = Path.GetExtension(originalFileName).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new UserFriendlyException("无法识别模型文件扩展名。请选择受支持的模型文件。");
        }

        if (!platformInfo.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new UserFriendlyException(
                $"当前平台仅支持以下扩展名：{string.Join(", ", platformInfo.SupportedExtensions)}。"
            );
        }

        return platformInfo;
    }

    private async Task<(
        AiModel model,
        List<AiModelFile> files,
        List<AiModelIdentifier> identifiers
    )> SaveUploadedModelAsync(
        UploadAiModelInput input,
        AiRuntimePlatformInfo platformInfo,
        IReadOnlyList<PendingUploadedFile> pendingFiles
    )
    {
        ValidatePendingFiles(pendingFiles);

        foreach (PendingUploadedFile pendingFile in pendingFiles)
        {
            AiModel? duplicated = await _aiModelRepository.FindByMd5Async(pendingFile.Md5);
            if (duplicated is not null)
            {
                AiModelFile? duplicatedFile = (
                    await _aiModelFileRepository.GetListByModelIdAsync(duplicated.Id)
                ).FirstOrDefault(x => x.Md5 == pendingFile.Md5);
                throw new UserFriendlyException(
                    $"检测到重复上传，已有模型：{duplicated.Name}（{duplicatedFile?.OriginalFileName ?? "未知文件"}）。"
                );
            }
        }

        string version = Clock.Now.ToString("yyyyMMddHHmmss");
        Guid id = GuidGenerator.Create();
        AiModel model = AiModel.Create(
            id,
            input.Name,
            input.Description,
            version,
            input.LocationKey,
            input.GenerationCondition,
            input.ConversionPreference,
            ResolveInitialResolvedConversionType(input.ConversionPreference),
            pendingFiles.Count
        );

        await _aiModelRepository.InsertAsync(model, autoSave: true);

        List<AiModelFile> files = [];
        foreach (PendingUploadedFile pendingFile in pendingFiles.OrderBy(x => x.SortOrder))
        {
            string extension = Path.GetExtension(pendingFile.OriginalFileName)
                .TrimStart('.')
                .ToLowerInvariant();
            string displayName = Path.GetFileNameWithoutExtension(pendingFile.OriginalFileName);
            string blobName = BuildBlobName(model.Id, pendingFile);

            await using FileStream blobStream = new(
                pendingFile.TempFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );

            await _blobContainer.SaveAsync(blobName, blobStream, overrideExisting: false);
            files.Add(
                AiModelFile.Create(
                    GuidGenerator.Create(),
                    model.Id,
                    pendingFile.OriginalFileName,
                    displayName,
                    extension.ToUpperInvariant(),
                    pendingFile.FileSizeBytes,
                    pendingFile.Md5,
                    blobName,
                    pendingFile.FileRole,
                    true,
                    pendingFile.SortOrder
                )
            );
        }

        if (files.Count > 0)
        {
            await _aiModelFileRepository.InsertManyAsync(files, autoSave: true);
        }

        List<AiModelIdentifier> identifiers = await GetOrCreateIdentifiersAsync(
            input.IdentifierNames
        );
        await _identifierLinkRepository.ReplaceAsync(model.Id, identifiers.Select(x => x.Id));
        return (model, files, identifiers);
    }

    private static void ValidateFileUploadInputs(IReadOnlyList<AiModelUploadFileInput> files)
    {
        if (files.Count == 0)
        {
            throw new UserFriendlyException("至少需要上传一个模型文件。");
        }

        if (files.Count > AiModelConsts.MaxFileCountPerModel)
        {
            throw new UserFriendlyException(
                $"单个模型最多仅支持 {AiModelConsts.MaxFileCountPerModel} 个文件。"
            );
        }
    }

    private static void ValidatePendingFiles(IReadOnlyList<PendingUploadedFile> files)
    {
        if (files.Count == 1)
        {
            if (files[0].FileRole != AiModelFileRole.SingleWholeModel)
            {
                throw new UserFriendlyException("单文件模型必须使用 SingleWholeModel 类型。");
            }

            return;
        }

        if (files.Count != 2)
        {
            throw new UserFriendlyException("当前仅支持单文件模型或编码器/解码器双文件模型。");
        }

        bool hasEncoder = files.Any(x => x.FileRole == AiModelFileRole.SplitEncoder);
        bool hasDecoder = files.Any(x => x.FileRole == AiModelFileRole.SplitDecoder);
        bool hasWhole = files.Any(x => x.FileRole == AiModelFileRole.SingleWholeModel);
        if (!hasEncoder || !hasDecoder || hasWhole)
        {
            throw new UserFriendlyException(
                "双文件模型必须且只能包含一个 SplitEncoder 和一个 SplitDecoder。"
            );
        }
    }

    private static string BuildBlobName(Guid modelId, PendingUploadedFile file)
    {
        string extension = Path.GetExtension(file.OriginalFileName)
            .TrimStart('.')
            .ToLowerInvariant();
        return $"model/{modelId:N}/{file.SortOrder:D2}-{file.FileRole.ToString().ToLowerInvariant()}.{extension}";
    }

    private static TFile? PickPrimaryFile<TFile>(IReadOnlyList<TFile> files)
        where TFile : class
    {
        return files.FirstOrDefault();
    }

    private sealed record PendingUploadedFile(
        Guid SessionId,
        string TempFilePath,
        string OriginalFileName,
        string Md5,
        long FileSizeBytes,
        AiModelFileRole FileRole,
        int SortOrder
    );

    private static AiModelUploadSessionStatusDto MapToUploadSessionStatusDto(
        AiModelUploadSessionSnapshot session
    )
    {
        return new AiModelUploadSessionStatusDto
        {
            SessionId = session.SessionId,
            OriginalFileName = session.OriginalFileName,
            Md5 = session.Md5,
            FileSizeBytes = session.FileSizeBytes,
            ChunkSizeBytes = session.ChunkSizeBytes,
            TotalChunks = session.TotalChunks,
            UploadedChunks = session.UploadedChunks,
            UploadedBytes = session.UploadedBytes,
            NextChunkIndex = session.UploadedChunks,
            IsCompleted = session.UploadedChunks >= session.TotalChunks,
            LastUpdatedTime = session.LastUpdatedTime,
        };
    }

    private static async Task<string> ComputeFileMd5Async(string tempFilePath)
    {
        await using FileStream source = new(
            tempFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            while (true)
            {
                int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length));
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
            }

            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<(
        string tempFilePath,
        string md5,
        long fileSizeBytes
    )> CopyToTempFileAndComputeMd5Async(Stream source)
    {
        string tempFilePath = Path.Combine(
            Path.GetTempPath(),
            $"ai-model-{Guid.NewGuid():N}.upload"
        );
        long fileSizeBytes = 0;

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);

        try
        {
            await using FileStream target = new(
                tempFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );

            while (true)
            {
                int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length));
                if (read == 0)
                {
                    break;
                }

                await target.WriteAsync(buffer.AsMemory(0, read));
                hash.AppendData(buffer, 0, read);
                fileSizeBytes += read;
            }

            string md5 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            return (tempFilePath, md5, fileSizeBytes);
        }
        catch
        {
            DeleteTempFileQuietly(tempFilePath);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void DeleteTempFileQuietly(string? tempFilePath)
    {
        if (string.IsNullOrWhiteSpace(tempFilePath) || !File.Exists(tempFilePath))
        {
            return;
        }

        try
        {
            File.Delete(tempFilePath);
        }
        catch { }
    }

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
                    "[AiModelAppService] 删除 BLOB 文件 {BlobName} 失败（可能已不存在）：{Message}",
                    candidate,
                    ex.Message
                );
            }
        }
    }

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

    private static string NormalizeBlobName(string blobName)
    {
        return blobName.Replace('\\', '/').TrimStart('/');
    }

    private static bool ContainsPhysicalBlob(
        IReadOnlySet<string> physicalBlobNames,
        string? blobName
    )
    {
        return !string.IsNullOrWhiteSpace(blobName)
            && physicalBlobNames.Contains(NormalizeBlobName(blobName));
    }

    private static HashSet<string> BuildReferencedBlobNameSet(IEnumerable<AiModelFile> files)
    {
        HashSet<string> referencedBlobNames = [];

        foreach (AiModelFile file in files)
        {
            if (string.IsNullOrWhiteSpace(file.BlobName))
            {
                continue;
            }

            referencedBlobNames.Add(NormalizeBlobName(file.BlobName));
        }

        return referencedBlobNames;
    }

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
}
