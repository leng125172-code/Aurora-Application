using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using AuroraStruct3D.Motors;
using AuroraStruct3D.SerialPorts;
using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.Motors.Dtos;

/// <summary>
/// 电机轴输出 DTO。
/// </summary>
public class MotorAxisDto : EntityDto<Guid>
{
    /// <summary>轴名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>轴序号</summary>
    public int AxisIndex { get; set; }

    /// <summary>描述</summary>
    public string? Description { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; }

    /// <summary>串口配置 ID</summary>
    public Guid SerialPortConfigId { get; set; }

    /// <summary>串口显示名</summary>
    public string SerialPortDisplayName { get; set; } = string.Empty;

    /// <summary>系统串口号</summary>
    public string PortName { get; set; } = string.Empty;

    /// <summary>串口当前波特率</summary>
    public int BaudRate { get; set; }

    /// <summary>串口运行态是否已打开</summary>
    public bool IsSerialPortOpen { get; set; }

    /// <summary>从机地址</summary>
    public int SlaveId { get; set; }

    /// <summary>品牌协议</summary>
    public MotorBrand Brand { get; set; }

    /// <summary>品牌显示文本</summary>
    public string BrandText { get; set; } = string.Empty;

    /// <summary>型号</summary>
    public string? Model { get; set; }

    /// <summary>运行状态</summary>
    public MotorDeviceStatus Status { get; set; }

    /// <summary>运行状态文本</summary>
    public string StatusText { get; set; } = string.Empty;

    /// <summary>是否已回零</summary>
    public bool IsHomed { get; set; }

    /// <summary>最后状态更新时间</summary>
    public DateTime? LastStatusUpdateAt { get; set; }

    /// <summary>旋转角度最小值（瓴控单圈角度，0.01°单位）</summary>
    public long? MinRotationAngle { get; set; }

    /// <summary>旋转角度最大值（瓴控单圈角度，0.01°单位）</summary>
    public long? MaxRotationAngle { get; set; }
}

/// <summary>
/// 旋转角度边界类型。
/// </summary>
public enum MotorRotationAngleLimitKind
{
    /// <summary>最小角度</summary>
    Minimum = 0,

    /// <summary>最大角度</summary>
    Maximum = 1,
}

/// <summary>
/// 设置电机旋转角度范围 DTO。
/// </summary>
public class SetMotorRotationAngleRangeDto
{
    /// <summary>旋转角度最小值（瓴控单圈角度，0.01°单位）</summary>
    public long? MinRotationAngle { get; set; }

    /// <summary>旋转角度最大值（瓴控单圈角度，0.01°单位）</summary>
    public long? MaxRotationAngle { get; set; }
}

/// <summary>
/// 将当前单圈角度设置为指定边界 DTO。
/// </summary>
public class SetMotorRotationAngleLimitFromCurrentDto
{
    /// <summary>要写入的边界类型</summary>
    public MotorRotationAngleLimitKind LimitKind { get; set; }
}

/// <summary>
/// 电机列表查询 DTO。
/// </summary>
public class GetMotorAxisListDto : PagedAndSortedResultRequestDto
{
    /// <summary>名称、串口或从机地址过滤</summary>
    public string? Filter { get; set; }

    /// <summary>启用状态过滤</summary>
    public bool? IsEnabled { get; set; }

    /// <summary>是否刷新硬件状态</summary>
    public bool RefreshHardware { get; set; } = true;
}

