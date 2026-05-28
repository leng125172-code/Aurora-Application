using AuroraStruct3D.Leisai.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.Leisai;

/// <summary>
/// 雷赛 iCL-RS 电机操作台应用服务接口。仅服务于 LeisaiIclRs 品牌轴。
/// 覆盖 Phase 1 功能：实时状态读取、IO 控制、故障清除、参数持久化、原始 Modbus 命令。
/// </summary>
public interface ILeisaiMotorAppService : IApplicationService
{
    // ====== 实时状态（单次手动读取，常规推送由 Sampler 走 SignalR） ======

    /// <summary>读取一次完整状态快照（含 0x1003、0x6002、命令/实际位置、电压、IO、故障）。</summary>
    Task<LeisaiStateSnapshotDto> GetStateAsync(Guid id);

    /// <summary>读取 DI/DO 功能配置。</summary>
    Task<LeisaiIoConfigDto> GetIoConfigAsync(Guid id);

    // ====== 控制 ======

    /// <summary>设置 DO 输出电平（通过修改 0x017B 对应位）。</summary>
    Task WriteOutputAsync(Guid id, LeisaiWriteOutputInputDto input);

    /// <summary>清除故障（按 mode 决定写入 0x1801 的值）。</summary>
    Task ClearFaultAsync(Guid id, LeisaiClearFaultInputDto input);

    /// <summary>保存参数到 EEPROM（FC06 写 0x1801 = 0x2211）。</summary>
    Task SaveToEepromAsync(Guid id);

    // ====== 原始 Modbus ======

    /// <summary>执行手动 Modbus 读（FC03）。</summary>
    Task<LeisaiRawModbusResultDto> RawReadAsync(Guid id, LeisaiRawModbusReadInputDto input);

    /// <summary>执行手动 Modbus 写（FC06 / FC16）。</summary>
    Task<LeisaiRawModbusResultDto> RawWriteAsync(Guid id, LeisaiRawModbusWriteInputDto input);

    // ====== 采样开关 ======

    /// <summary>查询当前采样是否启用。</summary>
    Task<LeisaiSamplingStateDto> GetSamplingStateAsync(Guid id);

    /// <summary>启用实时采样。</summary>
    Task EnableSamplingAsync(Guid id);

    /// <summary>暂停实时采样。</summary>
    Task DisableSamplingAsync(Guid id);

    // ====== 参数配置（Phase 2） ======

    /// <summary>获取参数元数据目录。不传 group 时返回全量；传入 group（0~9）时仅返回该分组的参数。</summary>
    Task<List<LeisaiParameterMetadataDto>> GetParameterMetadataAsync(Guid id, int? group = null);

    /// <summary>批量读取参数（按地址列表；内部尽量合并为多寄存器读取以提高效率）。</summary>
    Task<LeisaiBatchReadResultDto> BatchReadParametersAsync(Guid id, LeisaiBatchReadInputDto input);

    /// <summary>批量写入参数（按项目列表；逐个 FC06 写入）。</summary>
    Task<LeisaiBatchResultDto> BatchWriteParametersAsync(Guid id, LeisaiBatchWriteInputDto input);

    /// <summary>读取指定分组的全部参数（便捷接口）。</summary>
    Task<LeisaiBatchReadResultDto> ReadGroupParametersAsync(Guid id, int group);

    /// <summary>将指定地址列表的参数写回手册默认值（不在目录中的地址会被忽略）。</summary>
    Task<LeisaiBatchResultDto> ResetParametersToDefaultAsync(
        Guid id,
        LeisaiBatchReadInputDto input
    );
}
