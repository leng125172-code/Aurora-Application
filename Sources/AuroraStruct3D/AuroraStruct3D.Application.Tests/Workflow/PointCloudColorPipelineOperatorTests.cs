using AuroraStruct3D.OpenCV.PointCloudOps;
using AuroraStruct3D.OpenCV.PointCloudProjection;
using AuroraStruct3D.OpenCV.PointCloudSegmentation;
using AuroraStruct3D.OpenCV.RoiOps;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.Workflow;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class PointCloudColorPipelineOperatorTests
{
    [Fact]
    public void ZColorizePointCloud_Should_Expose_PointCloud_Input_Output_And_Config()
    {
        List<IVisionParameter>? inputs = z_colorize_point_cloud.InputVisionParameters;
        List<IVisionParameter>? outputs = z_colorize_point_cloud.OutputVisionParameters;
        List<IConfigParameter>? configs = z_colorize_point_cloud.ConfigParameters;

        Assert.NotNull(inputs);
        Assert.NotNull(outputs);
        Assert.NotNull(configs);

        Assert.Single(inputs!);
        Assert.IsType<PointCloudData>(inputs[0]);
        Assert.Equal("input_point_cloud", inputs[0].ParameterName);

        Assert.Single(outputs!);
        Assert.IsType<PointCloudData>(outputs[0]);
        Assert.Equal("output_point_cloud", outputs[0].ParameterName);

        Assert.Contains(configs!, config => config.Name == "colorMap");
        Assert.Contains(configs!, config => config.Name == "autoRange");
        Assert.Contains(configs!, config => config.Name == "minZ");
        Assert.Contains(configs!, config => config.Name == "maxZ");
        Assert.Contains(configs!, config => config.Name == "invert");
    }

    [Fact]
    public void ColoredPointCloudToImage_Should_Expose_PointCloud_Input_And_Image_Output()
    {
        List<IVisionParameter>? inputs = colored_point_cloud_to_image.InputVisionParameters;
        List<IVisionParameter>? outputs = colored_point_cloud_to_image.OutputVisionParameters;
        List<IConfigParameter>? configs = colored_point_cloud_to_image.ConfigParameters;

        Assert.NotNull(inputs);
        Assert.NotNull(outputs);
        Assert.NotNull(configs);

        Assert.Single(inputs!);
        Assert.IsType<PointCloudData>(inputs[0]);
        Assert.Equal("input_point_cloud", inputs[0].ParameterName);

        Assert.Single(outputs!);
        Assert.IsType<MatImg>(outputs[0]);
        Assert.Equal("output_image", outputs[0].ParameterName);

        Assert.Contains(configs!, config => config.Name == "autoBounds");
        Assert.Contains(configs!, config => config.Name == "imageResolution");
        Assert.Contains(configs!, config => config.Name == "minX");
        Assert.Contains(configs!, config => config.Name == "maxX");
        Assert.Contains(configs!, config => config.Name == "minY");
        Assert.Contains(configs!, config => config.Name == "maxY");
    }

    [Fact]
    public void LabelColorizePointCloud_Should_Expose_PointCloud_Input_Output_And_Config()
    {
        List<IVisionParameter>? inputs = label_colorize_point_cloud.InputVisionParameters;
        List<IVisionParameter>? outputs = label_colorize_point_cloud.OutputVisionParameters;
        List<IConfigParameter>? configs = label_colorize_point_cloud.ConfigParameters;

        Assert.NotNull(inputs);
        Assert.NotNull(outputs);
        Assert.NotNull(configs);

        Assert.Single(inputs!);
        Assert.IsType<PointCloudData>(inputs[0]);
        Assert.Single(outputs!);
        Assert.IsType<PointCloudData>(outputs[0]);
        Assert.Contains(configs!, config => config.Name == "noiseAsBlack");
    }

    [Fact]
    public void OverlayRoiMarkers_Should_Expose_Image_Mask_And_Metadata_Inputs()
    {
        List<IVisionParameter>? inputs = overlay_roi_markers.InputVisionParameters;
        List<IVisionParameter>? outputs = overlay_roi_markers.OutputVisionParameters;
        List<IConfigParameter>? configs = overlay_roi_markers.ConfigParameters;

        Assert.NotNull(inputs);
        Assert.NotNull(outputs);
        Assert.NotNull(configs);

        Assert.Collection(
            inputs!,
            input =>
            {
                Assert.IsType<MatImg>(input);
                Assert.Equal("input_mat", input.ParameterName);
            },
            input =>
            {
                VisionParameter<List<Mat>> masks = Assert.IsType<VisionParameter<List<Mat>>>(input);
                Assert.Equal("roi_masks", masks.ParameterName);
                Assert.Equal(PortControlType.Variable, masks.ControlType);
            },
            input =>
            {
                VisionParameter<string> metadata = Assert.IsType<VisionParameter<string>>(input);
                Assert.Equal("roi_metadata", metadata.ParameterName);
                Assert.Equal(PortControlType.Variable, metadata.ControlType);
            }
        );

        MatImg output = Assert.IsType<MatImg>(Assert.Single(outputs!));
        Assert.Equal("output_mat", output.ParameterName);

        Assert.Contains(configs!, config => config.Name == "overlayAlpha");
        Assert.Contains(configs!, config => config.Name == "borderThickness");
        Assert.Contains(configs!, config => config.Name == "showLabel");
    }

    [Fact]
    public void RoiPartition_Should_Expose_PrimaryMask_Output_For_SingleRoi_Workflows()
    {
        List<IVisionParameter>? outputs = roi_partition.OutputVisionParameters;

        Assert.NotNull(outputs);
        Assert.Contains(outputs!, output => output.ParameterName == "primary_mask");
        Assert.Contains(outputs!, output => output.ParameterName == "output_masks");
        Assert.Contains(outputs!, output => output.ParameterName == "roi_metadata");
    }

    [Fact]
    public void ColoredPointCloudToImage_Should_Keep_AspectRatio_And_Use_Transparent_Background()
    {
        using Mat cloudMat = new(2, 6, MatType.CV_32FC1, Scalar.All(0));
        cloudMat.Set(0, 0, 0f);
        cloudMat.Set(0, 1, 0f);
        cloudMat.Set(0, 2, 0f);
        cloudMat.Set(0, 3, 255f);
        cloudMat.Set(0, 4, 0f);
        cloudMat.Set(0, 5, 0f);

        cloudMat.Set(1, 0, 4f);
        cloudMat.Set(1, 1, 2f);
        cloudMat.Set(1, 2, 0f);
        cloudMat.Set(1, 3, 0f);
        cloudMat.Set(1, 4, 255f);
        cloudMat.Set(1, 5, 0f);

        PointCloudData pointCloud = new() { Value = cloudMat };

        using WorkflowContext context = new();
        context.Set("input_point_cloud", pointCloud);

        using var op = new colored_point_cloud_to_image(
            autoBounds: false,
            imageResolution: 200,
            minX: 0,
            maxX: 4,
            minY: 0,
            maxY: 2
        );

        op.Execute(context);

        Mat? output = context.Get<Mat>("output_image");
        Assert.NotNull(output);
        Assert.False(output!.Empty());
        Assert.Equal(4, output.Channels());
        Assert.Equal(200, output.Width);
        Assert.Equal(100, output.Height);

        Vec4b filledPixel = output.Get<Vec4b>(0, 0);
        Assert.Equal(255, filledPixel.Item2);
        Assert.Equal(255, filledPixel.Item3);

        Vec4b emptyPixel = output.Get<Vec4b>(50, 100);
        Assert.Equal(0, emptyPixel.Item3);
    }

    [Fact]
    public void AnnotateHeightDiffResult_Should_Preserve_Transparent_Background()
    {
        const string roiMetadata =
            "{\"rois\":[{\"name\":\"A\",\"type\":\"Rect\",\"index\":0,\"area\":100,\"centroid\":{\"x\":15,\"y\":15},\"boundingRect\":{\"x\":10,\"y\":10,\"width\":10,\"height\":10},\"contourPoints\":[{\"x\":10,\"y\":10},{\"x\":20,\"y\":10},{\"x\":20,\"y\":20},{\"x\":10,\"y\":20}]}],\"totalArea\":100}";

        using Mat input = new(80, 120, MatType.CV_8UC4, new Scalar(0, 0, 0, 0));
        using WorkflowContext context = new();
        context.Set("input_mat", input);
        context.Set("roi_metadata_a", roiMetadata);
        context.Set("roi_metadata_b", roiMetadata);
        context.Set("height_a", 1.2d);
        context.Set("height_b", 1.0d);
        context.Set("signed_diff", 0.2d);
        context.Set("is_ok", true);

        using var op = new annotate_height_diff_result();
        op.Execute(context);

        Mat? output = context.Get<Mat>("output_mat");
        Assert.NotNull(output);
        Assert.False(output!.Empty());
        Assert.Equal(4, output.Channels());

        Vec4b corner = output.Get<Vec4b>(79, 119);
        Assert.Equal(0, corner.Item3);
    }
}
