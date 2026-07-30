using System.Text.Json;
using AuroraStruct3D.OpenCV.TemplateOps;
using AuroraStruct3D.OpenCV.Workflow;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public sealed class OrbFeatureMatchOperatorTests
{
    [Fact]
    public void Execute_Should_Return_NotFound_For_Featureless_Images()
    {
        using Mat reference = new(32, 32, MatType.CV_8UC1, Scalar.All(0));
        using Mat input = new(32, 32, MatType.CV_8UC1, Scalar.All(0));
        using WorkflowContext context = new();
        context.Set("reference_mat", reference);
        context.Set("input_mat", input);

        using var sut = new orb_feature_match();
        sut.Execute(context);

        Assert.False(context.Get<bool>("is_found"));
        using JsonDocument result = JsonDocument.Parse(context.Get<string>("match_result_json")!);
        Assert.Equal(0, result.RootElement.GetProperty("matchCount").GetInt32());
        Assert.False(result.RootElement.GetProperty("isFound").GetBoolean());
    }

    [Fact]
    public void Constructor_Should_Reject_Invalid_Ransac_Configuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new orb_feature_match(ransacReprojectionThreshold: 0));
    }
}
