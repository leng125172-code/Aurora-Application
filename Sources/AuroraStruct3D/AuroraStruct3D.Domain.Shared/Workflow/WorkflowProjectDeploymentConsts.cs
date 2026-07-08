namespace AuroraStruct3D.Workflow;

/// <summary>
/// 项目部署快照常量。
/// </summary>
public static class WorkflowProjectDeploymentConsts
{
    /// <summary>快照 JSON 最大长度。</summary>
    public const int MaxSnapshotJsonLength = 131072;

    /// <summary>快照哈希最大长度。</summary>
    public const int MaxSnapshotHashLength = 128;

    /// <summary>冻结工作流图 JSON 最大长度（包含全量 GraphData）。</summary>
    public const int MaxFrozenGraphsJsonLength = 4194304;

    /// <summary>冻结变量定义 JSON 最大长度。</summary>
    public const int MaxFrozenVariablesJsonLength = 1048576;
}
