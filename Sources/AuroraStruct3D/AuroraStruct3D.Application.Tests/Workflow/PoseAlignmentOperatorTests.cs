using System.Text.Json;
using AuroraStruct3D.OpenCV.File.PointCloud;
using AuroraStruct3D.OpenCV.PointCloudRegistration;
using AuroraStruct3D.OpenCV.PointCloudOps;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.OpenCV.Workflow;
using OpenCvSharp;
using Xunit;
using Xunit.Abstractions;

namespace AuroraStruct3D.Application.Tests.Workflow;

public sealed class PoseAlignmentOperatorTests(ITestOutputHelper output)
{
    [Fact]
    public void FixedPoseTransform_Should_Apply_ZyxEuler_And_Translation()
    {
        using Mat points = new(1, 3, MatType.CV_32FC1);
        points.Set(0, 0, 1f);
        points.Set(0, 1, 0f);
        points.Set(0, 2, 0f);
        var input = new PointCloudData { Value = points.Clone() };
        using var context = new WorkflowContext();
        context.Set("input_point_cloud", input);

        using var op = new fixed_pose_transform(
            rotationZDeg: 90,
            translationX: 10,
            translationY: 20,
            translationZ: 30
        );
        op.Execute(context);

        PointCloudData result = Assert.IsType<PointCloudData>(context.Get("output_point_cloud"));
        Assert.InRange(Math.Abs(result.PointCloud!.Get<float>(0, 0) - 10), 0, 1e-5);
        Assert.InRange(Math.Abs(result.PointCloud.Get<float>(0, 1) - 21), 0, 1e-5);
        Assert.InRange(Math.Abs(result.PointCloud.Get<float>(0, 2) - 30), 0, 1e-5);
    }

    [Fact]
    public void ReferenceFootprintCrop_Should_Remove_Fixture_Points_Seen_Through_Holes()
    {
        var referencePoints = new List<(float x, float y, float z)>();
        for (int y = 0; y <= 10; y++)
        for (int x = 0; x <= 10; x++)
        {
            bool insideHole = x is >= 4 and <= 6 && y is >= 4 and <= 6;
            if (!insideHole)
                referencePoints.Add((x, y, 5));
        }
        var sourcePoints = new List<(float x, float y, float z)>(referencePoints)
        {
            (5, 5, 0),
            (-2, -2, 0),
        };
        PointCloudData reference = CreateCloud(referencePoints);
        PointCloudData source = CreateCloud(sourcePoints);
        using var context = new WorkflowContext();
        context.Set("source_cloud", source);
        context.Set("reference_cloud", reference);

        using var op = new reference_footprint_crop(
            lateralToleranceMm: 0.2,
            minimumKeptRatio: 0.5
        );
        op.Execute(context);

        PointCloudData filtered = Assert.IsType<PointCloudData>(context.Get("filtered_cloud"));
        Assert.Equal(referencePoints.Count, filtered.PointCloud!.Rows);
        string json = Assert.IsType<string>(context.Get("filter_result"));
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(2, document.RootElement.GetProperty("ignoredPointCount").GetInt32());
    }

