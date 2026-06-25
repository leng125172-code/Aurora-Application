using AuroraStruct3D.OperatorFile;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
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

    public CleanupExpiredOperatorFilesJob(
        IOperatorFileRecordRepository recordRepository,
        IBlobContainer<OperatorFileBlobContainer> blobContainer,
        ILogger<CleanupExpiredOperatorFilesJob> logger
    )
    {
        _recordRepository = recordRepository;
        _blobContainer = blobContainer;
        _logger = logger;
    }

    /// <summary>
    /// 执行清理：查找过期文件 → 删除 BLOB → 删除数据库记录。
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
            "[CleanupExpiredOperatorFilesJob] 发现 {Count} 个过期文件，开始清理...",
            expiredRecords.Count
        );

        int deletedCount = 0;
        int failedCount = 0;

        foreach (OperatorFileRecord record in expiredRecords)
        {
            try
            {
                // 删除所有相关 BLOB 文件（原文件 + 预览图）
                List<string> allBlobNames = record.GetAllBlobNames();
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

                // 删除数据库记录
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
            "[CleanupExpiredOperatorFilesJob] 清理完成：成功 {Deleted}，失败 {Failed}",
            deletedCount,
            failedCount
        );
    }
}
