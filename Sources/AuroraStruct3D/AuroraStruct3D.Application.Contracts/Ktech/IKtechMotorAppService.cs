using AuroraStruct3D.Ktech.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace AuroraStruct3D.Ktech;

/// <summary>
/// 瓴控 KTECH 电机操作台应用服务。
/// 仅服务于 KTECH 品牌轴，覆盖 Demo 全部 5 个 Tab 的功能：
/// 产品信息 / 标定 / 设置 / 运动 / 固件升级。
/// </summary>
public interface IKtechMotorAppService : IApplicationService
{
    // ====== Info ======
    /// <summary>读取设备类型（CMD 0x1F）</summary>
    Task<int> GetDeviceTypeAsync(Guid id);

    /// <summary>读取产品信息（CMD 0x12），同步回写到 MotorAxis 实体</summary>
    Task<KtechProductInfoDto> GetProductInfoAsync(Guid id);

    /// <summary>
    /// 获取设备能力描述：支持的运动模式、只读字段、量程范围。
    /// 前端切换电机时调用一次并缓存，用于动态禁用控件与下拉过滤。
    /// 如果 axis 尚未识别出 DeviceTypeCode，将返回"未知型号"全开放能力。
    /// </summary>
    Task<KtechCapabilitiesDto> GetCapabilitiesAsync(Guid id);

    /// <summary>连接设备（CMD 0x10）</summary>
    Task ConnectAsync(Guid id);

    /// <summary>断开设备（CMD 0x11）</summary>
    Task DisconnectAsync(Guid id);

    /// <summary>重启设备（CMD 0x07）</summary>
    Task RebootDeviceAsync(Guid id);

    // ====== Calib ======
    /// <summary>读取标定参数（CMD 0x16）</summary>
    Task<KtechCalibDto> GetCalibAsync(Guid id);

    /// <summary>写入标定参数（CMD 0x17）</summary>
    Task UpdateCalibAsync(Guid id, KtechCalibDto input);

    /// <summary>对齐电机编码器（CMD 0x18）</summary>
    Task<KtechCalibDto> AlignMotorEncoderAsync(Guid id);

    /// <summary>设置编码器零点（CMD 0x19）</summary>
    Task<KtechCalibDto> SetEncoderZeroAsync(Guid id);

    /// <summary>重置标定参数（CMD 0x4A）</summary>
    Task ResetCalibAsync(Guid id);

    // ====== Setting ======
    /// <summary>读取设置参数（CMD 0x14）</summary>
    Task<KtechSettingDto> GetSettingAsync(Guid id);

    /// <summary>写入设置参数到 RAM（CMD 0x15）</summary>
    Task UpdateSettingAsync(Guid id, KtechSettingDto input);

    /// <summary>将参数持久化到 ROM（CMD 0x44）</summary>
    Task PersistSettingAsync(Guid id);

    /// <summary>重置设置参数（CMD 0x48）</summary>
    Task ResetSettingAsync(Guid id);

    /// <summary>实时写入 PID 到 RAM（CMD 0x31）</summary>
    Task WritePidRamAsync(Guid id, KtechWritePidRamInputDto input);

    // ====== Basic ======
    /// <summary>电机开启（CMD 0x88）</summary>
    Task MotorOnAsync(Guid id);

    /// <summary>电机关闭（CMD 0x80）</summary>
    Task MotorOffAsync(Guid id);

    /// <summary>电机停止（CMD 0x81）</summary>
    Task MotorStopAsync(Guid id);

    /// <summary>电机恢复（CMD 0x89）</summary>
    Task MotorRestoreAsync(Guid id);

    /// <summary>制动控制（CMD 0x8C；release=true 释放）</summary>
    Task BrakeAsync(Guid id, bool release);

    /// <summary>清除电机圈数（CMD 0x93）</summary>
    Task ClearLoopsAsync(Guid id);

    /// <summary>设置电机零点到 RAM（CMD 0x95）</summary>
    Task SetMotorZeroRamAsync(Guid id);

    /// <summary>清除电机错误标志（CMD 0x9B）</summary>
    Task ClearErrorAsync(Guid id);

    // ====== Motion ======
    /// <summary>通用运动控制（按 Mode 派发到对应命令）</summary>
    Task<KtechMotionResponseDto> MotionAsync(Guid id, KtechMotionInputDto input);

    // ====== State 单次读取（Sampler 异步推送为主，REST 用于按需触发）======
    /// <summary>读取状态 1（CMD 0x9A）</summary>
    Task<KtechStateSnapshotDto> ReadStateOnceAsync(Guid id);

    /// <summary>读取状态 3（CMD 0x9D，相电流）</summary>
    Task<KtechState3Dto> ReadState3Async(Guid id);

    /// <summary>读取多圈角度（CMD 0x92）</summary>
    Task<long> ReadMultiAngleAsync(Guid id);

    /// <summary>读取单圈角度（CMD 0x94）</summary>
    Task<uint> ReadSingleAngleAsync(Guid id);

    // ====== Firmware ======
    /// <summary>固件升级（先发 0xBB BeginIap，再走 Ymodem）</summary>
    Task UploadFirmwareAsync(Guid id, IRemoteStreamContent file);

    // ====== Sampling Control ======
    /// <summary>
    /// 获取指定轴的实时采样是否启用。
    /// 对应路由：GET /api/app/ktech-motor/{id}/sampling-enabled
    /// </summary>
    Task<bool> GetSamplingEnabledAsync(Guid id);

    /// <summary>
    /// 启用指定轴的实时采样（默认已启用）。
    /// 对应路由：POST /api/app/ktech-motor/{id}/enable-sampling
    /// </summary>
    Task EnableSamplingAsync(Guid id);

    /// <summary>
    /// 暂停指定轴的实时采样（采样器跳过该轴，不再推送 SignalR 快照）。
    /// 对应路由：POST /api/app/ktech-motor/{id}/disable-sampling
    /// </summary>
    Task DisableSamplingAsync(Guid id);
}
