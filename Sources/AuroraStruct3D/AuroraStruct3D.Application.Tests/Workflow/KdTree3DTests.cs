using AuroraStruct3D.OpenCV.Common;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class KdTree3DTests
{
    [Fact]
    public void FindNearestNeighborWithin_Should_Match_BruteForce()
    {
        const int pointCount = 500;
        const double maxDistance = 2.5;
        Random random = new(20260826);
        float[] x = new float[pointCount];
        float[] y = new float[pointCount];
        float[] z = new float[pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            x[i] = (float)(random.NextDouble() * 20 - 10);
            y[i] = (float)(random.NextDouble() * 20 - 10);
            z[i] = (float)(random.NextDouble() * 20 - 10);
        }

        var tree = new KdTree3D(x, y, z);
        for (int query = 0; query < 200; query++)
        {
            float qx = (float)(random.NextDouble() * 24 - 12);
            float qy = (float)(random.NextDouble() * 24 - 12);
            float qz = (float)(random.NextDouble() * 24 - 12);
            int actual = tree.FindNearestNeighborWithin(
                qx,
                qy,
                qz,
                maxDistance,
                out double actualDistanceSquared
            );

            int expected = -1;
            double expectedDistanceSquared = maxDistance * maxDistance;
            for (int i = 0; i < pointCount; i++)
            {
                double dx = x[i] - qx;
                double dy = y[i] - qy;
                double dz = z[i] - qz;
                double distanceSquared = dx * dx + dy * dy + dz * dz;
                if (distanceSquared <= expectedDistanceSquared)
                {
                    expected = i;
                    expectedDistanceSquared = distanceSquared;
                }
            }

            Assert.Equal(expected >= 0, actual >= 0);
            if (expected >= 0)
                Assert.Equal(expectedDistanceSquared, actualDistanceSquared, 10);
            else
                Assert.Equal(double.MaxValue, actualDistanceSquared);
        }
    }

    [Fact]
    public void FindNearestNeighborWithin_Should_Handle_Exact_Duplicate_Coordinates()
    {
        float[] x = [1, 1, 4];
        float[] y = [2, 2, 5];
        float[] z = [3, 3, 6];
        var tree = new KdTree3D(x, y, z);

        int index = tree.FindNearestNeighborWithin(1, 2, 3, 0.1, out double distanceSquared);

        Assert.InRange(index, 0, 1);
        Assert.Equal(0, distanceSquared);
    }
}
