using AuroraStruct3D.OpenCV.GeometryOps;
using AuroraStruct3D.OpenCV.Workflow;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public sealed class AffineTransformOperatorTests
{
    [Fact]
    public void Execute_Should_Translate_Image_And_Expose_Applied_Matrix()
    {
        using Mat source = new(3, 3, MatType.CV_8UC1, Scalar.All(0));
        source.Set(1, 1, (byte)255);
        using WorkflowContext context = new();
        context.Set("input_mat", source);
        context.Set("transform_json", "[1,0,1,0,1,0]");

        using var sut = new affine_transform(interpolation: "Nearest");
        sut.Execute(context);

        using Mat output = context.Get<Mat>("output_mat")!;
        Assert.Equal((byte)255, output.At<byte>(1, 2));
        Assert.Equal("[1,0,1,0,1,0]", context.Get<string>("applied_transform_json"));
    }

    [Fact]
    public void Execute_Should_Reject_NonAffine_Matrix()
    {
        using Mat source = new(2, 2, MatType.CV_8UC1, Scalar.All(0));
        using WorkflowContext context = new();
        context.Set("input_mat", source);
        context.Set("transform_json", "[1,0,0]");

        using var sut = new affine_transform();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => sut.Execute(context));

        Assert.Contains("6 个", exception.Message);
    }
}
