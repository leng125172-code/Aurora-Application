namespace AuroraStruct3D.Workflow.Dtos;

/// <summary>
/// 生成 ROI 编辑底图的预运行请求。
/// <para>
/// 语义：在不执行完整工作流的前提下，仅执行目标 ROI 节点的祖先子图（其真正依赖的上游算子），
/// 得到 ROI 节点 <c>input_mat</c> 实际接收到的图像，作为 ROI 标注底图返回给前端。
/// 天然跳过并行分支上的副作用节点（如存 Blob）。
/// </para>
/// </summary>
public class GenerateRoiBaseImageInput
{
    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>工作流 ID（当未提供 <see cref="GraphData"/> 时用于读取已保存图）。</summary>
    public Guid WorkflowId { get; set; }

    /// <summary>
    /// 当前画布 graphData（优先使用，支持未保存的编辑态预览）。
    /// 为空时回退读取 <see cref="WorkflowId"/> 对应的已保存工作流。
    /// </summary>
    public string? GraphData { get; set; }

    /// <summary>目标 ROI 节点 ID。</summary>
    public string RoiNodeId { get; set; } = string.Empty;

    /// <summary>显式输入变量绑定键（用于加载上传文件等初值）。</summary>
    public List<WorkflowVariableBindingKeyDto>? InputVariableBindings { get; set; }

    /// <summary>输入变量键（回退默认推导）。</summary>
    public List<string>? InputVariableKeys { get; set; }
}

/// <summary>
/// ROI 编辑底图预运行结果。
/// </summary>
public class RoiBaseImageResultDto
{
    /// <summary>是否出错。</summary>
    public bool Error { get; set; }

    /// <summary>错误码（出错时）。</summary>
    public string? ErrorCode { get; set; }

    /// <summary>提示信息。</summary>
    public string? Message { get; set; }

    /// <summary>底图 Blob 名称（图片统一用 blobName 引用，不返回 URL）。</summary>
    public string? BlobName { get; set; }

    /// <summary>
    /// 已写回 blobName + projectionMapping 的 ROI 节点 <c>roiJson</c>（字符串）。
    /// 前端可直接把它写回对应 ROI 节点的 <c>params.roiJson</c>；
    /// 其中 <c>baseImage.projectionMapping</c> 已含 viewLabel / 世界坐标边界 / 真实底图宽高。
    /// </summary>
    public string? RoiJson { get; set; }

    /// <summary>是否已把底图字段持久化写回已保存的工作流（仅按 workflowId 预运行时为 true）。</summary>
    public bool Persisted { get; set; }
}
