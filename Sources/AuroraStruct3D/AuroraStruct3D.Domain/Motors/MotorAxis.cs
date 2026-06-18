using AuroraStruct3D.SerialPorts;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Motors;

/// <summary>
/// 伺服电机轴设备聚合根。
/// 对应总线上一个从机地址的物理电机轴，存储其连接配置和运行状态快照。
///
/// 数据库表：AbpProMotorAxis
/// </summary>
public class MotorAxis : FullAuditedAggregateRoot<Guid>
{
    // ─────────────────────────── 基本信息 ───────────────────────────

    /// <summary>轴名称（如"X轴"、"升降轴"）</summary>
    public string Name { get; private set; } = null!;

    /// <summary>轴序号，用于排序显示（0=第一轴）</summary>
    public int AxisIndex { get; private set; }

    /// <summary>描述/备注</summary>
    public string? Description { get; private set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; private set; }

    // ─────────────────────────── 硬件连接配置 ───────────────────────────

    /// <summary>关联的串口通讯配置 ID（多个电机轴可共享同一 RS485 总线）</summary>
    public Guid SerialPortConfigId { get; private set; }

    /// <summary>关联的串口通讯配置导航属性（EF Core 加载用）</summary>
    public SerialPortConfig? SerialPortConfig { get; private set; }

    /// <summary>Modbus / 私有协议 从机地址（1~247）</summary>
    public int SlaveId { get; private set; }

    /// <summary>电机品牌及协议类型</summary>
    public MotorBrand Brand { get; private set; }

    /// <summary>电机型号（如 iCL42-RS06、MF4005 等，可选）</summary>
    public string? Model { get; private set; }

    // ─────────────────────────── KTECH 设备信息（在线读取后缓存，仅 KTECH 品牌有效） ───────────────────────────

    /// <summary>KTECH 设备类型代码（CMD 0x1F 返回的 ushort，如 MS=8209/MF=8225/MG=8241）</summary>
    public int? KtechDeviceTypeCode { get; private set; }

    /// <summary>驱动器名称（CMD 0x12 返回的前 20 字节 ASCII）</summary>
    public string? KtechDriverName { get; private set; }

    /// <summary>电机名称（CMD 0x12 返回的 21~40 字节 ASCII）</summary>
    public string? KtechMotorName { get; private set; }

    /// <summary>芯片 ID（CMD 0x12 返回的 41~52 字节 hex 字符串）</summary>
    public string? KtechChipId { get; private set; }

    /// <summary>硬件版本（如 V1.0）</summary>
    public string? KtechHardwareVersion { get; private set; }

    /// <summary>电机版本（如 V1.0）</summary>
    public string? KtechMotorVersion { get; private set; }

    /// <summary>固件版本（如 V1.0）</summary>
    public string? KtechFirmwareVersion { get; private set; }

    // ─────────────────────────── 回原点配置 ───────────────────────────

    /// <summary>回零方式</summary>
    public HomeMethod HomeMethod { get; private set; }

    /// <summary>回零方向：true=正向，false=反向</summary>
    public bool HomeDirection { get; private set; }

    /// <summary>回零高速（rpm）</summary>
    public int HomeHighSpeedRpm { get; private set; }

    /// <summary>回零低速（rpm）</summary>
    public int HomeLowSpeedRpm { get; private set; }

    /// <summary>回零加速时间（ms/1000rpm）</summary>
    public int HomeAccTimeMs { get; private set; }

    /// <summary>回零减速时间（ms/1000rpm）</summary>
    public int HomeDecTimeMs { get; private set; }

    // ─────────────────────────── 软件限位 ───────────────────────────

    /// <summary>软件限位是否启用</summary>
    public bool SoftLimitEnabled { get; private set; }

    /// <summary>正向软件限位（脉冲数）</summary>
    public long SoftLimitPositive { get; private set; }

    /// <summary>负向软件限位（脉冲数）</summary>
    public long SoftLimitNegative { get; private set; }

