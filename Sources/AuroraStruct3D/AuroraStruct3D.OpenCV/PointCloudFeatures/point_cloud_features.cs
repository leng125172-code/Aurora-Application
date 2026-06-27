using System.Text.Json;

namespace AuroraStruct3D.OpenCV.PointCloudFeatures;

/// <summary>
/// 工作流算子：点云几何特征提取。
/// <para>
/// 计算点云的整体几何特征，包括包围盒、体积、质心、点密度、主轴方向等。
/// 这些特征可用于缺陷检测中的尺寸测量、形状分析和异常识别，
/// 也可以作为机器学习模型的输入特征向量。
/// </para>
/// <para>
/// 计算原理：
/// <list type="number">
///   <item>从坐标直接计算包围盒（AABB）和质心</item>
///   <item>对点云进行 PCA 分析，提取三个主轴方向（特征向量）</item>
///   <item>基于特征值估算点云在主轴上的扩展范围（标准差）</item>
///   <item>包围盒体积 = dx × dy × dz，密度 = 点数 / 体积</item>
/// </list>
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 输入点云</item>
///   <item>输出 <c>stats_json</c>（string）— 几何特征 JSON</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-000e-4000-8000-000000000021")]
[Category("3D拟合测量")]
[DisplayName("点云特征")]
[Description("算点云整体特征（数量、范围、密度等），先摸个底做概览。")]
public class point_cloud_features : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new VisionParameter<string>
            {
                ParameterName = "stats_json",
                DisplayName = "特征信息",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters => null;

    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <inheritdoc/>
    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PointCloudData input =
            context.Get<PointCloudData>("input_point_cloud")
            ?? throw new InvalidOperationException(
                "上下文变量 'input_point_cloud' 为空，请确认输入绑定已正确设置。"
            );

        Mat pointCloud = input.PointCloud!;
        if (pointCloud is null || pointCloud.Empty())
            throw new InvalidOperationException("输入点云为空，无法计算几何特征。");

        int pointCount = pointCloud.Rows;

        // ① 包围盒和质心
        float minX = float.MaxValue,
            maxX = float.MinValue;
        float minY = float.MaxValue,
            maxY = float.MinValue;
        float minZ = float.MaxValue,
            maxZ = float.MinValue;
        double cx = 0,
            cy = 0,
            cz = 0;

        for (int i = 0; i < pointCount; i++)
        {
            float x = pointCloud.Get<float>(i, 0);
            float y = pointCloud.Get<float>(i, 1);
            float z = pointCloud.Get<float>(i, 2);

            if (x < minX)
                minX = x;
            if (x > maxX)
                maxX = x;
            if (y < minY)
                minY = y;
            if (y > maxY)
                maxY = y;
            if (z < minZ)
                minZ = z;
            if (z > maxZ)
                maxZ = z;

            cx += x;
            cy += y;
            cz += z;
        }
        cx /= pointCount;
        cy /= pointCount;
        cz /= pointCount;

        double dx = maxX - minX;
        double dy = maxY - minY;
        double dz = maxZ - minZ;
        double volume = dx * dy * dz;
        double density = volume > 1e-15 ? pointCount / volume : 0;

        // ② PCA 主轴分析
        double c00 = 0,
            c01 = 0,
            c02 = 0;
        double c11 = 0,
            c12 = 0;
        double c22 = 0;

        for (int i = 0; i < pointCount; i++)
        {
            double dxc = pointCloud.Get<float>(i, 0) - cx;
            double dyc = pointCloud.Get<float>(i, 1) - cy;
            double dzc = pointCloud.Get<float>(i, 2) - cz;

            c00 += dxc * dxc;
            c01 += dxc * dyc;
            c02 += dxc * dzc;
            c11 += dyc * dyc;
            c12 += dyc * dzc;
            c22 += dzc * dzc;
        }

        (double[] eigenValues, double[,] eigenVectors) = JacobiEigenDecomposition(
            c00 / pointCount,
            c01 / pointCount,
            c02 / pointCount,
            c11 / pointCount,
            c12 / pointCount,
            c22 / pointCount
        );

        // 按特征值降序排列（主轴1 = 最大扩展方向）
        int[] order = new[] { 0, 1, 2 };
        Array.Sort(order, (a, b) => eigenValues[b].CompareTo(eigenValues[a]));

        var axes = new List<PrincipalAxis>();
        for (int k = 0; k < 3; k++)
        {
            int idx = order[k];
            axes.Add(
                new PrincipalAxis
                {
                    Index = k + 1,
                    Eigenvalue = eigenValues[idx],
                    // 标准差 = sqrt(特征值)
                    StdDev = Math.Sqrt(Math.Max(0, eigenValues[idx])),
                    VectorX = eigenVectors[0, idx],
                    VectorY = eigenVectors[1, idx],
                    VectorZ = eigenVectors[2, idx],
                }
            );
        }

        var stats = new CloudFeatures
        {
            PointCount = pointCount,
            Centroid = new Point3D
            {
                X = cx,
                Y = cy,
                Z = cz,
            },
            BoundingBox = new BoundingBox3D
            {
                MinX = minX,
                MaxX = maxX,
                MinY = minY,
                MaxY = maxY,
                MinZ = minZ,
                MaxZ = maxZ,
                SizeX = dx,
                SizeY = dy,
                SizeZ = dz,
            },
            Volume = volume,
            Density = density,
            PrincipalAxes = axes,
        };

        string statsJson = JsonSerializer.Serialize(stats, JsonOptions);
        context.Set("stats_json", statsJson);
    }

    // ===== Jacobi 特征值分解 =====

    private static (double[] eigenvalues, double[,] eigenvectors) JacobiEigenDecomposition(
        double s00,
        double s01,
        double s02,
        double s11,
        double s12,
        double s22
    )
    {
        double[,] M = new double[3, 3]
        {
            { s00, s01, s02 },
            { s01, s11, s12 },
            { s02, s12, s22 },
        };

        double[,] V = new double[3, 3];
        for (int i = 0; i < 3; i++)
            V[i, i] = 1.0;

        for (int iter = 0; iter < 100; iter++)
        {
            int p = 0,
                q = 1;
            double maxOff = Math.Abs(M[0, 1]);
            if (Math.Abs(M[0, 2]) > maxOff)
            {
                maxOff = Math.Abs(M[0, 2]);
                p = 0;
                q = 2;
            }
            if (Math.Abs(M[1, 2]) > maxOff)
            {
                maxOff = Math.Abs(M[1, 2]);
                p = 1;
                q = 2;
            }

            if (maxOff < 1e-15)
                break;

            double theta = (M[q, q] - M[p, p]) / (2 * M[p, q]);
            double t =
                theta >= 0
                    ? 1.0 / (theta + Math.Sqrt(theta * theta + 1))
                    : 1.0 / (theta - Math.Sqrt(theta * theta + 1));
            double cosA = 1.0 / Math.Sqrt(t * t + 1);
            double sinA = t * cosA;

            double mpp = M[p, p];
            double mqq = M[q, q];
            double mpq = M[p, q];

            M[p, p] = cosA * cosA * mpp + sinA * sinA * mqq - 2 * sinA * cosA * mpq;
            M[q, q] = sinA * sinA * mpp + cosA * cosA * mqq + 2 * sinA * cosA * mpq;
            M[p, q] = M[q, p] = (cosA * cosA - sinA * sinA) * mpq + sinA * cosA * (mpp - mqq);

            for (int r = 0; r < 3; r++)
            {
                if (r == p || r == q)
                    continue;
                double mrp = M[r, p];
                double mrq = M[r, q];
                M[r, p] = M[p, r] = cosA * mrp - sinA * mrq;
                M[r, q] = M[q, r] = sinA * mrp + cosA * mrq;
            }

            for (int r = 0; r < 3; r++)
            {
                double vrp = V[r, p];
                double vrq = V[r, q];
                V[r, p] = cosA * vrp - sinA * vrq;
                V[r, q] = sinA * vrp + cosA * vrq;
            }
        }

        double[] eigenvalues = new double[] { M[0, 0], M[1, 1], M[2, 2] };
        return (eigenvalues, V);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    // ── 输出 DTO ────────────────────────────────────────────────────────────

    /// <summary>点云几何特征。</summary>
    public class CloudFeatures
    {
        public int PointCount { get; set; }
        public Point3D Centroid { get; set; } = new();
        public BoundingBox3D BoundingBox { get; set; } = new();
        public double Volume { get; set; }
        public double Density { get; set; }
        public List<PrincipalAxis> PrincipalAxes { get; set; } = new();
    }

    /// <summary>3D 点坐标。</summary>
    public class Point3D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    /// <summary>3D 包围盒。</summary>
    public class BoundingBox3D
    {
        public double MinX { get; set; }
        public double MaxX { get; set; }
        public double MinY { get; set; }
        public double MaxY { get; set; }
        public double MinZ { get; set; }
        public double MaxZ { get; set; }
        public double SizeX { get; set; }
        public double SizeY { get; set; }
        public double SizeZ { get; set; }
    }

    /// <summary>PCA 主轴信息。</summary>
    public class PrincipalAxis
    {
        public int Index { get; set; }
        public double Eigenvalue { get; set; }
        public double StdDev { get; set; }
        public double VectorX { get; set; }
        public double VectorY { get; set; }
        public double VectorZ { get; set; }
    }
}
