using System.Diagnostics;
using System.Security.Cryptography;
using AuroraStruct3D.AI.Dtos;
using Hangfire;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Uow;

namespace AuroraStruct3D.AI.Jobs;

/// <summary>
/// AI 模型转换后台任务。
/// 负责下载 ONNX 原始文件、调用 Python 转换器并保存转换产物。
/// </summary>
public class AiModelConversionJob : ITransientDependency
{
    private readonly IAiModelRepository _aiModelRepository;
    private readonly IAiModelFileRepository _aiModelFileRepository;
    private readonly IAiModelOperationLogRepository _operationLogRepository;
    private readonly IBlobContainer<AiModelBlobContainer> _blobContainer;
    private readonly IAiModelPythonConversionExecutor _pythonConversionExecutor;
    private readonly IAiModelConversionNotifier _notifier;
    private readonly ILogger<AiModelConversionJob> _logger;

    public AiModelConversionJob(
        IAiModelRepository aiModelRepository,
        IAiModelFileRepository aiModelFileRepository,
        IAiModelOperationLogRepository operationLogRepository,
        IBlobContainer<AiModelBlobContainer> blobContainer,
        IAiModelPythonConversionExecutor pythonConversionExecutor,
        IAiModelConversionNotifier notifier,
        ILogger<AiModelConversionJob> logger
    )
    {
        _aiModelRepository = aiModelRepository;
        _aiModelFileRepository = aiModelFileRepository;
        _operationLogRepository = operationLogRepository;
        _blobContainer = blobContainer;
        _pythonConversionExecutor = pythonConversionExecutor;
        _notifier = notifier;
        _logger = logger;
    }

    /// <summary>
    /// 执行 AI 模型转换。
    /// </summary>
    [AutomaticRetry(Attempts = 2)]
    [UnitOfWork]
    public async Task ExecuteAsync(AiModelConversionJobArgs args)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        AiModel model = await _aiModelRepository.GetAsync(args.ModelId);
        List<AiModelFile> files = await _aiModelFileRepository.GetListByModelIdAsync(args.ModelId);
        List<AiModelFile> sourceFiles = files
            .Where(x => args.SourceFileIds.Contains(x.Id))
            .OrderBy(x => x.SortOrder)
            .ToList();

        if (sourceFiles.Count == 0)
        {
            _logger.LogWarning(
                "[AiModelConversionJob] 模型 {ModelId} 未找到待转换文件。",
                args.ModelId
            );
            return;
        }

        if (
            sourceFiles.Any(file =>
                file.ConversionStatus != AiModelFileConversionStatus.Pending
                || file.ConversionTargetType != args.TargetType
            )
        )
        {
            _logger.LogInformation(
                "[AiModelConversionJob] 模型 {ModelId} 的转换状态已变化，跳过重复执行。",
                args.ModelId
            );
            return;
        }