    [Fact]
    public void PartialSurfacePoseAlignment_Should_Recover_Transformed_Subset()
    {
        PointCloudData target = CreateAsymmetricSurface();
        const double rz = 31;
        const double tx = 18;
        const double ty = -7;
        const double tz = 12;
        PointCloudData source = CreateInverseTransformedPartialSurface(target, rz, tx, ty, tz);
        using var context = new WorkflowContext();
        context.Set("source_cloud", source);
        context.Set("target_cloud", target);

        using var op = new partial_surface_pose_alignment(
            sourceSampleCount: 800,
            targetSampleCount: 3000,
            maxIterations: 35,
            trimFraction: 0.85,
            inlierDistance: 0.6,
            minimumInlierRatio: 0.6,
            yawSearchStepDeg: 2,
            yawCenterDeg: 31,
            yawSearchRangeDeg: 8
        );
        op.Execute(context);

        string json = Assert.IsType<string>(context.Get("pose_json"));
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("inlierRatio").GetDouble() > 0.7);
        Assert.True(document.RootElement.GetProperty("trimmedRmse").GetDouble() < 0.5);
        Assert.InRange(
            Math.Abs(document.RootElement.GetProperty("yawOffsetFromCenterDeg").GetDouble()),
            0,
            8
        );
        Assert.InRange(document.RootElement.GetProperty("hypothesisCount").GetInt32(), 1, 11);
    }

    [Fact]
    public void RealQualifiedScan_PoseDiagnostic_When_Paths_Are_Configured()
    {
        string? scanPath = Environment.GetEnvironmentVariable("AURORA_REAL_POSE_SCAN");
        string? modelPath = Environment.GetEnvironmentVariable("AURORA_REAL_POSE_MODEL");
        if (string.IsNullOrWhiteSpace(scanPath) || string.IsNullOrWhiteSpace(modelPath))
            return;

        PointCloudData scan = ReadCloud(scanPath);
        PointCloudData model = ReadCloud(modelPath);
        using var context = new WorkflowContext();
        context.Set("source_cloud", scan);
        context.Set("target_cloud", model);
        using var op = new partial_surface_pose_alignment(
            sourceSampleCount: 5000,
            targetSampleCount: 50_000,
            maxIterations: 50,
            trimFraction: 0.65,
            inlierDistance: 1.5,
            minimumInlierRatio: 0.05
        );
        op.Execute(context);
        output.WriteLine(Assert.IsType<string>(context.Get("pose_json")));
        string? overlayPath = Environment.GetEnvironmentVariable("AURORA_REAL_POSE_OVERLAY");
        if (!string.IsNullOrWhiteSpace(overlayPath))
        {
            PointCloudData aligned = Assert.IsType<PointCloudData>(context.Get("aligned_cloud"));
            WritePoseOverlay(model.PointCloud!, aligned.PointCloud!, overlayPath);
        }
    }

    private static PointCloudData ReadCloud(string path)
    {
        using var context = new WorkflowContext();
        context.Set("point_cloud_path", path);
        using var reader = new read_point_cloud();
        reader.Execute(context);
        PointCloudData cloud = Assert.IsType<PointCloudData>(context.Get("output_point_cloud"));
        return new PointCloudData { Value = cloud.PointCloud!.Clone() };
    }

    private static PointCloudData CreateAsymmetricSurface()
    {
        var points = new List<(float x, float y, float z)>();
        for (int yi = 0; yi < 31; yi++)
        for (int xi = 0; xi < 41; xi++)
        {
            float x = xi * 0.5f;
            float y = yi * 0.5f;
            float z = (float)(0.03 * x * x + 0.15 * y + 0.8 * Math.Sin(x * 0.6));
            points.Add((x, y, z));
        }
        // 给完整数模增加一个不对称侧面，使主轴与局部顶面不完全相同。
        for (int zi = 0; zi < 16; zi++)
        for (int yi = 0; yi < 31; yi++)
            points.Add((0, yi * 0.5f, zi * 0.35f));
        return CreateCloud(points);
    }

    private static PointCloudData CreateInverseTransformedPartialSurface(
        PointCloudData target,
        double rotationZDeg,
        double tx,
        double ty,
        double tz
    )
    {
        double angle = rotationZDeg * Math.PI / 180;
        double c = Math.Cos(angle);
        double s = Math.Sin(angle);
        var points = new List<(float x, float y, float z)>();
        Mat cloud = target.PointCloud!;
        for (int index = 0; index < 31 * 41; index++)
        {
            float modelX = cloud.Get<float>(index, 0);
            float modelY = cloud.Get<float>(index, 1);
            float modelZ = cloud.Get<float>(index, 2);
            if (modelX < 3 || modelY < 2)
                continue;
            double dx = modelX - tx;
            double dy = modelY - ty;
            points.Add(((float)(c * dx + s * dy), (float)(-s * dx + c * dy), modelZ - (float)tz));
        }
        return CreateCloud(points);
    }

    private static PointCloudData CreateCloud(List<(float x, float y, float z)> points)
    {
        Mat mat = new(points.Count, 3, MatType.CV_32FC1);
        for (int index = 0; index < points.Count; index++)
        {
            mat.Set(index, 0, points[index].x);
            mat.Set(index, 1, points[index].y);
            mat.Set(index, 2, points[index].z);
        }
        return new PointCloudData { Value = mat };
    }

    private static void WritePoseOverlay(Mat model, Mat scan, string path)
    {
        const int size = 800;
        using Mat xy = new(size, size, MatType.CV_8UC3, new Scalar(255, 255, 255));
        using Mat xz = new(size, size, MatType.CV_8UC3, new Scalar(255, 255, 255));
        (float minX, float maxX) = Range(model, 0);
        (float minY, float maxY) = Range(model, 1);
        (float minZ, float maxZ) = Range(model, 2);
        DrawProjection(xy, model, 0, 1, minX, maxX, minY, maxY, new Scalar(170, 170, 170));
        DrawProjection(xy, scan, 0, 1, minX, maxX, minY, maxY, new Scalar(0, 0, 255));
        DrawProjection(xz, model, 0, 2, minX, maxX, minZ, maxZ, new Scalar(170, 170, 170));
        DrawProjection(xz, scan, 0, 2, minX, maxX, minZ, maxZ, new Scalar(0, 0, 255));
        using Mat combined = new(size, size * 2, MatType.CV_8UC3, new Scalar(255, 255, 255));
        using (Mat left = combined.ColRange(0, size)) xy.CopyTo(left);
        using (Mat right = combined.ColRange(size, size * 2)) xz.CopyTo(right);
        Cv2.PutText(combined, "XY TOP", new Point(20, 35), HersheyFonts.HersheySimplex, 0.8, Scalar.Black, 2);
        Cv2.PutText(combined, "XZ SIDE", new Point(size + 20, 35), HersheyFonts.HersheySimplex, 0.8, Scalar.Black, 2);
        Cv2.ImWrite(path, combined);
    }

    private static (float min, float max) Range(Mat cloud, int column)
    {
        float min = float.MaxValue, max = float.MinValue;
        for (int index = 0; index < cloud.Rows; index++)
        {
            float value = cloud.Get<float>(index, column);
            min = Math.Min(min, value); max = Math.Max(max, value);
        }
        float margin = (max - min) * 0.05f;
        return (min - margin, max + margin);
    }

    private static void DrawProjection(
        Mat image,
        Mat cloud,
        int horizontalColumn,
        int verticalColumn,
        float minHorizontal,
        float maxHorizontal,
        float minVertical,
        float maxVertical,
        Scalar color
    )
    {
        int stride = Math.Max(1, cloud.Rows / 50_000);
        for (int index = 0; index < cloud.Rows; index += stride)
        {
            float horizontal = cloud.Get<float>(index, horizontalColumn);
            float vertical = cloud.Get<float>(index, verticalColumn);
            int px = (int)((horizontal - minHorizontal) / (maxHorizontal - minHorizontal) * (image.Cols - 1));
            int py = image.Rows - 1 - (int)((vertical - minVertical) / (maxVertical - minVertical) * (image.Rows - 1));
            if (px >= 0 && px < image.Cols && py >= 0 && py < image.Rows)
                image.Set(py, px, new Vec3b((byte)color.Val0, (byte)color.Val1, (byte)color.Val2));
        }
    }
}
