using System.Text.Json;
using AuroraStruct3D.CalibrationManagement.Results;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Uow;

namespace AuroraStruct3D.CalibrationManagement.Projects.Jobs;

/// <summary>
/// 标定计算后台任务：异步执行多目相机内外参与结构光标定，写入 <see cref="CalibrationResult"/>。
/// </summary>
/// <remarks>
/// 当前阶段：完整搭建 Hangfire Job 框架（状态机切换、版本号分配、Result 落库、异常回滚），
/// 真正的 OpenCV 计算以占位数据填充，待集成 OpenCvSharp 后替换 <see cref="RunOpenCvComputation"/>。
/// </remarks>
public class CalibrationComputeJob
    : AsyncBackgroundJob<CalibrationComputeJobArgs>,
        ITransientDependency
{
    private readonly ICalibrationProjectRepository _projectRepository;
    private readonly ICalibrationResultRepository _resultRepository;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ILogger<CalibrationComputeJob> _logger;

    public CalibrationComputeJob(
        ICalibrationProjectRepository projectRepository,
        ICalibrationResultRepository resultRepository,
        IUnitOfWorkManager unitOfWorkManager,
        IGuidGenerator guidGenerator,
        ILogger<CalibrationComputeJob> logger
    )
    {
        _projectRepository = projectRepository;
        _resultRepository = resultRepository;
        _unitOfWorkManager = unitOfWorkManager;
        _guidGenerator = guidGenerator;
        _logger = logger;
    }

    /// <inheritdoc/>
    public override async Task ExecuteAsync(CalibrationComputeJobArgs args)
    {
        Guid projectId = args.CalibrationProjectId;
        _logger.LogInformation("[CalibrationComputeJob] 开始计算工程 {ProjectId}", projectId);

        // ── 第一步：将工程状态切换为"计算中" ──
        using (IUnitOfWork uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            CalibrationProject? project = await _projectRepository.FindWithDetailsAsync(projectId);
            if (project is null)
            {
                _logger.LogWarning("[CalibrationComputeJob] 工程 {ProjectId} 不存在，任务终止", projectId);
                return;
            }

            project.TransitionStatus(CalibrationProjectStatus.Computing);
            await _projectRepository.UpdateAsync(project);
            await uow.CompleteAsync();
        }

        // ── 第二步：执行计算（当前为占位实现） ──
        ComputationOutput output;
        try
        {
            CalibrationProject? loaded = await _projectRepository.FindWithDetailsAsync(projectId);
            if (loaded is null)
            {
                return;
            }

            output = RunOpenCvComputation(loaded);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CalibrationComputeJob] 工程 {ProjectId} 计算异常", projectId);

            using IUnitOfWork failUow = _unitOfWorkManager.Begin(
                requiresNew: true,
                isTransactional: true
            );
            CalibrationProject? failedProject = await _projectRepository.FindWithDetailsAsync(
                projectId
            );
            if (failedProject is not null)
            {
                failedProject.TransitionStatus(CalibrationProjectStatus.Failed, ex.Message);
                await _projectRepository.UpdateAsync(failedProject);
                await failUow.CompleteAsync();
            }
            return;
        }

        // ── 第三步：写入 CalibrationResult + 切换工程状态为"已完成" ──
        using (IUnitOfWork uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            CalibrationProject? project = await _projectRepository.FindWithDetailsAsync(projectId);
            if (project is null)
            {
                return;
            }

            int maxVersion = await _resultRepository.GetMaxVersionAsync(projectId);
            CalibrationResult result = new(
                _guidGenerator.Create(),
                projectId,
                project.CalibrationDeviceId,
                version: maxVersion + 1
            );
            result.SetCameraIntrinsics(output.CameraIntrinsicsJson);
            result.SetCameraExtrinsics(output.CameraExtrinsicsJson);
            if (!string.IsNullOrEmpty(output.StructuredLightCalibrationJson))
            {
                result.SetStructuredLightCalibration(output.StructuredLightCalibrationJson);
            }
            result.SetErrorStatistics(
                output.OverallReprojectionError,
                output.MaxError,
                output.MinError,
                output.MeanError,
                output.RmsError
            );

            // 首次结果默认生效；已有生效版本则保持原状（用户可后续切换）
            List<CalibrationResult> existingActives = await _resultRepository.GetActiveResultsAsync(
                projectId
            );
            if (existingActives.Count == 0)
            {
                result.SetActive(true);
            }

            await _resultRepository.InsertAsync(result);

            project.TransitionStatus(CalibrationProjectStatus.Completed);
            await _projectRepository.UpdateAsync(project);

            await uow.CompleteAsync();
        }

        _logger.LogInformation(
            "[CalibrationComputeJob] 工程 {ProjectId} 计算完成，重投影误差 RMS={Rms:F4}px",
            projectId,
            output.RmsError
        );
    }

    /// <summary>
    /// OpenCV 标定计算占位实现。
    /// 真实集成时此方法应：
    ///   1. 从 BLOB 容器加载所有 <see cref="CalibrationCaptureImage"/> 字节流
    ///   2. 解码图像 → 检测标定板角点（OpenCvSharp.Cv2.FindChessboardCorners 等）
    ///   3. 调用 Cv2.CalibrateCamera 求解单相机内参
    ///   4. 调用 Cv2.StereoCalibrate / Rodrigues 求解多相机外参
    ///   5. 单光系列额外计算结构光相位 - 深度映射
    ///   6. 序列化为 JSON 并填充误差统计
    /// </summary>
    private static ComputationOutput RunOpenCvComputation(CalibrationProject project)
    {
        // TODO(Phase 4)：引入 OpenCvSharp4 NuGet 包并替换为真实算法
        Dictionary<string, object> intrinsicsPlaceholder = new()
        {
            ["note"] = "占位标定结果，待 OpenCV 集成后由 Cv2.CalibrateCamera 输出",
            ["frameCount"] = project.Frames.Count(f => f.IsAccepted),
            ["boardType"] = project.BoardType.ToString(),
            ["targetCaptureCount"] = project.TargetCaptureCount,
        };
        Dictionary<string, object> extrinsicsPlaceholder = new()
        {
            ["note"] = "占位多相机外参，待 OpenCV 集成后由 Cv2.StereoCalibrate 输出",
        };

        JsonSerializerOptions jsonOpts = new() { WriteIndented = false };
        return new ComputationOutput
        {
            CameraIntrinsicsJson = JsonSerializer.Serialize(intrinsicsPlaceholder, jsonOpts),
            CameraExtrinsicsJson = JsonSerializer.Serialize(extrinsicsPlaceholder, jsonOpts),
            StructuredLightCalibrationJson = null,
            OverallReprojectionError = 0.0,
            MaxError = 0.0,
            MinError = 0.0,
            MeanError = 0.0,
            RmsError = 0.0,
        };
    }

    /// <summary>OpenCV 计算输出的内部 DTO。</summary>
    private sealed class ComputationOutput
    {
        public string CameraIntrinsicsJson { get; set; } = "{}";
        public string CameraExtrinsicsJson { get; set; } = "{}";
        public string? StructuredLightCalibrationJson { get; set; }
        public double OverallReprojectionError { get; set; }
        public double MaxError { get; set; }
        public double MinError { get; set; }
        public double MeanError { get; set; }
        public double RmsError { get; set; }
    }
}
