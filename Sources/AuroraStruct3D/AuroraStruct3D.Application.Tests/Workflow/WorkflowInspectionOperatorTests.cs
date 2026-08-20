using System.Text.Json;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.OpenCV.Workflow.Operators;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class WorkflowInspectionOperatorTests
{
    [Fact]
    public void Operators_Should_Expose_Unified_Judgement_Contract()
    {
        AssertUnifiedOutputs(shape_inspection.OutputVisionParameters!);
        AssertUnifiedOutputs(dimension_angle_inspection.OutputVisionParameters!);
        AssertUnifiedOutputs(flatness_inspection.OutputVisionParameters!);

        Assert.Equal(
            new[] { "expectedShape", "expectedCount", "allowAdditional" },
            shape_inspection.ConfigParameters!.Select(x => x.Name)
        );
        Assert.Equal(
            new[]
            {
                "checkLength",
                "nominalLength",
                "minLengthDeviation",
                "maxLengthDeviation",
                "checkAngle",
                "nominalAngle",
                "minAngleDeviation",
                "maxAngleDeviation",
            },
            dimension_angle_inspection.ConfigParameters!.Select(x => x.Name)
        );
        Assert.Equal(
            new[] { "maxFlatness", "maxAbsoluteDistance" },
            flatness_inspection.ConfigParameters!.Select(x => x.Name)
        );
    }

    [Theory]
    [InlineData(2, false, "OK")]
    [InlineData(3, false, "NG")]
    [InlineData(3, true, "OK")]
    [InlineData(1, true, "NG")]
    public void ShapeInspection_Should_Judge_Expected_Count(
        int detectedCount,
        bool allowAdditional,
        string expectedStatus
    )
    {
        using WorkflowContext context = new();
        context.Set("detected_count", detectedCount);
        context.Set("detection_json", """{"source":"rectangle"}""");

        using var sut = new shape_inspection("rectangle", 2, allowAdditional);
        sut.Execute(context);

        InspectionResultBase inspection = Result(context);
        Assert.Equal(expectedStatus, inspection.resultCode.ToString());
        Assert.Equal(expectedStatus == "OK", inspection.isOk);
        using JsonDocument result = JsonDocument.Parse(inspection.ToDetailsNode()!.ToJsonString());
        Assert.Equal(
            "rectangle",
            result.RootElement.GetProperty("expectedShape").GetString()
        );
        Assert.Equal(
            "rectangle",
            result.RootElement.GetProperty("detection").GetProperty("source").GetString()
        );
    }

    [Fact]
    public void ShapeInspection_Should_Return_Unknown_When_Count_Is_Missing()
    {
        using WorkflowContext context = new();
        using var sut = new shape_inspection();

        sut.Execute(context);

        Assert.False(Result(context).isValid);
        Assert.Equal(InspectionResultCode.UNKNOWN, Result(context).resultCode);
    }

    [Theory]
    [InlineData(10.2, 89.6, "OK")]
    [InlineData(10.21, 89.6, "NG")]
    [InlineData(10.2, 89.49, "NG")]
    public void DimensionAngleInspection_Should_Apply_Inclusive_Tolerances(
        double length,
        double angle,
        string expectedStatus
    )
    {
        using WorkflowContext context = new();
        context.Set("measured_length", length);
        context.Set("measured_angle", angle);
        using var sut = new dimension_angle_inspection(
            true,
            10,
            -0.2,
            0.2,
            true,
            90,
            -0.5,
            0.5
        );

        sut.Execute(context);

        Assert.Equal(expectedStatus, Result(context).resultCode.ToString());
        Assert.Equal(expectedStatus == "OK", Result(context).isOk);
    }

    [Fact]
    public void DimensionAngleInspection_Should_Not_Require_Disabled_Measurement()
    {
        using WorkflowContext context = new();
        context.Set("measured_length", 5.05);
        using var sut = new dimension_angle_inspection(
            checkLength: true,
            nominalLength: 5,
            minLengthDeviation: -0.1,
            maxLengthDeviation: 0.1,
            checkAngle: false
        );

        sut.Execute(context);

        Assert.True(Result(context).isValid);
        Assert.True(Result(context).ToDetailsNode()!["angle"]!["isOk"]!.GetValue<bool>());
        Assert.Equal(InspectionResultCode.OK, Result(context).resultCode);
    }

    [Fact]
    public void DimensionAngleInspection_Should_Return_Unknown_When_Enabled_Input_Is_Missing()
    {
        using WorkflowContext context = new();
        context.Set("measured_length", 10d);
        using var sut = new dimension_angle_inspection();

        sut.Execute(context);

        Assert.False(Result(context).isValid);
        Assert.Equal(InspectionResultCode.UNKNOWN, Result(context).resultCode);
    }

    [Theory]
    [InlineData(0.1, 0.2, "OK")]
    [InlineData(0.1001, 0.2, "NG")]
    [InlineData(0.1, 0.2001, "NG")]
    public void FlatnessInspection_Should_Apply_Inclusive_Limits(
        double flatness,
        double absoluteDistance,
        string expectedStatus
    )
    {
        using WorkflowContext context = new();
        context.Set("measured_flatness", flatness);
        context.Set("max_absolute_distance", absoluteDistance);
        using var sut = new flatness_inspection(0.1, 0.2);

        sut.Execute(context);

        Assert.Equal(expectedStatus, Result(context).resultCode.ToString());
        Assert.Equal(expectedStatus == "OK", Result(context).isOk);
    }

    [Fact]
    public void FlatnessInspection_Should_Return_Unknown_When_Required_Input_Is_Missing()
    {
        using WorkflowContext context = new();
        using var sut = new flatness_inspection(0.1, 0.2);

        sut.Execute(context);

        Assert.False(Result(context).isValid);
        Assert.Equal(InspectionResultCode.UNKNOWN, Result(context).resultCode);
    }

    private static void AssertUnifiedOutputs(
        IEnumerable<AuroraStruct3D.OpenCV.VisionParameters.IVisionParameter> outputs
    )
    {
        var output = Assert.Single(outputs);
        Assert.Equal("result", output.ParameterName);
        Assert.True(typeof(InspectionResultBase).IsAssignableFrom(output.ParameterType));
    }

    private static InspectionResultBase Result(WorkflowContext context) =>
        Assert.IsAssignableFrom<InspectionResultBase>(context.Get("result"));
}
