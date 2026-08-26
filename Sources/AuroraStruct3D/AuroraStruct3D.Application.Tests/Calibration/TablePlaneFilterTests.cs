using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Calibration;

public class TablePlaneFilterTests
{
    [Fact]
    public void Should_remove_tilted_table_and_keep_object_above_clearance()
    {
        using Mat projection = BuildProjection();
        using Mat depth = new(160, 200, MatType.CV_64FC1);
        int rows = depth.Rows, cols = depth.Cols;
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < cols; x++)
            depth.Set(y, x, 1000d + x * 0.03d + y * 0.02d);

        for (int y = 55; y < 105; y++)
        for (int x = 70; x < 130; x++)
            depth.Set(y, x, 985d + x * 0.03d + y * 0.02d);

        TablePlaneFilterResult result = TablePlaneFilter.Apply(depth, projection, 3d);

        Assert.True(result.Applied, result.FailureReason);
        Assert.NotNull(result.Plane);
        Assert.True(result.RemovedPointCount > 20_000);
        Assert.True(double.IsNaN(depth.At<double>(10, 10)));
        Assert.True(double.IsFinite(depth.At<double>(80, 100)));
    }

    [Fact]
    public void Should_leave_depth_unchanged_when_plane_cannot_be_fitted()
    {
        using Mat projection = BuildProjection();
        using Mat depth = new(80, 100, MatType.CV_64FC1, Scalar.All(double.NaN));
        depth.Set(40, 50, 1000d);

        TablePlaneFilterResult result = TablePlaneFilter.Apply(depth, projection, 3d);

        Assert.False(result.Applied);
        Assert.Equal(1000d, depth.At<double>(40, 50));
    }

    [Fact]
    public void Cached_plane_should_filter_without_border_candidates()
    {
        using Mat projection = BuildProjection();
        using Mat depth = new(20, 20, MatType.CV_64FC1, Scalar.All(1000d));
        TablePlaneModel plane = new(0, 0, -1, 1000);

        TablePlaneFilterResult result = TablePlaneFilter.Apply(depth, projection, 3d, plane);

        Assert.True(result.Applied);
        Assert.Equal(400, result.RemovedPointCount);
    }

    [Fact]
    public void Should_fallback_to_full_frame_when_table_does_not_reach_image_border()
    {
        using Mat projection = BuildProjection();
        using Mat depth = new(200, 240, MatType.CV_64FC1, Scalar.All(double.NaN));
        for (int y = 130; y < 165; y++)
        for (int x = 45; x < 195; x++)
            depth.Set(y, x, 240d);
        for (int y = 70; y < 110; y++)
        for (int x = 95; x < 145; x++)
            depth.Set(y, x, 220d);

        TablePlaneFilterResult result = TablePlaneFilter.Apply(depth, projection, 3d);

        Assert.True(result.Applied, result.FailureReason);
        Assert.True(double.IsNaN(depth.At<double>(140, 60)));
        Assert.Equal(220d, depth.At<double>(90, 120));
    }

    private static Mat BuildProjection()
    {
        Mat projection = Mat.Zeros(3, 4, MatType.CV_64FC1);
        projection.Set(0, 0, 800d);
        projection.Set(1, 1, 800d);
        projection.Set(0, 2, 100d);
        projection.Set(1, 2, 80d);
        projection.Set(2, 2, 1d);
        return projection;
    }
}
