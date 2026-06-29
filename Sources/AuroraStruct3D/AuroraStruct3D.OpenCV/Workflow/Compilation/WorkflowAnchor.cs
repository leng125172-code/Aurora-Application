namespace AuroraStruct3D.OpenCV.Workflow.Compilation;

/// <summary>
/// 统一锚点方位约定，与前端 <c>workflowAnchor.ts</c> 对齐。
/// 边的 <c>sourceAnchorIndex</c> / <c>targetAnchorIndex</c> 取此索引（纯视觉路由信息，
/// 不影响执行；执行拓扑与分支由 sourceNodeId/targetNodeId/branch 决定）。
/// </summary>
public static class WorkflowAnchor
{
    /// <summary>上。</summary>
    public const int Top = 0;

    /// <summary>右。</summary>
    public const int Right = 1;

    /// <summary>下。</summary>
    public const int Bottom = 2;

    /// <summary>左。</summary>
    public const int Left = 3;

    /// <summary>锚点索引是否合法（0=上 / 1=右 / 2=下 / 3=左）。</summary>
    public static bool IsValidIndex(int index) => index is >= Top and <= Left;
}