/// <summary>
/// 更新电机轴基础信息 DTO。
/// </summary>
public class UpdateMotorAxisDto
{
    /// <summary>轴名称</summary>
    [Required]
    [MaxLength(MotorConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>描述</summary>
    [MaxLength(MotorConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>型号</summary>
    [MaxLength(MotorConsts.MaxModelLength)]
    public string? Model { get; set; }
}

/// <summary>
/// 扫描电机设备 DTO。
/// </summary>
public class ScanMotorDevicesInput
{
    /// <summary>起始从机地址</summary>
    [Range(1, 32)]
    public int StartSlaveId { get; set; } = 1;

    /// <summary>结束从机地址</summary>
    [Range(1, 32)]
    public int EndSlaveId { get; set; } = 32;

    /// <summary>本次扫描波特率列表，为空则使用电机协议推荐波特率</summary>
    public List<int> BaudRates { get; set; } = new();

    /// <summary>单次探测超时毫秒</summary>
    [Range(20, 2000)]
    public int ProbeTimeoutMs { get; set; } = 80;
}

/// <summary>
/// 扫描发现的电机设备 DTO。
/// </summary>
public class DiscoveredMotorDeviceDto
{
    /// <summary>串口配置 ID</summary>
    public Guid SerialPortConfigId { get; set; }

    /// <summary>系统串口名</summary>
    public string PortName { get; set; } = string.Empty;

    /// <summary>波特率</summary>
    public int BaudRate { get; set; }

    /// <summary>从机地址</summary>
    public int SlaveId { get; set; }

    /// <summary>品牌协议</summary>
    public MotorBrand Brand { get; set; }

    /// <summary>品牌显示文本</summary>
    public string BrandText { get; set; } = string.Empty;

    /// <summary>数据库电机轴 ID</summary>
    public Guid MotorAxisId { get; set; }
}

/// <summary>
/// 电机扫描结果 DTO。
/// </summary>
public class ScanMotorDevicesResultDto
{
    /// <summary>尝试探测次数</summary>
    public int TriedCount { get; set; }

    /// <summary>发现数量</summary>
    public int FoundCount { get; set; }

    /// <summary>耗时毫秒</summary>
    public long ElapsedMs { get; set; }

    /// <summary>发现的设备列表</summary>
    public List<DiscoveredMotorDeviceDto> Items { get; set; } = new();
}

/// <summary>
/// 电机运动输入 DTO。
/// </summary>
public class MoveMotorInput
{
    /// <summary>位置或位移量</summary>
    public long Position { get; set; }

    /// <summary>速度 RPM，0 表示使用设备默认值</summary>
    public int SpeedRpm { get; set; }
}

/// <summary>
/// Modbus 读保持寄存器 DTO。
/// </summary>
public class ReadHoldingRegistersInput
{
    /// <summary>起始寄存器地址</summary>
    public ushort StartAddress { get; set; }

    /// <summary>读取数量</summary>
    [Range(1, 64)]
    public ushort Quantity { get; set; } = 1;
}

/// <summary>
/// Modbus 写单寄存器 DTO。
/// </summary>
public class WriteSingleRegisterInput
{
    /// <summary>寄存器地址</summary>
    public ushort Address { get; set; }

    /// <summary>寄存器值</summary>
    public ushort Value { get; set; }
}

/// <summary>
/// 寄存器读取结果 DTO。
/// </summary>
public class RegisterReadResultDto
{
    /// <summary>请求帧十六进制</summary>
    public string RequestHex { get; set; } = string.Empty;

    /// <summary>响应帧十六进制</summary>
    public string ResponseHex { get; set; } = string.Empty;

    /// <summary>寄存器值列表</summary>
    public List<ushort> Values { get; set; } = new();
}

/// <summary>
/// 电机操作日志输出 DTO。
/// </summary>
public class MotorOperationLogDto : EntityDto<Guid>
{
    /// <summary>所属电机轴 ID</summary>
    public Guid MotorAxisId { get; set; }

    /// <summary>Modbus 从机地址</summary>
    public int SlaveId { get; set; }

    /// <summary>操作类型</summary>
    public MotorOperationType OperationType { get; set; }

    /// <summary>操作发生时间（UTC）</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>是否成功</summary>
    public bool IsSuccess { get; set; }

    /// <summary>协议命令标识</summary>
    public string? CommandCode { get; set; }

    /// <summary>操作参数摘要</summary>
    public string? ParameterSummary { get; set; }

    /// <summary>失败时的错误消息</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>命令往返耗时（毫秒），-1 表示未记录</summary>
    public long RoundTripMs { get; set; }
}

/// <summary>
/// 查询电机操作日志请求 DTO。
/// </summary>
public class GetMotorLogListDto : Volo.Abp.Application.Dtos.PagedResultRequestDto
{
    /// <summary>电机轴 ID（必填，无 ID 则返回空结果）</summary>
    public Guid? MotorAxisId { get; set; }

    /// <summary>按操作类型过滤（可选）</summary>
    public MotorOperationType? OperationType { get; set; }

    /// <summary>仅返回失败记录</summary>
    public bool? IsFailedOnly { get; set; }

    /// <summary>开始时间（可选，UTC）</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>结束时间（可选，UTC）</summary>
    public DateTime? EndTime { get; set; }
}
