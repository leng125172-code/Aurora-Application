using AuroraStruct3D.CalibrationManagement.Projects.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.CalibrationManagement.Projects;

/// <summary>
/// 标定工程应用服务接口。
/// 自动生成路径：/api/app/calibration-project
/// 注：实时采集与计算方法当前为占位实现，等待 Phase 3 OpenCV/SignalR/Hangfire 集成后落地。
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
    /// 触发一次采集（占位实现：抛 <see cref="NotImplementedException"/>，
    /// Phase 3 由 Hangfire Job 异步执行多相机同步触发 + BLOB 写入）。
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
    /// 触发标定计算（占位实现：抛 <see cref="NotImplementedException"/>，
    /// Phase 3 由 Hangfire Job 调用 OpenCV 完成相机内外参与结构光标定）。
    /// </summary>
    Task ComputeAsync(Guid id);
}
