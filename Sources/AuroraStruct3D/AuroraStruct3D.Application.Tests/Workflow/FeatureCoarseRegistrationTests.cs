using System.Diagnostics;
using System.Text.Json;
using AuroraStruct3D.OpenCV.PointCloudRegistration;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.OpenCV.Workflow;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class FeatureCoarseRegistrationTests
{
    [Fact]
    public void LargeCloud_Should_BoundNormalEstimationAndPreserveFullOutput()
    {
        const int pointCount = 5_000;
        using Mat source = CreateCloud(pointCount, 0, 0, 0);
        using Mat target = CreateCloud(pointCount, 0.02f, -0.01f, 0.005f);
        using WorkflowContext context = new();
        context.Set("source_cloud", new PointCloudData { Value = source });
        context.Set("target_cloud", new PointCloudData { Value = target });

        using var op = new feature_coarse_registration(
            normalK: 10,
            featureRadius: 0.08,
            maxIterations: 50,
            sampleCount: 100,
            distanceThreshold: 0.1
        );
        Stopwatch timer = Stopwatch.StartNew();

        op.Execute(context);

        timer.Stop();
        PointCloudData aligned = context.Get<PointCloudData>("aligned_cloud")!;
        Assert.NotNull(aligned.PointCloud);
        Assert.Equal(pointCount, aligned.PointCloud!.Rows);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), $"执行耗时 {timer.Elapsed}");

        using JsonDocument stats = JsonDocument.Parse(context.Get<string>("stats_json")!);
        Assert.Equal(pointCount, stats.RootElement.GetProperty("sourcePointCount").GetInt32());
        Assert.Equal(500, stats.RootElement.GetProperty("sourceWorkingPointCount").GetInt32());
        Assert.Equal(500, stats.RootElement.GetProperty("targetWorkingPointCount").GetInt32());
    }

    [Fact]
    public void Constructor_Should_RejectUnboundedSamplingParameters()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new feature_coarse_registration(sampleCount: 2_001)
        );
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new feature_coarse_registration(maxIterations: 100_001)
        );
    }

    private static Mat CreateCloud(int count, float dx, float dy, float dz)
    {
        Mat cloud = new(count, 3, MatType.CV_32FC1);
        for (int i = 0; i < count; i++)
        {
            int xIndex = i % 50;
            int yIndex = (i / 50) % 50;
            int zIndex = i / 2_500;
            float x = xIndex * 0.01f;
            float y = yIndex * 0.01f;
            float z = (float)(
                zIndex * 0.03
                + 0.01 * Math.Sin(xIndex * 0.17)
                + 0.008 * Math.Cos(yIndex * 0.13)
            );
            cloud.Set(i, 0, x + dx);
            cloud.Set(i, 1, y + dy);
            cloud.Set(i, 2, z + dz);
        }
        return cloud;
    }
}
