using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.Motors;

/// <summary>
/// 电机故障记录实体。
/// 记录电机发生的每一次故障报警，供故障分析和维护追踪使用。
///
/// 数据库表：AbpProMotorFaultRecord
/// </summary>
public class MotorFaultRecord : Entity<Guid>
{
    /// <summary>所属电机轴ID</summary>
    public Guid MotorAxisId { get; private set; }

    /// <summary>故障发生时间（UTC）</summary>
    public DateTime OccurredAt { get; private set; }

    /// <summary>故障消除时间（UTC），null 表示尚未消除</summary>
    public DateTime? ClearedAt { get; private set; }

    /// <summary>
    /// 原始故障码（十六进制）。
    /// 瓴控KTECH：errorState 字节（0x01=低压，0x02=高压，0x04=驱动过温...）。
    /// 雷赛iCL-RS：报警寄存器值（0x00E0=过流，0x00C0=过压，0x0180=超差...）。
    /// </summary>
    public int RawFaultCode { get; private set; }

    /// <summary>故障描述（中文）</summary>
    public string FaultDescription { get; private set; } = null!;

    /// <summary>故障发生时的电机位置（脉冲数）</summary>
    public long PositionAtFault { get; private set; }

    /// <summary>故障发生时的电机速度（rpm）</summary>
    public int SpeedAtFault { get; private set; }

    /// <summary>故障发生时的母线电压（0.01V，仅瓴控KTECH有效）</summary>
    public int? BusVoltageAtFault { get; private set; }

    /// <summary>故障发生时的母线电流（0.01A，仅瓴控KTECH有效）</summary>
    public int? BusCurrentAtFault { get; private set; }

    /// <summary>故障发生时的电机温度（℃，仅瓴控KTECH有效）</summary>
    public int? TemperatureAtFault { get; private set; }

    /// <summary>是否已处理</summary>
    public bool IsHandled { get; private set; }

    /// <summary>处理备注</summary>
    public string? HandlingNote { get; private set; }

    protected MotorFaultRecord() { }

    /// <summary>
    /// 创建故障记录
    /// </summary>
    public MotorFaultRecord(
        Guid id,
        Guid motorAxisId,
        int rawFaultCode,
        string faultDescription,
        long positionAtFault,
        int speedAtFault,
        int? busVoltage = null,
        int? busCurrent = null,
        int? temperature = null
    )
        : base(id)
    {
        MotorAxisId = motorAxisId;
        OccurredAt = DateTime.UtcNow;
        RawFaultCode = rawFaultCode;
        FaultDescription = faultDescription;
        PositionAtFault = positionAtFault;
        SpeedAtFault = speedAtFault;
        BusVoltageAtFault = busVoltage;
        BusCurrentAtFault = busCurrent;
        TemperatureAtFault = temperature;
        IsHandled = false;
    }

    /// <summary>标记故障已消除</summary>
    public MotorFaultRecord MarkCleared()
    {
        ClearedAt = DateTime.UtcNow;
        return this;
    }

    /// <summary>标记已处理并记录备注</summary>
    public MotorFaultRecord MarkHandled(string? note = null)
    {
        IsHandled = true;
        HandlingNote = note;
        return this;
    }
}
