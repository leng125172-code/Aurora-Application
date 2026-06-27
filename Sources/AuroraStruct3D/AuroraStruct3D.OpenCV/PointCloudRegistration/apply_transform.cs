using AuroraStruct3D.OpenCV.Common;

namespace AuroraStruct3D.OpenCV.PointCloudRegistration;

/// <summary>
/// 工作流算子：对点云施加 4×4 刚体/仿射变换。
/// <para>
/// 逐点计算 <c>p' = R·p + t</c>，其中 R 为变换矩阵左上 3×3、t 为第 4 列前三行。
/// 常用于将配准算子（ICP、特征粗配准等）求得的变换矩阵实际作用到点云上，
/// 或在工作流中复用一个已知变换。
/// </para>
/// <para>
/// 颜色数据原样保留；若点云含法向量列（第 3-5 列，共 ≥6 列），法向量仅用 R 旋转（不平移）。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 待变换的点云</item>
///   <item>输入 <c>transform_matrix</c>（Mat）— 4×4 变换矩阵（CV_64FC1）</item>
///   <item>输出 <c>output_point_cloud</c>（PointCloudData）— 变换后的点云</item>
/// </list>
/// </para>
/// </summary>
[Guid("b1a10005-0005-4000-8000-000000000035")]
[Category("3D配准")]
[DisplayName("应用变换")]
[Description("把算好的变换矩阵真正作用到点云上，配准后落位用。")]
public class apply_transform : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
            new MatImg() { ParameterName = "transform_matrix", DisplayName = "变换矩阵" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new PointCloudData()
            {
                ParameterName = "output_point_cloud",
                DisplayName = "输出点云",
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "invert",
                DisplayName = "求逆变换",
                ParameterType = typeof(bool),
                DefaultValue = "false",
                Required = false,
                ControlType = PortControlType.Switch,
            },
        };

    private readonly bool _invert;
    private bool _disposed;

    /// <summary>
    /// 初始化应用变换算子。
    /// </summary>
    /// <param name="invert">为 true 时施加变换矩阵的逆变换。</param>
    public apply_transform(bool invert = false)
    {
        _invert = invert;
    }

    /// <inheritdoc/>
    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PointCloudData input =
            context.Get<PointCloudData>("input_point_cloud")
            ?? throw new InvalidOperationException(
                "上下文变量 'input_point_cloud' 为空，请确认输入绑定已正确设置。"
            );

        Mat? transform = context.Get<Mat>("transform_matrix");
        if (transform is null || transform.Empty())
            throw new InvalidOperationException("上下文变量 'transform_matrix' 为空，无法应用变换。");
        if (transform.Rows != 4 || transform.Cols != 4)
            throw new InvalidOperationException(
                $"变换矩阵必须为 4×4，实际为 {transform.Rows}×{transform.Cols}。"
            );

        Mat pointCloud = input.PointCloud!;
        if (pointCloud is null || pointCloud.Empty())
            throw new InvalidOperationException("输入点云为空，无法应用变换。");

        // 读取（可选求逆）4×4 矩阵
        using Mat mat64 = new Mat();
        transform.ConvertTo(mat64, MatType.CV_64FC1);

        Mat effective = mat64;
        Mat? inverted = null;
        if (_invert)
        {
            inverted = new Mat();
            if (Cv2.Invert(mat64, inverted, DecompTypes.LU) == 0)
            {
                inverted.Dispose();
                throw new InvalidOperationException("变换矩阵不可逆，无法求逆变换。");
            }
            effective = inverted;
        }

        try
        {
            double[] R = new double[9];
            double[] t = new double[3];
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                    R[r * 3 + c] = effective.Get<double>(r, c);
                t[r] = effective.Get<double>(r, 3);
            }

            int pointCount = pointCloud.Rows;
            int colCount = pointCloud.Cols;
            bool hasNormals = colCount >= 6;

            Mat output = new Mat(pointCount, colCount, MatType.CV_32FC1);
            for (int i = 0; i < pointCount; i++)
            {
                double x = pointCloud.Get<float>(i, 0);
                double y = pointCloud.Get<float>(i, 1);
                double z = pointCloud.Get<float>(i, 2);

                output.Set(i, 0, (float)(R[0] * x + R[1] * y + R[2] * z + t[0]));
                output.Set(i, 1, (float)(R[3] * x + R[4] * y + R[5] * z + t[1]));
                output.Set(i, 2, (float)(R[6] * x + R[7] * y + R[8] * z + t[2]));

                if (hasNormals)
                {
                    double nx = pointCloud.Get<float>(i, 3);
                    double ny = pointCloud.Get<float>(i, 4);
                    double nz = pointCloud.Get<float>(i, 5);
                    output.Set(i, 3, (float)(R[0] * nx + R[1] * ny + R[2] * nz));
                    output.Set(i, 4, (float)(R[3] * nx + R[4] * ny + R[5] * nz));
                    output.Set(i, 5, (float)(R[6] * nx + R[7] * ny + R[8] * nz));
                }

                // 复制其余列（法向量之后的附加通道，或非法向布局的 3-5 列）
                for (int c = hasNormals ? 6 : 3; c < colCount; c++)
                    output.Set(i, c, pointCloud.Get<float>(i, c));
            }

            var outputCloud = PointCloudUtils.BuildCloud(
                output,
                input.HasColors && input.Colors != null ? input.Colors.Clone() : null
            );
            context.Set("output_point_cloud", outputCloud);
        }
        finally
        {
            inverted?.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
