namespace AuroraStruct3D.Cameras.Dtos;

/// <summary>
/// GenICam NodeMap 快照（前端用于动态生成参数 UI）
/// </summary>
public class CameraNodeMapDto
{
    /// <summary>相机设备 ID</summary>
    public Guid CameraId { get; set; }

    /// <summary>NodeMap 枚举时间（UTC）</summary>
    public DateTime EnumeratedAt { get; set; }

    /// <summary>按 Category 树聚合的节点</summary>
    public List<GenICamCategoryDto> Categories { get; set; } = new();

    /// <summary>原始扁平节点列表（前端可直接索引）</summary>
    public List<GenICamNodeDto> AllNodes { get; set; } = new();

    /// <summary>选择器依赖图（Selector → 受影响节点集合）</summary>
    public List<GenICamDependencyDto> Dependencies { get; set; } = new();
}

/// <summary>
/// GenICam Category（按 Level 折叠生成的层级容器，支持多级嵌套）
/// </summary>
public class GenICamCategoryDto
{
    /// <summary>Category 节点名称（若无明确 Category 父节点则为空字符串）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Category 显示名称</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>该 Category 下包含的直属叶子节点</summary>
    public List<GenICamNodeDto> Nodes { get; set; } = new();

    /// <summary>子 Category 列表（对应 GenICam Category 树的嵌套层级）</summary>
    public List<GenICamCategoryDto> Children { get; set; } = new();
}

/// <summary>
/// GenICam 节点 DTO（包含静态元数据与首次枚举时的值快照）
/// </summary>
public class GenICamNodeDto
{
    /// <summary>节点名称（程序名）</summary>
    public string NodeName { get; set; } = string.Empty;

    /// <summary>显示名称</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>所属 XML 域：0=TU_CAMERA_XML，3=TU_CAMERABASE_XML</summary>
    public int XmlScope { get; set; }

    /// <summary>层级（Category 树深度）</summary>
    public byte Level { get; set; }

    /// <summary>节点类型（Integer/Float/Enumeration/Boolean/String/Command/Category）</summary>
    public string NodeType { get; set; } = string.Empty;

    /// <summary>访问模式（ReadOnly/WriteOnly/ReadWrite/NotImplemented/NotAvailable）</summary>
    public string Access { get; set; } = string.Empty;

    /// <summary>可见性（Beginner/Expert/Guru/Invisible）</summary>
    public string Visibility { get; set; } = string.Empty;

    /// <summary>表示方式（SDK 原始值，前端用于决定滑块/线性/对数等）</summary>
    public ushort Representation { get; set; }

    /// <summary>单位（如 us / dB）</summary>
    public string? Unit { get; set; }

    /// <summary>描述</summary>
    public string? Description { get; set; }

    /// <summary>是否被锁定（运行态不可写）</summary>
    public bool IsLocked { get; set; }

    /// <summary>整数最小值（Integer/Enumeration 有效）</summary>
    public long IntMin { get; set; }

    /// <summary>整数最大值</summary>
    public long IntMax { get; set; }

    /// <summary>整数步进</summary>
    public long IntStep { get; set; }

    /// <summary>浮点最小值</summary>
    public double FloatMin { get; set; }

    /// <summary>浮点最大值</summary>
    public double FloatMax { get; set; }

    /// <summary>浮点步进</summary>
    public double FloatStep { get; set; }

    /// <summary>当前值（字符串统一表示；浮点 InvariantCulture）</summary>
    public string? CurrentValue { get; set; }

    /// <summary>枚举条目（仅 Enumeration / Boolean 有效）</summary>
    public List<GenICamEnumEntryDto> EnumEntries { get; set; } = new();

    /// <summary>轮询时间（毫秒）</summary>
    public long PollingTime { get; set; }

    /// <summary>浮点显示精度</summary>
    public long DisplayPrecision { get; set; }
}

/// <summary>
/// GenICam 枚举条目 DTO
/// </summary>
public class GenICamEnumEntryDto
{
    /// <summary>整数值</summary>
    public long Value { get; set; }

    /// <summary>程序名（symbolic）</summary>
    public string Symbolic { get; set; } = string.Empty;

    /// <summary>显示名称</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>当前模式下是否可用（true 表示可选择）</summary>
    public bool IsAvailable { get; set; }
}

/// <summary>
/// 选择器依赖项：当 SelectorNode 切换到 OptionValue 时，AffectedNode 的属性会变化
/// </summary>
public class GenICamDependencyDto
{
    /// <summary>触发依赖的选择器节点</summary>
    public string SelectorNode { get; set; } = string.Empty;

    /// <summary>选择器的整数值</summary>
    public long OptionValue { get; set; }

    /// <summary>选择器的枚举标签</summary>
    public string OptionLabel { get; set; } = string.Empty;

    /// <summary>受影响的目标节点</summary>
    public string AffectedNode { get; set; } = string.Empty;

    /// <summary>变化摘要（Access/Visibility/Value/Range 之一或多个）</summary>
    public string ChangeSummary { get; set; } = string.Empty;

    /// <summary>
    /// 切换到该选项后，受影响节点的新 Access 状态（仅在含 Access 变化时有值，
    /// 如 "ReadOnly" / "ReadWrite" / "NotAvailable"）。
    /// 前端写节点成功后可直接用此字段本地更新 access，无需重新枚举。
    /// </summary>
    public string? NewAccess { get; set; }
}

/// <summary>
/// 相机实时状态 DTO（通过 SignalR 推送给前端）
/// </summary>
public class CameraStateDto
{
    /// <summary>相机设备 ID</summary>
    public Guid CameraId { get; set; }

    /// <summary>状态枚举值（CameraStatus）</summary>
    public int Status { get; set; }

    /// <summary>状态文本（Disconnected / Connected / Ready / Capturing / Error 等）</summary>
    public string StatusText { get; set; } = string.Empty;

    /// <summary>是否正在采集</summary>
    public bool IsCapturing { get; set; }

    /// <summary>NodeMap 是否已加载完成（预跑结束后置 true）</summary>
    public bool IsXmlLoaded { get; set; }

    /// <summary>传感器温度（可空）</summary>
    public double? SensorTemperature { get; set; }

    /// <summary>FPGA 温度（可空）</summary>
    public double? FpgaTemperature { get; set; }

    /// <summary>最近错误信息（无错误时为 null 或空串）</summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>状态变更时间（UTC）</summary>
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
