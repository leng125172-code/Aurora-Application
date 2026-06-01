using AuroraStruct3D.CalibrationManagement.Projects.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.CalibrationManagement.Projects;

/// <summary>
/// 标定工程应用服务接口。
/// 自动生成路径：/api/app/calibration-project
/// </summary>
public interface ICalibrationProjectAppService : IApplicationService
{
    // ── 工程 CRUD ────────────────────────────────────────────────────────

    Task<PagedResultDto<CalibrationProjectListDto>> GetListAsync(
        GetCalibrationProjectListInput input
    );

    Task<CalibrationProjectDetailDto> GetAsync(Guid id);

    Task<CalibrationProjectDetailDto> CreateAsync(CreateCalibrationProjectDto input);

    Task<CalibrationProjectDetailDto> UpdateAsync(Guid id, UpdateCalibrationProjectDto input);

    Task DeleteAsync(Guid id);

    // ── 配置 ─────────────────────────────────────────────────────────────

    Task<CalibrationProjectDetailDto> SetBoardConfigAsync(Guid id, SetBoardConfigInput input);

    Task<CalibrationProjectDetailDto> SetCaptureConfigAsync(Guid id, SetCaptureConfigInput input);

    Task<CalibrationProjectDetailDto> TransitionStatusAsync(
        Guid id,
        TransitionProjectStatusInput input
    );

    // ── 采集帧管理 ───────────────────────────────────────────────────────

    /// <summary>
    /// 同步触发一次多相机采集：依次从绑定到该工程标定设备的全部相机抓取单帧快照，
    /// 写入 BLOB 容器，并新增 <see cref="CalibrationCaptureFrame"/> + 多张 <see cref="CalibrationCaptureImage"/>。
    /// 返回新创建的帧详情。
    /// </summary>
    Task<CalibrationCaptureFrameDto> CaptureFrameAsync(Guid id);

    /// <summary>拒绝采集帧</summary>
    Task RejectFrameAsync(Guid projectId, Guid frameId, RejectFrameInput input);

    /// <summary>接受采集帧</summary>
    Task AcceptFrameAsync(Guid projectId, Guid frameId);

    /// <summary>删除采集帧</summary>
    Task DeleteFrameAsync(Guid projectId, Guid frameId);

    // ── 计算 / 导出 ──────────────────────────────────────────────────────

    /// <summary>
    /// 异步触发标定计算：通过 Hangfire 入队
    /// <see cref="AuroraStruct3D.CalibrationManagement.Projects.Jobs.CalibrationComputeJob"/>，
    /// 由 Worker 进程调用 OpenCV 完成相机内外参与结构光标定，最终写入 <see cref="CalibrationResult"/>。
    /// 接口立即返回，不阻塞调用方。
    /// </summary>
    Task ComputeAsync(Guid id);
}
