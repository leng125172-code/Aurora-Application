using AuroraStruct3D.CalibrationManagement.Devices.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.CalibrationManagement.Devices;

/// <summary>
/// 标定设备应用服务接口。
/// ABP 自动生成 REST 端点，基础路径：/api/app/calibration-device
/// 涵盖：CRUD、相机/电机/投射器绑定、云台组管理、互锁规则、硬件参数快照。
/// </summary>
public interface ICalibrationDeviceAppService : IApplicationService
{
    // ── 设备 CRUD ────────────────────────────────────────────────────────

    /// <summary>分页查询标定设备列表</summary>
    Task<PagedResultDto<CalibrationDeviceListDto>> GetListAsync(GetCalibrationDeviceListInput input);

    /// <summary>获取标定设备详情（含全部子集合）</summary>
    Task<CalibrationDeviceDetailDto> GetAsync(Guid id);

    /// <summary>创建标定设备</summary>
    Task<CalibrationDeviceDetailDto> CreateAsync(CreateUpdateCalibrationDeviceDto input);

    /// <summary>更新设备基础信息（名称/描述/拓扑类型）</summary>
    Task<CalibrationDeviceDetailDto> UpdateAsync(Guid id, CreateUpdateCalibrationDeviceDto input);

    /// <summary>删除标定设备（连同所有子集合）</summary>
    Task DeleteAsync(Guid id);

    /// <summary>切换启用状态</summary>
    Task<CalibrationDeviceDetailDto> SetActiveAsync(Guid id, SetCalibrationDeviceActiveInput input);

    // ── 相机绑定 ─────────────────────────────────────────────────────────

    /// <summary>新增或替换相机绑定（按角色唯一）</summary>
    Task<CalibrationCameraBindingDto> UpsertCameraBindingAsync(
        Guid deviceId,
        UpsertCameraBindingInput input
    );

    /// <summary>移除相机绑定</summary>
    Task RemoveCameraBindingAsync(Guid deviceId, Guid bindingId);

    // ── 电机绑定 ─────────────────────────────────────────────────────────

    /// <summary>新增或替换电机绑定（按角色唯一）</summary>
    Task<CalibrationMotorBindingDto> UpsertMotorBindingAsync(
        Guid deviceId,
        UpsertMotorBindingInput input
    );

    /// <summary>移除电机绑定</summary>
    Task RemoveMotorBindingAsync(Guid deviceId, Guid bindingId);

    // ── 投射器绑定 ───────────────────────────────────────────────────────

    /// <summary>新增或替换投射器绑定</summary>
    Task<CalibrationProjectorBindingDto> UpsertProjectorBindingAsync(
        Guid deviceId,
        UpsertProjectorBindingInput input
    );

    /// <summary>移除投射器绑定</summary>
    Task RemoveProjectorBindingAsync(Guid deviceId, Guid bindingId);

    // ── 云台组 ───────────────────────────────────────────────────────────

    /// <summary>创建云台组</summary>
    Task<CalibrationGimbalGroupDto> CreateGimbalGroupAsync(
        Guid deviceId,
        CreateUpdateGimbalGroupInput input
    );

    /// <summary>更新云台组</summary>
    Task<CalibrationGimbalGroupDto> UpdateGimbalGroupAsync(
        Guid deviceId,
        Guid groupId,
        CreateUpdateGimbalGroupInput input
    );

    /// <summary>删除云台组</summary>
    Task DeleteGimbalGroupAsync(Guid deviceId, Guid groupId);

    /// <summary>新增云台预设位置</summary>
    Task<CalibrationGimbalPresetDto> AddGimbalPresetAsync(
        Guid deviceId,
        Guid groupId,
        CreateGimbalPresetInput input
    );

    /// <summary>删除云台预设位置</summary>
    Task RemoveGimbalPresetAsync(Guid deviceId, Guid groupId, Guid presetId);

    // ── 互锁规则 ─────────────────────────────────────────────────────────

    /// <summary>新增电机联动软限位规则</summary>
    Task<CalibrationMotorInterlockRuleDto> CreateInterlockRuleAsync(
        Guid deviceId,
        CreateUpdateInterlockRuleInput input
    );

    /// <summary>更新电机联动软限位规则</summary>
    Task<CalibrationMotorInterlockRuleDto> UpdateInterlockRuleAsync(
        Guid deviceId,
        Guid ruleId,
        CreateUpdateInterlockRuleInput input
    );

    /// <summary>删除电机联动软限位规则</summary>
    Task DeleteInterlockRuleAsync(Guid deviceId, Guid ruleId);

    // ── 硬件参数 ─────────────────────────────────────────────────────────

    /// <summary>新增/替换相机硬件参数（同一相机唯一）</summary>
    Task<CalibrationCameraParameterDto> UpsertCameraParameterAsync(
        Guid deviceId,
        CreateUpdateCameraParameterInput input
    );

    /// <summary>新增/替换投射器硬件参数</summary>
    Task<CalibrationProjectorParameterDto> UpsertProjectorParameterAsync(
        Guid deviceId,
        CreateUpdateProjectorParameterInput input
    );

    /// <summary>新增/替换电机硬件参数</summary>
    Task<CalibrationMotorParameterDto> UpsertMotorParameterAsync(
        Guid deviceId,
        CreateUpdateMotorParameterInput input
    );
}
