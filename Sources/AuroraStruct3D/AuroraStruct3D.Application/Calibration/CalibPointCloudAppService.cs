using AuroraStruct3D.Calibration.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// Step7 点云生成应用服务实现。
/// 按钮触发后在后台异步生成 PLY 点云文件，通过 SignalR 实时推送进度，完成后推送下载 URL。
/// </summary>
[Authorize]
public class CalibPointCloudAppService : AuroraStruct3DAppService, ICalibPointCloudAppService
{
    private readonly IRepository<CalibProject, Guid> _projectRepository;
    private readonly CalibPointCloudStateStore _stateStore;
    private readonly ICalibPointCloudNotifier _notifier;
    private readonly ILogger<CalibPointCloudAppService> _logger;

    /// <summary>构造注入</summary>
    public CalibPointCloudAppService(
        IRepository<CalibProject, Guid> projectRepository,
        CalibPointCloudStateStore stateStore,
        ICalibPointCloudNotifier notifier,
        ILogger<CalibPointCloudAppService> logger
    )
    {
        _projectRepository = projectRepository;
        _stateStore = stateStore;
        _notifier = notifier;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<PointCloudStatusDto> GenerateAsync(GeneratePointCloudInput input)
    {
        CalibProject project = await _projectRepository.GetAsync(input.CalibProjectId);

        PointCloudSessionState session = _stateStore.GetOrCreate(project.Id);
        if (session.IsRunning)
        {
            throw new UserFriendlyException("点云生成任务正在进行中，请等待完成或先取消。");
        }

        (PointCloudSessionState started, CancellationToken token) = _stateStore.Start(project.Id);
        PointCloudStatusDto startDto = started.ToDto();

        _logger.LogInformation("Step7 点云生成启动：ProjectId={ProjectId}", project.Id);

        // 通知前端：已开始
        await _notifier.NotifyStatusAsync(startDto);

        // 在后台线程执行实际生成逻辑
        _ = Task.Run(async () => await RunGenerationAsync(project, token), token);

        return startDto;
    }

    /// <inheritdoc/>
    public async Task<PointCloudStatusDto> CancelAsync(Guid calibProjectId)
    {
        bool canceled = _stateStore.Cancel(calibProjectId);

        PointCloudSessionState session = _stateStore.GetOrCreate(calibProjectId);
        PointCloudStatusDto dto = session.ToDto();

        if (canceled)
        {
            _logger.LogInformation("Step7 点云生成已取消：ProjectId={ProjectId}", calibProjectId);
            await _notifier.NotifyStatusAsync(dto);
        }

        return dto;
    }

    /// <inheritdoc/>
    public async Task<PointCloudStatusDto> GetStatusAsync(Guid calibProjectId)
    {
        // 确保项目存在
        await _projectRepository.GetAsync(calibProjectId);
        PointCloudSessionState session = _stateStore.GetOrCreate(calibProjectId);
        return session.ToDto();
    }

    // ─── 私有：后台生成流程 ────────────────────────────────────────────────────

    /// <summary>
    /// 后台点云生成流程（模拟阶段进度）。
    /// 实际项目中此处调用结构光重建算法、相机采集、PLY 输出等，并持续上报进度。
    /// </summary>
    private async Task RunGenerationAsync(CalibProject project, CancellationToken cancellationToken)
    {
        try
        {
            // ── 阶段1：相机采集（0~30%）
            await ReportProgressAsync(project.Id, 5, "正在采集结构光图像...", cancellationToken);
            await SimulateWorkAsync(1200, cancellationToken);

            await ReportProgressAsync(
                project.Id,
                15,
                "图像采集完成，正在预处理...",
                cancellationToken
            );
            await SimulateWorkAsync(800, cancellationToken);

            await ReportProgressAsync(project.Id, 30, "图像预处理完成", cancellationToken);

            // ── 阶段2：相位解包与深度计算（30~70%）
            await ReportProgressAsync(project.Id, 40, "正在进行相位解包...", cancellationToken);
            await SimulateWorkAsync(1500, cancellationToken);

            await ReportProgressAsync(project.Id, 55, "正在计算深度图...", cancellationToken);
            await SimulateWorkAsync(1200, cancellationToken);

            await ReportProgressAsync(project.Id, 70, "深度图计算完成", cancellationToken);

            // ── 阶段3：点云生成与 PLY 输出（70~100%）
            await ReportProgressAsync(project.Id, 80, "正在生成点云...", cancellationToken);
            await SimulateWorkAsync(1000, cancellationToken);

            await ReportProgressAsync(project.Id, 90, "正在保存 PLY 文件...", cancellationToken);
            await SimulateWorkAsync(500, cancellationToken);

            // ── 完成：构造下载 URL（实际应为 BLOB 存储或文件服务 URL）
            string plyDownloadUrl = $"/api/app/calib-point-cloud/download/{project.Id}";
            long fileSizeBytes = 1024 * 1024 * 5L; // 示例：5MB

            PointCloudStatusDto completed = _stateStore.Complete(
                project.Id,
                plyDownloadUrl,
                fileSizeBytes
            );
            await _notifier.NotifyStatusAsync(completed);

            _logger.LogInformation(
                "Step7 点云生成完成：ProjectId={ProjectId}, Url={Url}",
                project.Id,
                plyDownloadUrl
            );
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Step7 点云生成被取消：ProjectId={ProjectId}", project.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step7 点云生成失败：ProjectId={ProjectId}", project.Id);
            PointCloudStatusDto failed = _stateStore.Fail(project.Id, ex.Message);
            try
            {
                await _notifier.NotifyStatusAsync(failed);
            }
            catch
            {
                // 通知失败时不再抛出
            }
        }
    }

    /// <summary>上报进度并通知前端。</summary>
    private async Task ReportProgressAsync(
        Guid calibProjectId,
        int progress,
        string message,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        PointCloudStatusDto? dto = _stateStore.UpdateProgress(calibProjectId, progress, message);
        if (dto != null)
        {
            await _notifier.NotifyStatusAsync(dto);
        }
    }

    /// <summary>模拟耗时工作（实际替换为真实算法调用）。</summary>
    private static Task SimulateWorkAsync(int milliseconds, CancellationToken cancellationToken)
    {
        return Task.Delay(milliseconds, cancellationToken);
    }
}
