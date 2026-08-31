using AuroraStruct3D.OpenCV.PointCloudPlaneOps;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.Workflow;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class InstallationAngleStageTwoOperatorTests
{
    [Fact]
    public void AxisToPlane_Should_Output_Directional_Tilt()
    {
        using Mat plane = Plane([0, 0, 1]);
        double radians = 3 * Math.PI / 180;
        double[] direction = [0, -Math.Sin(radians), Math.Cos(radians)];
        using Mat axis = Line(direction);
        PointCloudData planePoints = PlanePoints(36);
        PointCloudData axisPoints = LinePoints(direction, 36);
        using WorkflowContext context = new();
        context.Set("reference_plane_params", plane);
        context.Set("reference_points", planePoints);
        context.Set("axis_params", axis);
        context.Set("axis_points", axisPoints);

        using var op = new installation_axis_to_plane_inspection(
            maxDeviationX: 0.5, maxDeviationY: 0.5,
            maxTotalDeviation: 0.7, maxFitRmse: 0.001, minPointCount: 20);
        op.Execute(context);

        Assert.Equal(InspectionResultCode.NG, Result(context).resultCode);
        Assert.InRange(Number(context, "tiltX"), 2.999, 3.001);
        Assert.InRange(Number(context, "axisToPlaneAngle"), 86.999, 87.001);
        Assert.Contains(
            Result(context).ToDetailsNode()!["reasons"]!.AsArray(),
            reason => reason!.GetValue<string>().Contains("X方向偏差")
        );
    }

    [Fact]
    public void AxisToPlane_Should_Accept_Cylinder_Params_And_Use_Surface_Rmse()
    {
        using Mat plane = Plane([0, 0, 1]);
        using Mat cylinder = Cylinder([0, 0, 1], 2);
        using WorkflowContext context = new();
        context.Set("reference_plane_params", plane);
        context.Set("reference_points", PlanePoints(36));
        context.Set("axis_params", cylinder);
        context.Set("axis_points", CylinderPoints(2, 40));

        using var op = new installation_axis_to_plane_inspection(
            maxFitRmse: 0.001, minPointCount: 20);
        op.Execute(context);

        Assert.Equal(InspectionResultCode.OK, Result(context).resultCode);
        Assert.True(Result(context).isValid);
    }

    [Fact]
    public void AxisToAxis_Should_Ignore_Direction_Sign_And_Measure_Acute_Angle()
    {
        double radians = 10 * Math.PI / 180;
        double[] referenceDirection = [1, 0, 0];
        double[] measuredDirection = [-Math.Cos(radians), -Math.Sin(radians), 0];
        using Mat reference = Line(referenceDirection);
        using Mat measured = Line(measuredDirection);
        using WorkflowContext context = new();
        context.Set("reference_axis_params", reference);
        context.Set("reference_axis_points", LinePoints(referenceDirection, 30));
        context.Set("measured_axis_params", measured);
        context.Set("measured_axis_points", LinePoints(measuredDirection, 30));

        using var op = new installation_axis_to_axis_inspection(
            nominalAngle: 10, maxAngleDeviation: 0.1,
            maxFitRmse: 0.001, minPointCount: 20);
        op.Execute(context);

        Assert.Equal(InspectionResultCode.OK, Result(context).resultCode);
        Assert.InRange(Number(context, "axisAngle"), 9.999, 10.001);
    }

    [Fact]
    public void Twist_Should_Output_Signed_Angle_On_Reference_Plane()
    {
        double radians = 15 * Math.PI / 180;
        double[] referenceDirection = [1, 0, 0];
        double[] measuredDirection = [Math.Cos(radians), Math.Sin(radians), 0];
        using Mat plane = Plane([0, 0, 1]);
        using Mat reference = Line(referenceDirection);
        using Mat measured = Line(measuredDirection);
        using WorkflowContext context = new();
        context.Set("reference_plane_params", plane);
        context.Set("reference_direction_params", reference);
        context.Set("reference_direction_points", LinePoints(referenceDirection, 30));
        context.Set("measured_direction_params", measured);
        context.Set("measured_direction_points", LinePoints(measuredDirection, 30));

        using var op = new installation_twist_inspection(
            nominalTwist: 15, maxTwistDeviation: 0.1,
            maxFitRmse: 0.001, minPointCount: 20);
        op.Execute(context);

        Assert.Equal(InspectionResultCode.OK, Result(context).resultCode);
        Assert.InRange(Number(context, "twistZ"), 14.999, 15.001);
    }

    private static InspectionResultBase Result(WorkflowContext context) =>
        Assert.IsAssignableFrom<InspectionResultBase>(context.Get("result"));

    private static double Number(WorkflowContext context, string name) =>
        Result(context).ToDetailsNode()![name]!.GetValue<double>();

    private static Mat Plane(double[] normal)
    {
        Mat result = new(4, 1, MatType.CV_64FC1);
        result.Set(0, 0, normal[0]); result.Set(1, 0, normal[1]);
        result.Set(2, 0, normal[2]); result.Set(3, 0, 0d);
        return result;
    }

    private static Mat Line(double[] direction)
    {
        Mat result = new(6, 1, MatType.CV_64FC1, Scalar.All(0));
        result.Set(3, 0, direction[0]); result.Set(4, 0, direction[1]);
        result.Set(5, 0, direction[2]);
        return result;
    }

    private static Mat Cylinder(double[] direction, double radius)
    {
        Mat result = new(7, 1, MatType.CV_64FC1, Scalar.All(0));
        result.Set(3, 0, direction[0]); result.Set(4, 0, direction[1]);
        result.Set(5, 0, direction[2]); result.Set(6, 0, radius);
        return result;
    }

    private static PointCloudData PlanePoints(int count)
    {
        Mat points = new(count, 3, MatType.CV_32FC1);
        for (int i = 0; i < count; i++)
        {
            points.Set(i, 0, (float)(i % 6));
            points.Set(i, 1, (float)(i / 6));
            points.Set(i, 2, 0f);
        }
        return new PointCloudData { Value = points };
    }

    private static PointCloudData LinePoints(double[] direction, int count)
    {
        Mat points = new(count, 3, MatType.CV_32FC1);
        for (int i = 0; i < count; i++)
        {
            double distance = i - count / 2d;
            points.Set(i, 0, (float)(distance * direction[0]));
            points.Set(i, 1, (float)(distance * direction[1]));
            points.Set(i, 2, (float)(distance * direction[2]));
        }
        return new PointCloudData { Value = points };
    }

    private static PointCloudData CylinderPoints(double radius, int count)
    {
        Mat points = new(count, 3, MatType.CV_32FC1);
        for (int i = 0; i < count; i++)
        {
            double angle = 2 * Math.PI * i / count;
            points.Set(i, 0, (float)(radius * Math.Cos(angle)));
            points.Set(i, 1, (float)(radius * Math.Sin(angle)));
            points.Set(i, 2, (float)(i % 5));
        }
        return new PointCloudData { Value = points };
    }
}
