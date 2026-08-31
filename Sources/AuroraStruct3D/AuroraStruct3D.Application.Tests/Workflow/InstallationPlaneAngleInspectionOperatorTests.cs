using AuroraStruct3D.OpenCV.PointCloudPlaneOps;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.Workflow;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class InstallationPlaneAngleInspectionOperatorTests
{
    [Fact]
    public void Contract_Should_Expose_Installation_Angles_Quality_And_Three_State_Result()
    {
        Assert.Collection(
            installation_plane_angle_inspection.InputVisionParameters!,
            input => Assert.Equal("reference_plane_params", input.ParameterName),
            input => Assert.Equal("reference_points", input.ParameterName),
            input => Assert.Equal("measured_plane_params", input.ParameterName),
            input => Assert.Equal("measured_points", input.ParameterName)
        );
        Assert.Collection(
            installation_plane_angle_inspection.OutputVisionParameters!,
            output => Assert.Equal("result", output.ParameterName)
        );
    }

    [Fact]
    public void Execute_Should_Return_Ng_With_Signed_X_And_Y_Installation_Tilt()
    {
        double expectedX = 2;
        double expectedY = -1;
        double rx = expectedX * Math.PI / 180;
        double ry = expectedY * Math.PI / 180;
        double[] measuredNormal =
        [
            Math.Sin(ry) * Math.Cos(rx),
            -Math.Sin(rx),
            Math.Cos(ry) * Math.Cos(rx),
        ];

        using Mat referencePlane = CreatePlane([0, 0, 1]);
        using Mat measuredPlane = CreatePlane(measuredNormal);
        PointCloudData referencePoints = CreatePlanePoints([0, 0, 1], 64);
        PointCloudData measuredPoints = CreatePlanePoints(measuredNormal, 64);
        using WorkflowContext context = new();
        context.Set("reference_plane_params", referencePlane);
        context.Set("reference_points", referencePoints);
        context.Set("measured_plane_params", measuredPlane);
        context.Set("measured_points", measuredPoints);

        using var op = new installation_plane_angle_inspection(
            minDeviationX: -0.5,
            maxDeviationX: 0.5,
            minDeviationY: -0.5,
            maxDeviationY: 0.5,
            maxTotalDeviation: 0.7,
            maxFitRmse: 0.001,
            minPointCount: 30
        );
        op.Execute(context);

        InspectionResultBase result = Assert.IsAssignableFrom<InspectionResultBase>(context.Get("result"));
        Assert.Equal(InspectionResultCode.NG, result.resultCode);
        Assert.True(result.isValid);
        Assert.False(result.isOk);
        Assert.InRange(result.ToDetailsNode()!["tiltX"]!.GetValue<double>(), 1.999, 2.001);
        Assert.InRange(result.ToDetailsNode()!["tiltY"]!.GetValue<double>(), -1.001, -0.999);
        Assert.InRange(result.ToDetailsNode()!["totalTilt"]!.GetValue<double>(), 2.235, 2.237);
        Assert.Contains(
            result.ToDetailsNode()!["qualityReasons"]!.AsArray(),
            reason => reason!.GetValue<string>().Contains("X方向偏差")
        );
    }

    [Fact]
    public void Execute_Should_Return_Unknown_When_Roi_Point_Count_Is_Insufficient()
    {
        using Mat plane = CreatePlane([0, 0, 1]);
        PointCloudData referencePoints = CreatePlanePoints([0, 0, 1], 4);
        PointCloudData measuredPoints = CreatePlanePoints([0, 0, 1], 4);
        using WorkflowContext context = new();
        context.Set("reference_plane_params", plane);
        context.Set("reference_points", referencePoints);
        context.Set("measured_plane_params", plane);
        context.Set("measured_points", measuredPoints);

        using var op = new installation_plane_angle_inspection(minPointCount: 30);
        op.Execute(context);

        InspectionResultBase result = Assert.IsAssignableFrom<InspectionResultBase>(context.Get("result"));
        Assert.Equal(InspectionResultCode.UNKNOWN, result.resultCode);
        Assert.False(result.isValid);
        Assert.False(result.isOk);
        Assert.NotEmpty(result.ToDetailsNode()!["qualityReasons"]!.AsArray());
    }

    private static Mat CreatePlane(double[] normal)
    {
        Mat plane = new(4, 1, MatType.CV_64FC1);
        plane.Set(0, 0, normal[0]);
        plane.Set(1, 0, normal[1]);
        plane.Set(2, 0, normal[2]);
        plane.Set(3, 0, 0d);
        return plane;
    }

    private static PointCloudData CreatePlanePoints(double[] normal, int count)
    {
        double[] helper = Math.Abs(normal[2]) < 0.9 ? [0, 0, 1] : [0, 1, 0];
        double[] u = Normalize(Cross(helper, normal));
        double[] v = Normalize(Cross(normal, u));
        Mat points = new(count, 3, MatType.CV_32FC1);
        for (int i = 0; i < count; i++)
        {
            double a = i % 8 - 3.5;
            double b = i / 8 - 3.5;
            points.Set(i, 0, (float)(a * u[0] + b * v[0]));
            points.Set(i, 1, (float)(a * u[1] + b * v[1]));
            points.Set(i, 2, (float)(a * u[2] + b * v[2]));
        }
        return new PointCloudData { Value = points };
    }

    private static double[] Cross(double[] a, double[] b) =>
        [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

    private static double[] Normalize(double[] value)
    {
        double length = Math.Sqrt(value.Sum(component => component * component));
        return value.Select(component => component / length).ToArray();
    }
}
