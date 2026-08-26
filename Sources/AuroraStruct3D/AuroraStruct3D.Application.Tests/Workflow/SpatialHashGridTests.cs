using AuroraStruct3D.OpenCV.Common;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public sealed class SpatialHashGridTests
{
    [Fact]
    public void FindNearestNeighbor_Should_Not_Stop_At_First_Occupied_Cell()
    {
        float[] x = [0, 1.01f];
        float[] y = [0, 0];
        float[] z = [0, 0];
        var grid = new SpatialHashGrid(x, y, z, cellSize: 1);

        int index = grid.FindNearestNeighbor(0.99f, 0, 0, out double distanceSquared);

        Assert.Equal(1, index);
        Assert.InRange(distanceSquared, 0.00039, 0.00041);
    }

    [Fact]
    public void FindNearestNeighborWithin_Should_Respect_Maximum_Distance()
    {
        float[] x = [0, 2];
        float[] y = [0, 0];
        float[] z = [0, 0];
        var grid = new SpatialHashGrid(x, y, z, cellSize: 1);

        int index = grid.FindNearestNeighborWithin(1, 0, 0, 0.4, out double distanceSquared);

        Assert.Equal(-1, index);
        Assert.Equal(double.MaxValue, distanceSquared);
    }

    [Fact]
    public void FindNearestNeighborWithin_Should_Search_Just_Outside_Indexed_Bounds()
    {
        float[] x = [0, 1];
        float[] y = [0, 0];
        float[] z = [0, 0];
        var grid = new SpatialHashGrid(x, y, z, cellSize: 1);

        int index = grid.FindNearestNeighborWithin(-0.05f, 0, 0, 0.1, out double distanceSquared);

        Assert.Equal(0, index);
        Assert.InRange(distanceSquared, 0.00249, 0.00251);
    }
}
