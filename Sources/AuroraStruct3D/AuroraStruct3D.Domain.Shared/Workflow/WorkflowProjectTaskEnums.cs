namespace AuroraStruct3D.Workflow;

/// <summary>
/// 工作流任务（执行配置）触发类型。
/// <para>对应 CodeSYS 任务的触发方式；当前运行触发在“运行”入队级控制，此字段为任务级预留。</para>
/// </summary>
public enum WorkflowProjectTaskType
{
    /// <summary>立即触发（手动启动一次）。</summary>
    Immediate = 0,

    /// <summary>周期触发（按间隔重复）。</summary>
    Cyclic = 1,
}