        string workDirectory = Path.Combine(
            Path.GetTempPath(),
            "aurora-ai-conversion",
            args.ModelId.ToString("N"),
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(workDirectory);

        try
        {
            foreach (AiModelFile file in sourceFiles)
            {
                file.UpdateConversionState(AiModelFileConversionStatus.Converting, args.TargetType);
                await _aiModelFileRepository.UpdateAsync(file, autoSave: true);
            }

            await _notifier.NotifyStartedAsync(
                CreateConversionStateDto(
                    sourceFiles,
                    args.TargetType,
                    AiModelFileConversionStatus.Converting,
                    null,
                    DateTime.UtcNow
                )
            );

            List<AiModelPythonConversionInputFile> inputFiles = [];
            foreach (AiModelFile sourceFile in sourceFiles)
            {
                string localPath = Path.Combine(workDirectory, sourceFile.OriginalFileName);
                await using Stream blobStream = await _blobContainer.GetAsync(sourceFile.BlobName);
                await using FileStream localStream = new(
                    localPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None
                );
                await blobStream.CopyToAsync(localStream);
                await localStream.FlushAsync();

                inputFiles.Add(
                    new AiModelPythonConversionInputFile
                    {
                        SourceFileId = sourceFile.Id,
                        FilePath = localPath,
                        OriginalFileName = sourceFile.OriginalFileName,
                        FileRole = sourceFile.FileRole,
                        SortOrder = sourceFile.SortOrder,
                    }
                );
            }

            AiModelPythonConversionResult result = await _pythonConversionExecutor.ExecuteAsync(
                new AiModelPythonConversionRequest
                {
                    ModelId = model.Id,
                    ModelName = model.Name,
                    GenerationCondition = model.GenerationCondition,
                    TargetType = args.TargetType,
                    WorkingDirectory = workDirectory,
                    InputFiles = inputFiles,
                }
            );

            if (!result.Success)
            {
                throw new BusinessException(message: result.Message);
            }

            List<AiModelFile> convertedFiles = [];
            foreach (
                AiModelPythonConversionOutputFile outputFile in result.OutputFiles.OrderBy(x =>
                    x.SortOrder
                )
            )
            {
                if (!File.Exists(outputFile.FilePath))
                {
                    throw new FileNotFoundException(
                        $"Python 转换结果文件不存在：{outputFile.FilePath}"
                    );
                }

                FileInfo fileInfo = new(outputFile.FilePath);
                string blobName =
                    $"converted/{model.Id:N}/{Guid.NewGuid():N}.{outputFile.FileFormat.ToLowerInvariant()}";
                await using FileStream fileStream = new(
                    outputFile.FilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read
                );
                await _blobContainer.SaveAsync(blobName, fileStream, overrideExisting: false);

                AiModelFile sourceFile = sourceFiles.First(x => x.Id == outputFile.SourceFileId);
                convertedFiles.Add(
                    AiModelFile.CreateConvertedProduct(
                        Guid.NewGuid(),
                        model.Id,
                        sourceFile.Id,
                        outputFile.FileName,
                        outputFile.DisplayName,
                        outputFile.FileFormat.ToUpperInvariant(),
                        fileInfo.Length,
                        await CalculateMd5Async(outputFile.FilePath),
                        blobName,
                        outputFile.FileRole,
                        args.TargetType,
                        outputFile.SortOrder
                    )
                );
            }

            if (convertedFiles.Count > 0)
            {
                await _aiModelFileRepository.InsertManyAsync(convertedFiles, autoSave: true);
            }

            foreach (AiModelFile file in sourceFiles)
            {
                file.UpdateConversionState(
                    AiModelFileConversionStatus.Completed,
                    args.TargetType,
                    null,
                    DateTime.UtcNow
                );
                await _aiModelFileRepository.UpdateAsync(file, autoSave: true);
            }

            await _notifier.NotifyFinishedAsync(
                CreateConversionStateDto(
                    sourceFiles,
                    args.TargetType,
                    AiModelFileConversionStatus.Completed,
                    null,
                    DateTime.UtcNow
                )
            );

            await _operationLogRepository.InsertAsync(
                AiModelOperationLog.Success(
                    Guid.NewGuid(),
                    model.Id,
                    model.Name,
                    sourceFiles.FirstOrDefault()?.OriginalFileName,
                    AiModelOperationType.StartConversion,
                    result.Message,
                    (int)stopwatch.ElapsedMilliseconds
                ),
                autoSave: true
            );
        }
        catch (Exception ex)
        {
            foreach (AiModelFile file in sourceFiles)
            {
                file.UpdateConversionState(
                    AiModelFileConversionStatus.Failed,
                    args.TargetType,
                    ex.Message,
                    DateTime.UtcNow
                );
                await _aiModelFileRepository.UpdateAsync(file, autoSave: true);
            }

            await _notifier.NotifyFinishedAsync(
                CreateConversionStateDto(
                    sourceFiles,
                    args.TargetType,
                    AiModelFileConversionStatus.Failed,
                    ex.Message,
                    DateTime.UtcNow
                )
            );

            await _operationLogRepository.InsertAsync(
                AiModelOperationLog.Failure(
                    Guid.NewGuid(),
                    model.Id,
                    model.Name,
                    sourceFiles.FirstOrDefault()?.OriginalFileName,
                    AiModelOperationType.StartConversion,
                    ex.Message,
                    null,
                    (int)stopwatch.ElapsedMilliseconds
                ),
                autoSave: true
            );

            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(workDirectory))
                {
                    Directory.Delete(workDirectory, recursive: true);
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogDebug(
                    cleanupEx,
                    "[AiModelConversionJob] 清理临时目录失败：{Directory}",
                    workDirectory
                );
            }
        }
    }

    private static async Task<string> CalculateMd5Async(string filePath)
    {
        using MD5 md5 = MD5.Create();
        await using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read
        );
        byte[] hash = await md5.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
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
}