    /// <summary>旋转角度最小值（瓴控单圈角度，0.01°单位）</summary>
    public long? MinRotationAngle { get; private set; }

    /// <summary>旋转角度最大值（瓴控单圈角度，0.01°单位）</summary>
    public long? MaxRotationAngle { get; private set; }

    // ─────────────────────────── 运行状态快照（实时刷新，不做历史追踪） ───────────────────────────

    /// <summary>当前设备状态</summary>
    public MotorDeviceStatus Status { get; private set; }

    /// <summary>最后一次查询时的当前位置（脉冲数）</summary>
    public long LastKnownPosition { get; private set; }

    /// <summary>最后一次查询时的当前速度（rpm 或 dps，取决于品牌）</summary>
    public int LastKnownSpeed { get; private set; }

    /// <summary>是否已完成回零</summary>
    public bool IsHomed { get; private set; }

    /// <summary>最后一次状态更新时间（UTC）</summary>
    public DateTime? LastStatusUpdateAt { get; private set; }

    // ─────────────────────────── 关联数据 ───────────────────────────

    /// <summary>当前激活的运动配置ID（可为空，表示使用默认值）</summary>
    public Guid? ActiveMotionConfigId { get; private set; }

    /// <summary>该轴下的所有运动配置（含PID、速度限制等）</summary>
    public ICollection<MotorMotionConfig> MotionConfigs { get; private set; } =
        new List<MotorMotionConfig>();

    /// <summary>故障历史记录（最近N条）</summary>
    public ICollection<MotorFaultRecord> FaultRecords { get; private set; } =
        new List<MotorFaultRecord>();

    // ─────────────────────────── 构造函数 ───────────────────────────

    // EF Core 所需的无参构造函数
    protected MotorAxis() { }

    /// <summary>
    /// 创建电机轴
    /// </summary>
    /// <param name="id">聚合根ID</param>
    /// <param name="name">轴名称</param>
    /// <param name="axisIndex">轴序号</param>
    /// <param name="serialPortConfigId">关联的串口通讯配置 ID</param>
    /// <param name="slaveId">从机地址（1~247）</param>
    /// <param name="brand">品牌协议类型</param>
    public MotorAxis(
        Guid id,
        string name,
        int axisIndex,
        Guid serialPortConfigId,
        int slaveId,
        MotorBrand brand
    )
        : base(id)
    {
        SetName(name);
        AxisIndex = axisIndex;
        SerialPortConfigId = serialPortConfigId;
        SlaveId = slaveId;
        Brand = brand;

        // 按品牌设置默认回零方式
        HomeMethod =
            brand == MotorBrand.KtechKtech
                ? HomeMethod.HardLimit
                : HomeMethod.PhotoelectricSwitchDI;
        HomeDirection = false;
        HomeHighSpeedRpm = 200;
        HomeLowSpeedRpm = 50;
        HomeAccTimeMs = 100;
        HomeDecTimeMs = 100;

        SoftLimitEnabled = false;
        SoftLimitPositive = int.MaxValue;
        SoftLimitNegative = int.MinValue;
        MinRotationAngle = null;
        MaxRotationAngle = null;

        Status = MotorDeviceStatus.Unknown;
        IsEnabled = true;
        IsHomed = false;
    }

    // ─────────────────────────── 领域方法 ───────────────────────────

