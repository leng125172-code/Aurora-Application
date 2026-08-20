using System.Text.Json.Nodes;
using AuroraStruct3D.OpenCV.MeasureOps;
using AuroraStruct3D.OpenCV.PointCloudProjection;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.Workflow;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class InstallationAngleFeatureInspectionOperatorTests
{
    private const string TemplateJson =
        """
        {
          "features": [
            {
              "name": "horizontal",
              "start": { "x": 20, "y": 40 },
              "end": { "x": 100, "y": 40 },
              "searchRegion": [
                { "x": 10, "y": 25 }, { "x": 110, "y": 25 },
                { "x": 110, "y": 55 }, { "x": 10, "y": 55 }
              ]
            },
            {
              "name": "vertical",
              "start": { "x": 80, "y": 70 },
              "end": { "x": 80, "y": 140 },
              "searchRegion": [
                { "x": 65, "y": 60 }, { "x": 95, "y": 60 },
                { "x": 95, "y": 150 }, { "x": 65, "y": 150 }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void Should_Match_Actual_Lines_And_Use_Infinite_Line_Intersection()
    {
        using Mat image = Mat.Zeros(180, 180, MatType.CV_8UC1);
        Cv2.Line(image, new Point(20, 40), new Point(65, 40), Scalar.White, 2);
        Cv2.Line(image, new Point(80, 75), new Point(80, 140), Scalar.White, 2);

        using WorkflowContext context = new();
        context.Set("actual_image", image);
        using var op = new installation_angle_feature_inspection(
            TemplateJson,
            nominalAngle: 90,
            minDeviation: -2,
            maxDeviation: 2,
            minimumLineLength: 20
        );

        op.Execute(context);

        InspectionResultBase result = Assert.IsAssignableFrom<InspectionResultBase>(context.Get("result"));
        Assert.Equal(InspectionResultCode.OK, result.resultCode);
        Assert.True(result.isValid);
        Assert.True(result.isOk);
        JsonNode details = result.ToDetailsNode()!;
        Assert.InRange(details["MeasuredAngle"]!.GetValue<double>(), 89, 91);
        Assert.InRange(
            details["Intersection"]!["X"]!.GetValue<double>(),
            78,
            82
        );
        Assert.InRange(
            details["Intersection"]!["Y"]!.GetValue<double>(),
            38,
            42
        );
    }

    [Fact]
    public void Missing_Feature_Should_Return_Unknown_Instead_Of_Ng()
    {
        using Mat image = Mat.Zeros(180, 180, MatType.CV_8UC1);
        Cv2.Line(image, new Point(20, 40), new Point(100, 40), Scalar.White, 2);
        using WorkflowContext context = new();
        context.Set("actual_image", image);
        using var op = new installation_angle_feature_inspection(TemplateJson);

        op.Execute(context);

        InspectionResultBase result = Assert.IsAssignableFrom<InspectionResultBase>(context.Get("result"));
        Assert.Equal(InspectionResultCode.UNKNOWN, result.resultCode);
        Assert.False(result.isValid);
        Assert.False(result.isOk);
    }

    [Fact]
    public void Tilted_View_Should_Expose_Full_Result_Configuration_And_Validate_Angles()
    {
        Assert.Contains(
            colored_point_cloud_to_tilted_image.ConfigParameters!,
            x => x.Name == "outputWidth"
        );
        Assert.Contains(
            colored_point_cloud_to_tilted_image.ConfigParameters!,
            x => x.Name == "outputHeight"
        );
        Assert.Contains(
            colored_point_cloud_to_tilted_image.ConfigParameters!,
            x => x.Name == "backgroundColor"
        );
        Assert.Contains(
            colored_point_cloud_to_tilted_image.ConfigParameters!,
            x => x.Name == "autoFit"
        );
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new colored_point_cloud_to_tilted_image(tiltAngleX: 181)
        );
    }
}
