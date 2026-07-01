namespace AuroraStruct3D.Workflow.Dtos;

/// <summary>
/// 示例点云信息 DTO。
/// </summary>
public class SamplePointCloudInfoDto
{
    /// <summary>点云文件名。</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>文件大小（字节）。</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>分辨率宽度。</summary>
    public int ResolutionWidth { get; set; }

    /// <summary>分辨率高度。</summary>
    public int ResolutionHeight { get; set; }

    /// <summary>Z 轴最大值。</summary>
    public double ZMax { get; set; }

    /// <summary>Z 轴最小值。</summary>
    public double ZMin { get; set; }

    /// <summary>主平面高度。</summary>
    public double MainPlaneHeight { get; set; }

    /// <summary>铆接点高度。</summary>
    public double RivetPointHeight { get; set; }

    /// <summary>立柱高度。</summary>
    public double ColumnHeight { get; set; }
}

/// <summary>
/// 模拟执行结果 DTO。
/// </summary>
public class MockExecuteResultDto
{
    /// <summary>示例点云信息。</summary>
    public SamplePointCloudInfoDto PointCloud { get; set; } = new();
}
