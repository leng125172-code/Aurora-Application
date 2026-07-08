namespace AuroraStruct3D.Workflow;

/// <summary>
/// 项目级工作流运行实例常量。
/// </summary>
public static class WorkflowProjectRunConsts
{
    /// <summary>运行名称最大长度。</summary>
    public const int MaxNameLength = 128;

    /// <summary>Hangfire JobId 最大长度。</summary>
    public const int MaxHangfireJobIdLength = 64;

    /// <summary>错误信息最大长度。</summary>
    public const int MaxErrorLength = 2048;

    /// <summary>工作流 ID 列表 JSON 最大长度。</summary>
    public const int MaxWorkflowIdsJsonLength = 16384;

    /// <summary>执行结果 JSON 最大长度。</summary>
    public const int MaxResultsJsonLength = 131072;
}
