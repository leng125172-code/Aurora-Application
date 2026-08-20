using AuroraStruct3D.Calibration;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Calibration;

public class CalibImageRotationTests
{
    [Theory]
    [InlineData(0, new byte[] { 1, 2, 3, 4, 5, 6 }, 2, 3)]
    [InlineData(90, new byte[] { 4, 1, 5, 2, 6, 3 }, 3, 2)]
    [InlineData(180, new byte[] { 6, 5, 4, 3, 2, 1 }, 2, 3)]
    [InlineData(270, new byte[] { 3, 6, 2, 5, 1, 4 }, 3, 2)]
    public void ApplyRotation_UsesClockwiseAngles(
        int angle,
        byte[] expected,
        int expectedRows,
        int expectedColumns
    )
    {
        using Mat source = new(2, 3, MatType.CV_8UC1);
        source.Set(0, 0, (byte)1);
        source.Set(0, 1, (byte)2);
        source.Set(0, 2, (byte)3);
        source.Set(1, 0, (byte)4);
        source.Set(1, 1, (byte)5);
        source.Set(1, 2, (byte)6);

        using Mat actual = CalibImageUtils.ApplyRotation(source, angle);

        Assert.Equal(expectedRows, actual.Rows);
        Assert.Equal(expectedColumns, actual.Cols);
        Assert.Equal(
            expected,
            Enumerable.Range(0, actual.Rows)
                .SelectMany(row => Enumerable.Range(0, actual.Cols).Select(column => actual.At<byte>(row, column)))
                .ToArray()
        );
    }
}