    /// <summary>设置轴名称</summary>
    public MotorAxis SetName(string name)
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), MotorConsts.MaxNameLength);
        Name = name;
        return this;
    }

    /// <summary>设置型号</summary>
    public MotorAxis SetModel(string? model)
    {
        if (model != null)
        {
            Check.Length(model, nameof(model), MotorConsts.MaxModelLength);
        }
        Model = model;
        return this;
    }

    /// <summary>设置描述</summary>
    public MotorAxis SetDescription(string? description)
    {
        if (description != null)
        {
            Check.Length(description, nameof(description), MotorConsts.MaxDescriptionLength);
        }
        Description = description;
        return this;
    }

    /// <summary>配置回原点参数</summary>
    public MotorAxis ConfigureHome(
        HomeMethod homeMethod,
        bool homeDirection,
        int highSpeedRpm,
        int lowSpeedRpm,
        int accTimeMs,
        int decTimeMs
    )
    {
        HomeMethod = homeMethod;
        HomeDirection = homeDirection;
        HomeHighSpeedRpm = highSpeedRpm;
        HomeLowSpeedRpm = lowSpeedRpm;
        HomeAccTimeMs = accTimeMs;
        HomeDecTimeMs = decTimeMs;
        return this;
    }

    /// <summary>配置软件限位</summary>
    public MotorAxis ConfigureSoftLimit(bool enabled, long positive, long negative)
    {
        SoftLimitEnabled = enabled;
        SoftLimitPositive = positive;
        SoftLimitNegative = negative;
        return this;
    }

    /// <summary>设置瓴控单圈旋转角度范围</summary>
    public MotorAxis SetRotationAngleRange(long? minRotationAngle, long? maxRotationAngle)
    {
        ValidateRotationAngle(minRotationAngle);
        ValidateRotationAngle(maxRotationAngle);

        MinRotationAngle = minRotationAngle;
        MaxRotationAngle = maxRotationAngle;
        return this;
    }

    /// <summary>更新运行状态快照（由后台服务定期调用）</summary>
    public MotorAxis UpdateStatus(MotorDeviceStatus status, long position, int speed, bool isHomed)
    {
        Status = status;
        LastKnownPosition = position;
        LastKnownSpeed = speed;
        IsHomed = isHomed;
        LastStatusUpdateAt = DateTime.UtcNow;
        return this;
    }

    /// <summary>变更关联的串口通讯配置</summary>
    public MotorAxis SetSerialPortConfig(Guid serialPortConfigId)
    {
        SerialPortConfigId = serialPortConfigId;
        return this;
    }

    /// <summary>切换激活的运动配置</summary>
    public MotorAxis SetActiveMotionConfig(Guid? configId)
    {
        ActiveMotionConfigId = configId;
        return this;
    }

    /// <summary>标记回零完成</summary>
    public MotorAxis MarkHomed()
    {
        IsHomed = true;
        Status = MotorDeviceStatus.Enabled;
        return this;
    }

    /// <summary>切换启用状态</summary>
    public MotorAxis SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        return this;
    }

    /// <summary>更新 KTECH 设备识别信息（来自 CMD 0x1F + CMD 0x12 的读取结果，可分次更新）</summary>
    public MotorAxis SetKtechDeviceInfo(
        int? deviceTypeCode = null,
        string? driverName = null,
        string? motorName = null,
        string? chipId = null,
        string? hardwareVersion = null,
        string? motorVersion = null,
        string? firmwareVersion = null
    )
    {
        if (deviceTypeCode.HasValue)
        {
            KtechDeviceTypeCode = deviceTypeCode.Value;
        }
        if (driverName != null)
        {
            KtechDriverName = driverName;
        }
        if (motorName != null)
        {
            KtechMotorName = motorName;
        }
        if (chipId != null)
        {
            KtechChipId = chipId;
        }
        if (hardwareVersion != null)
        {
            KtechHardwareVersion = hardwareVersion;
        }
        if (motorVersion != null)
        {
            KtechMotorVersion = motorVersion;
        }
        if (firmwareVersion != null)
        {
            KtechFirmwareVersion = firmwareVersion;
        }
        return this;
    }

    private static void ValidateRotationAngle(long? angle)
    {
        if (!angle.HasValue)
        {
            return;
        }

        if (angle.Value < 0 || angle.Value > MotorConsts.KtechSingleTurnAngleUnits)
        {
            throw new ArgumentException(
                $"旋转角度必须在 0~{MotorConsts.KtechSingleTurnAngleUnits}（0.01°）之间"
            );
        }
    }
}
