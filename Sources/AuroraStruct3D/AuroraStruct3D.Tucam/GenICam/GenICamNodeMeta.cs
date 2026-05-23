using AuroraStruct3D.Tucam.Interop;

namespace AuroraStruct3D.Tucam.GenICam;

/// <summary>
/// GenICam 节点元数据（从相机 NodeMap 动态枚举得到的单条节点信息）
/// </summary>
public sealed class GenICamNodeMeta
{
    /// <summary>节点在枚举序列中的索引（仅用于稳定排序）</summary>
    public int Index { get; init; }

    /// <summary>所属 XML 域：0 = TU_CAMERA_XML，3 = TU_CAMERABASE_XML</summary>
    public int XmlScope { get; init; }

    /// <summary>节点在 Category 树中的层级</summary>
    public byte Level { get; init; }

    /// <summary>节点名称（GenICam 节点的程序名，区分大小写）</summary>
    public string NodeName { get; init; } = string.Empty;

    /// <summary>显示名称（人类可读）</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>节点类型（Integer / Float / Enumeration / Boolean / String / Command / Category 等）</summary>
    public TuElemType Type { get; init; }

    /// <summary>访问模式</summary>
    public TuAccessMode Access { get; init; }

    /// <summary>可见性</summary>
    public TuVisibility Visibility { get; init; }

    /// <summary>表示方式（线性/对数/IPv4 等，由 SDK 返回的原始值）</summary>
    public ushort Representation { get; init; }

    /// <summary>单位字符串（如 "us"、"dB"）</summary>
    public string? Unit { get; init; }

    /// <summary>节点描述（来自 SDK）</summary>
    public string? Description { get; init; }

    /// <summary>是否被锁定（运行态不可写）</summary>
    public bool IsLocked { get; init; }

    /// <summary>整数型最小值（仅 Integer/Enumeration 有效）</summary>
    public long IntMin { get; init; }

    /// <summary>整数型最大值</summary>
    public long IntMax { get; init; }

    /// <summary>整数型步进</summary>
    public long IntStep { get; init; }

    /// <summary>浮点型最小值</summary>
    public double FloatMin { get; init; }

    /// <summary>浮点型最大值</summary>
    public double FloatMax { get; init; }

    /// <summary>浮点型步进</summary>
    public double FloatStep { get; init; }

    /// <summary>当前值（统一转字符串：浮点用 InvariantCulture）</summary>
    public string? CurrentValue { get; init; }

    /// <summary>枚举条目列表（仅 Enumeration / Boolean 有效）</summary>
    public IReadOnlyList<GenICamEnumEntry> EnumEntries { get; init; } =
        Array.Empty<GenICamEnumEntry>();

    /// <summary>轮询间隔（毫秒，0 表示无周期刷新需求）</summary>
    public long PollingTime { get; init; }

    /// <summary>浮点显示精度</summary>
    public long DisplayPrecision { get; init; }
}

/// <summary>
/// GenICam 枚举条目
/// </summary>
public sealed class GenICamEnumEntry
{
    /// <summary>整数值</summary>
    public long Value { get; init; }

    /// <summary>程序名（symbolic）</summary>
    public string Symbolic { get; init; } = string.Empty;

    /// <summary>显示名称</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>当前模式下是否可用</summary>
    public bool IsAvailable { get; init; }
}

/// <summary>
/// 一次性的 NodeMap 快照（含枚举结果与缓存时间）
/// </summary>
public sealed class GenICamNodeMap
{
    /// <summary>枚举完成时间（UTC）</summary>
    public DateTime EnumeratedAt { get; init; }

    /// <summary>全部节点（按 Index 排序）</summary>
    public IReadOnlyList<GenICamNodeMeta> Nodes { get; init; } = Array.Empty<GenICamNodeMeta>();

    /// <summary>按节点名称快速索引</summary>
    public IReadOnlyDictionary<string, GenICamNodeMeta> NodesByName { get; init; } =
        new Dictionary<string, GenICamNodeMeta>(StringComparer.Ordinal);
}

/// <summary>
/// 选择器依赖一条边（修改某选择器为某个选项时受影响的另一节点）
/// </summary>
public sealed class GenICamDependencyEdge
{
    /// <summary>触发依赖的选择器节点</summary>
    public string SelectorNode { get; init; } = string.Empty;

    /// <summary>选择器写入的整数值</summary>
    public long OptionValue { get; init; }

    /// <summary>选择器写入的枚举标签</summary>
    public string OptionLabel { get; init; } = string.Empty;

    /// <summary>受影响的目标节点名</summary>
    public string AffectedNode { get; init; } = string.Empty;

    /// <summary>变化摘要（Access/Visibility/Value/Range/EnumEntries 之一或多个）</summary>
    public string ChangeSummary { get; init; } = string.Empty;
}

/// <summary>
/// 选择器依赖图：按选择器节点名聚合受影响节点集合
/// </summary>
public sealed class GenICamDependencyGraph
{
    /// <summary>探测完成时间（UTC）</summary>
    public DateTime ProbedAt { get; init; }

    /// <summary>原始依赖边列表</summary>
    public IReadOnlyList<GenICamDependencyEdge> Edges { get; init; } =
        Array.Empty<GenICamDependencyEdge>();

    /// <summary>选择器 → 受影响节点集合（去重）</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> AffectedBySelector { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
}
