namespace AuroraStruct3D.OpenCV.PointCloudPlaneOps;

[Guid("c38e27a6-8d49-4c41-96a0-a53f30c23101")]
[Category("3D平面处理")]
[DisplayName("选择拟合平面")]
[Description("从输入槽位 A/B/C 的三个拟合平面中选择一个，输出统一的平面参数和内点。")]
public sealed class select_fitted_plane : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        [
            new MatImg { ParameterName = "plane_a_params", DisplayName = "平面A参数" },
            new PointCloudData { ParameterName = "plane_a_points", DisplayName = "平面A内点" },
            new MatImg { ParameterName = "plane_b_params", DisplayName = "平面B参数" },
            new PointCloudData { ParameterName = "plane_b_points", DisplayName = "平面B内点" },
            new MatImg { ParameterName = "plane_c_params", DisplayName = "平面C参数" },
            new PointCloudData { ParameterName = "plane_c_points", DisplayName = "平面C内点" },
        ];

    public static List<IVisionParameter>? OutputVisionParameters =>
        [
            new MatImg { ParameterName = "selected_plane_params", DisplayName = "选中平面参数" },
            new PointCloudData
            {
                ParameterName = "selected_plane_points",
                DisplayName = "选中平面内点",
            },
            new VisionParameter<string>
            {
                ParameterName = "selected_plane_name",
                DisplayName = "选中平面名称",
                ParameterType = typeof(string),
            },
        ];

    public static List<IConfigParameter>? ConfigParameters =>
        [
            new ConfigParameter
            {
                Name = "planeName",
                DisplayName = "选择输入槽位",
                ParameterType = typeof(string),
                DefaultValue = "A",
                ValueLimit = new[] { "A", "B", "C" },
                Required = false,
                ControlType = PortControlType.Select,
            },
        ];

    private readonly string _planeName;
    private bool _disposed;

    public select_fitted_plane(string planeName = "A")
    {
        _planeName = planeName.Trim().ToUpperInvariant();
        if (_planeName is not ("A" or "B" or "C"))
            throw new ArgumentException("平面名称必须是 A、B 或 C。", nameof(planeName));
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string suffix = _planeName.ToLowerInvariant();
        Mat plane =
            context.Get<Mat>($"plane_{suffix}_params")
            ?? throw new InvalidOperationException($"平面 {_planeName} 参数未连接。");
        PointCloudData points =
            context.Get<PointCloudData>($"plane_{suffix}_points")
            ?? throw new InvalidOperationException($"平面 {_planeName} 内点未连接。");
        if (plane.Empty() || plane.Total() < 4 || points.PointCloud is null || points.PointCloud.Empty())
            throw new InvalidOperationException($"平面 {_planeName} 数据为空。");

        context.Set("selected_plane_params", plane.Clone());
        context.Set("selected_plane_points", CloneCloud(points));
        context.Set("selected_plane_name", _planeName);
    }

    private static PointCloudData CloneCloud(PointCloudData source)
    {
        PointCloudData result = new() { Value = source.PointCloud!.Clone() };
        if (source.HasColors)
            result.SetColors(source.Colors!.Clone());
        return result;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
