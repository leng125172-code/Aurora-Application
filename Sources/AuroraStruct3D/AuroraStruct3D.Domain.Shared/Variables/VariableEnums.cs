namespace AuroraStruct3D.Variables;

/// <summary>
/// 变量可见性。
/// </summary>
public enum VariableVisibility
{
    /// <summary>仅所属工作流可见。</summary>
    Private = 0,

    /// <summary>允许外部工作流只读导入。</summary>
    PublicRead = 1,
}

/// <summary>
/// 变量可变性。
/// </summary>
public enum VariableMutability
{
    /// <summary>可读可写。</summary>
    Mutable = 0,

    /// <summary>只读，声明后不可写。</summary>
    Readonly = 1,

    /// <summary>仅允许初始化阶段写入一次。</summary>
    ConstInit = 2,
}

/// <summary>
/// 未初始化变量读取等待策略。
/// </summary>
public enum VariableWaitPolicy
{
    /// <summary>不等待，直接失败。</summary>
    NoWait = 0,

    /// <summary>等待到可读或超时。</summary>
    Wait = 1,

    /// <summary>等待超时后返回默认值（若有）。</summary>
    WaitOrDefault = 2,
}

/// <summary>
/// 运行时变量值状态。
/// </summary>
public enum VariableValueState
{
    /// <summary>已分配槽位但未初始化。</summary>
    Uninitialized = 0,

    /// <summary>初始化中。</summary>
    Initializing = 1,

    /// <summary>可读状态。</summary>
    Ready = 2,

    /// <summary>故障态。</summary>
    Faulted = 3,

    /// <summary>已过期。</summary>
    Expired = 4,
}

/// <summary>
/// 变量离线诊断级别。
/// </summary>
public enum VariableDiagnosticSeverity
{
    /// <summary>提示。</summary>
    Info = 0,

    /// <summary>警告。</summary>
    Warning = 1,

    /// <summary>错误。</summary>
    Error = 2,
}

/// <summary>
/// 离线 Def-Use 分析模式。
/// </summary>
public enum VariableDefUseAnalysisMode
{
    /// <summary>关闭 Def-Use 分析。</summary>
    Disabled = 0,

    /// <summary>保守模式，仅在可确定读先于写时报错。</summary>
    Conservative = 1,

    /// <summary>严格模式，顺序不可判定时也按风险报错。</summary>
    Strict = 2,
}
