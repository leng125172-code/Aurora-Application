using AuroraStruct3D.Motors.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.Motors;

/// <summary>
/// 485 串口电机设备管理应用服务。
/// </summary>
public interface IMotorDeviceAppService : IApplicationService
{
    /// <summary>获取电机轴列表</summary>
    Task<PagedResultDto<MotorAxisDto>> GetListAsync(GetMotorAxisListDto input);

    /// <summary>获取单个电机轴</summary>
    Task<MotorAxisDto> GetAsync(Guid id);

    /// <summary>扫描串口和电机设备，并同步写入数据库</summary>
    Task<ScanMotorDevicesResultDto> ScanDevicesAsync(ScanMotorDevicesInput input);

    /// <summary>更新电机轴基础信息</summary>
    Task<MotorAxisDto> UpdateAsync(Guid id, UpdateMotorAxisDto input);

    /// <summary>设置瓴控电机旋转角度最小/最大值</summary>
    Task<MotorAxisDto> SetRotationAngleRangeAsync(Guid id, SetMotorRotationAngleRangeDto input);

    /// <summary>读取当前单圈角度并设置为旋转角度最小值或最大值</summary>
    Task<MotorAxisDto> SetRotationAngleLimitFromCurrentAsync(
        Guid id,
        SetMotorRotationAngleLimitFromCurrentDto input
    );

    /// <summary>从设备读取状态，写入数据库后返回</summary>
    Task<MotorAxisDto> RefreshStatusAsync(Guid id);

    /// <summary>使能电机</summary>
    Task<MotorAxisDto> EnableAsync(Guid id);

    /// <summary>去使能电机</summary>
    Task<MotorAxisDto> DisableAsync(Guid id);

    /// <summary>绝对位置运动</summary>
    Task<MotorAxisDto> MoveAbsoluteAsync(Guid id, MoveMotorInput input);

    /// <summary>相对位置运动</summary>
    Task<MotorAxisDto> MoveRelativeAsync(Guid id, MoveMotorInput input);

    /// <summary>减速停止</summary>
    Task<MotorAxisDto> StopAsync(Guid id);

    /// <summary>急停</summary>
    Task<MotorAxisDto> EmergencyStopAsync(Guid id);

    /// <summary>回零</summary>
    Task<MotorAxisDto> HomeAsync(Guid id);

    /// <summary>清除故障</summary>
    Task<MotorAxisDto> ClearFaultAsync(Guid id);

    /// <summary>读取保持寄存器（主要用于雷赛 Modbus RTU 调试）</summary>
    Task<RegisterReadResultDto> ReadHoldingRegistersAsync(Guid id, ReadHoldingRegistersInput input);

    /// <summary>写单个保持寄存器（主要用于雷赛 Modbus RTU 调试）</summary>
    Task<bool> WriteSingleRegisterAsync(Guid id, WriteSingleRegisterInput input);

    /// <summary>分页查询电机操作日志</summary>
    Task<PagedResultDto<MotorOperationLogDto>> GetLogsAsync(GetMotorLogListDto input);
}
