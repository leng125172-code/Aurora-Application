using System.Text.Json;
using AuroraStruct3D.OpenCV.RoiOps;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.Workflow;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class Annotate3dLineAngleResultOperatorTests
{
    [Fact]
    public void Contract_Should_Expose_Two_Independent_Lines_And_One_Annotated_Image()
    {
        Assert.Collection(
            annotate_3d_line_angle_result.InputVisionParameters!,
            input => Assert.Equal("input_mat", input.ParameterName),
            input => Assert.Equal("projection_mapping", input.ParameterName),
            input => Assert.Equal("line1_params", input.ParameterName),
            input => Assert.Equal("line1_points", input.ParameterName),
            input => Assert.Equal("line2_params", input.ParameterName),
            input => Assert.Equal("line2_points", input.ParameterName),
            input => Assert.Equal("inspection_result", input.ParameterName)
        );
        Assert.Collection(
            annotate_3d_line_angle_result.OutputVisionParameters!,
            output => Assert.Equal("output_mat", output.ParameterName)
        );
    }

    [Fact]
    public void Execute_Should_Draw_Segments_Extensions_Intersection_And_Angle()
    {
        using Mat input = new(240, 320, MatType.CV_8UC4, new Scalar(0, 0, 0, 255));
        using Mat line1 = CreateLineParameters([0, 0, 0], [1, 0, 0]);
        using Mat line2 = CreateLineParameters([2, 0, 0], [0, 1, 0]);
        PointCloudData line1Points = CreateLinePoints([-8, 0, 0], [-2, 0, 0], 40);
        PointCloudData line2Points = CreateLinePoints([2, 3, 0], [2, 8, 0], 40);
        var inspection = InspectionResults.CreateTyped(
            true,
            true,
            new InstallationAxisInspectionDetails { axisAngle = 90, angleDeviation = 0 },
            "OK"
        );
        string mapping = JsonSerializer.Serialize(new RoiProjectionMapping
        {
            ViewLabel = "XY",
            WorldMinX = -10,
            WorldMaxX = 10,
            WorldMinY = -10,
            WorldMaxY = 10,
            ImageWidth = 320,
            ImageHeight = 240,
        });

        using WorkflowContext context = new();
        context.Set("input_mat", input);
        context.Set("projection_mapping", mapping);
        context.Set("line1_params", line1);
        context.Set("line1_points", line1Points);
        context.Set("line2_params", line2);
        context.Set("line2_points", line2Points);
        context.Set("inspection_result", inspection);

        using var op = new annotate_3d_line_angle_result();
        op.Execute(context);

        Mat output = Assert.IsType<Mat>(context.Get("output_mat"));
        Assert.Equal(4, output.Channels());
        Assert.True(CountPixels(output, pixel => pixel.Item1 > 180 && pixel.Item2 > 50) > 20);
        Assert.True(CountPixels(output, pixel => pixel.Item1 > 100 && pixel.Item2 > 180) > 20);
        Assert.True(CountPixels(output, pixel => pixel.Item2 < 80 && pixel.Item3 > 180) > 10);
        Assert.Equal(new Vec4b(0, 0, 0, 255), input.Get<Vec4b>(120, 160));
        output.Dispose();
    }

    private static Mat CreateLineParameters(double[] point, double[] direction)
    {
        Mat parameters = new(6, 1, MatType.CV_64FC1);
        for (int i = 0; i < 3; i++)
        {
            parameters.Set(i, 0, point[i]);
            parameters.Set(i + 3, 0, direction[i]);
        }
        return parameters;
    }

    private static PointCloudData CreateLinePoints(double[] start, double[] end, int count)
    {
        Mat points = new(count, 3, MatType.CV_32FC1);
        for (int row = 0; row < count; row++)
        {
            double t = row / (double)(count - 1);
            for (int col = 0; col < 3; col++)
                points.Set(row, col, (float)(start[col] + (end[col] - start[col]) * t));
        }
        return new PointCloudData { Value = points };
    }

    private static int CountPixels(Mat image, Func<Vec4b, bool> predicate)
    {
        int count = 0;
        int rows = image.Rows;
        int cols = image.Cols;
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
                if (predicate(image.Get<Vec4b>(y, x)))
                    count++;
        return count;
    }
}
