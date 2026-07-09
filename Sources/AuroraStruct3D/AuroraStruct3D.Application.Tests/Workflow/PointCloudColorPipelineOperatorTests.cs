using AuroraStruct3D.OpenCV.PointCloudOps;
using AuroraStruct3D.OpenCV.PointCloudProjection;
using AuroraStruct3D.OpenCV.RoiOps;
using AuroraStruct3D.OpenCV.VisionParameters;
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

        ConfigParameter resolution = Assert.IsType<ConfigParameter>(Assert.Single(configs!));
        Assert.Equal("imageResolution", resolution.Name);
        Assert.Equal(PortControlType.Select, resolution.ControlType);
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
}