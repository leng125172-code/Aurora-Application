using AuroraStruct3D.OperatorFile;
using AuroraStruct3D.Workflow;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace AuroraStruct3D.Jobs;

/// <summary>
/// 清理过期算子文件 Job 参数（无参，仅用于类型标识）。
/// </summary>
public class CleanupExpiredOperatorFilesJobArgs { }

/// <summary>
/// 定时清理过期未使用的算子上传文件 Hangfire Job。
/// <para>
/// 每小时执行一次：查找所有已过期且未使用的文件记录，
/// 同时检查是否被任何工作流引用，只有不在引用列表中的文件才删除，
/// 只处理工作流相关路径前缀的文件（{guid}/ 和 workflow-image/），
/// 删除对应 BLOB 存储文件，然后删除数据库记录。
/// </para>
/// </summary>
public class CleanupExpiredOperatorFilesJob
    : AsyncBackgroundJob<CleanupExpiredOperatorFilesJobArgs>,
        ITransientDependency
{
    private readonly IOperatorFileRecordRepository _recordRepository;
    private readonly IBlobContainer<OperatorFileBlobContainer> _blobContainer;
    private readonly ILogger<CleanupExpiredOperatorFilesJob> _logger;
    private readonly WorkflowBlobReferenceScanner _blobReferenceScanner;

    private static readonly HashSet<string> WorkflowBlobPrefixes = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "workflow-image/",
    };

    public CleanupExpiredOperatorFilesJob(
        IOperatorFileRecordRepository recordRepository,
        IBlobContainer<OperatorFileBlobContainer> blobContainer,
        ILogger<CleanupExpiredOperatorFilesJob> logger,
        WorkflowBlobReferenceScanner blobReferenceScanner
    )
    {
        _recordRepository = recordRepository;
        _blobContainer = blobContainer;
        _logger = logger;
        _blobReferenceScanner = blobReferenceScanner;
    }

    /// <summary>
    /// 执行清理：查找过期文件 → 检查工作流引用 → 删除未被引用的 BLOB → 删除数据库记录。
    /// </summary>
    [UnitOfWork]
    public override async Task ExecuteAsync(CleanupExpiredOperatorFilesJobArgs args)
    {
        DateTime now = DateTime.Now;
        List<OperatorFileRecord> expiredRecords = await _recordRepository.GetExpiredUnusedAsync(
            now
        );

        if (expiredRecords.Count == 0)
        {
            _logger.LogDebug("[CleanupExpiredOperatorFilesJob] 没有过期文件需要清理。");
            return;
        }

        _logger.LogInformation(
            "[CleanupExpiredOperatorFilesJob] 发现 {Count} 个过期文件，开始检查工作流引用...",
            expiredRecords.Count
        );

        HashSet<string> referencedBlobNames = await _blobReferenceScanner.GetAllReferencedBlobNamesAsync();
        _logger.LogInformation(
            "[CleanupExpiredOperatorFilesJob] 已扫描到 {RefCount} 个被工作流引用的 BlobName",
            referencedBlobNames.Count
        );

        int deletedCount = 0;
        int skippedReferencedCount = 0;
        int skippedNonWorkflowCount = 0;
        int failedCount = 0;

        foreach (OperatorFileRecord record in expiredRecords)
        {
            try
            {
                if (!IsWorkflowRelatedBlob(record.BlobName))
                {
                    _logger.LogInformation(
                        "[CleanupExpiredOperatorFilesJob] 文件 {BlobName} 不是工作流相关文件，跳过清理",
                        record.BlobName
                    );
                    skippedNonWorkflowCount++;
                    continue;
                }

                bool isReferenced = false;
                List<string> allBlobNames = record.GetAllBlobNames();
                foreach (string blobName in allBlobNames)
                {
                    if (referencedBlobNames.Contains(blobName))
                    {
                        isReferenced = true;
                        break;
                    }
                }

                if (isReferenced)
                {
                    _logger.LogInformation(
                        "[CleanupExpiredOperatorFilesJob] 文件 {BlobName} 被工作流引用，跳过清理",
                        record.BlobName
                    );
                    skippedReferencedCount++;
                    continue;
                }

                foreach (string blobName in allBlobNames)
                {
                    try
                    {
                        await _blobContainer.DeleteAsync(blobName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "[CleanupExpiredOperatorFilesJob] 删除 BLOB 失败：{BlobName}",
                            blobName
                        );
                    }
                }

                await _recordRepository.DeleteAsync(record);
                deletedCount++;

                _logger.LogDebug(
                    "[CleanupExpiredOperatorFilesJob] 已清理文件：{BlobName}（{OriginalFileName}）",
                    record.BlobName,
                    record.OriginalFileName
                );
            }
            catch (Exception ex)
            {
                failedCount++;
                _logger.LogError(
                    ex,
                    "[CleanupExpiredOperatorFilesJob] 清理文件失败：{BlobName}",
                    record.BlobName
                );
            }
        }

        _logger.LogInformation(
            "[CleanupExpiredOperatorFilesJob] 清理完成：成功 {Deleted}，跳过（被引用）{SkippedReferenced}，跳过（非工作流）{SkippedNonWorkflow}，失败 {Failed}",
            deletedCount,
            skippedReferencedCount,
            skippedNonWorkflowCount,
            failedCount
        );
    }

    private static bool IsWorkflowRelatedBlob(string blobName)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            return false;
        }

        if (WorkflowBlobPrefixes.Any(prefix => blobName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return IsOperatorUploadPath(blobName);
    }

    private static bool IsOperatorUploadPath(string blobName)
    {
        int firstSlashIndex = blobName.IndexOf('/');
        if (firstSlashIndex <= 0 || firstSlashIndex >= blobName.Length - 1)
        {
            return false;
        }

        string prefix = blobName.Substring(0, firstSlashIndex);
        return Guid.TryParse(prefix, out _);
    }
}
