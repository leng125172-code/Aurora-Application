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

        Assert.Equal(expectedStatus, context.Get<string>("inspection_status"));
        Assert.Equal(expectedStatus == "OK", context.Get<bool>("is_ok"));
        using JsonDocument result = JsonDocument.Parse(context.Get<string>("result_json")!);
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

        Assert.False(context.Get<bool>("is_valid"));
        Assert.Equal("UNKNOWN", context.Get<string>("inspection_status"));
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

        Assert.Equal(expectedStatus, context.Get<string>("inspection_status"));
        Assert.Equal(expectedStatus == "OK", context.Get<bool>("is_ok"));
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

        Assert.True(context.Get<bool>("is_valid"));
        Assert.True(context.Get<bool>("angle_ok"));
        Assert.Equal("OK", context.Get<string>("inspection_status"));
    }

    [Fact]
    public void DimensionAngleInspection_Should_Return_Unknown_When_Enabled_Input_Is_Missing()
    {
        using WorkflowContext context = new();
        context.Set("measured_length", 10d);
        using var sut = new dimension_angle_inspection();

        sut.Execute(context);

        Assert.False(context.Get<bool>("is_valid"));
        Assert.Equal("UNKNOWN", context.Get<string>("inspection_status"));
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

        Assert.Equal(expectedStatus, context.Get<string>("inspection_status"));
        Assert.Equal(expectedStatus == "OK", context.Get<bool>("is_ok"));
    }

    [Fact]
    public void FlatnessInspection_Should_Return_Unknown_When_Required_Input_Is_Missing()
    {
        using WorkflowContext context = new();
        using var sut = new flatness_inspection(0.1, 0.2);

        sut.Execute(context);

        Assert.False(context.Get<bool>("is_valid"));
        Assert.Equal("UNKNOWN", context.Get<string>("inspection_status"));
    }

    private static void AssertUnifiedOutputs(
        IEnumerable<AuroraStruct3D.OpenCV.VisionParameters.IVisionParameter> outputs
    )
    {
        Assert.Contains(outputs, x => x.ParameterName == "is_valid");
        Assert.Contains(outputs, x => x.ParameterName == "is_ok");
        Assert.Contains(outputs, x => x.ParameterName == "inspection_status");
        Assert.Contains(outputs, x => x.ParameterName == "result_json");
    }
}
