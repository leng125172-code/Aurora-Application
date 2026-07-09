using AuroraStruct3D.OpenCV.File.Images;
using AuroraStruct3D.OpenCV.PointCloudOps;
using AuroraStruct3D.OpenCV.RoiOps;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.Workflow;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class HeightDiffWorkflowOperatorsTests
{
    [Fact]
    public void HeightDiffRangeEval_Should_Expose_Expected_Contracts()
    {
        List<IVisionParameter>? inputs = height_diff_range_eval.InputVisionParameters;
        List<IVisionParameter>? outputs = height_diff_range_eval.OutputVisionParameters;
        List<IConfigParameter>? configs = height_diff_range_eval.ConfigParameters;

        Assert.NotNull(inputs);
        Assert.NotNull(outputs);
        Assert.NotNull(configs);
        Assert.Collection(
            inputs!,
            input => Assert.Equal("height_a", input.ParameterName),
            input => Assert.Equal("height_b", input.ParameterName)
        );
        Assert.Contains(outputs!, output => output.ParameterName == "signed_diff");
        Assert.Contains(outputs!, output => output.ParameterName == "abs_diff");
        Assert.Contains(outputs!, output => output.ParameterName == "is_ok");
        Assert.Contains(outputs!, output => output.ParameterName == "result_json");
        Assert.Contains(configs!, config => config.Name == "minDiff");
        Assert.Contains(configs!, config => config.Name == "maxDiff");
    }

    [Fact]
    public void AnnotateHeightDiffResult_Should_Expose_Expected_Contracts()
    {
        List<IVisionParameter>? inputs = annotate_height_diff_result.InputVisionParameters;
        List<IVisionParameter>? outputs = annotate_height_diff_result.OutputVisionParameters;
        List<IConfigParameter>? configs = annotate_height_diff_result.ConfigParameters;

        Assert.NotNull(inputs);
        Assert.NotNull(outputs);
        Assert.NotNull(configs);
        Assert.Contains(inputs!, input => input.ParameterName == "input_mat");
        Assert.Contains(inputs!, input => input.ParameterName == "roi_metadata_a");
        Assert.Contains(inputs!, input => input.ParameterName == "roi_metadata_b");
        Assert.Contains(inputs!, input => input.ParameterName == "height_a");
        Assert.Contains(inputs!, input => input.ParameterName == "height_b");
        Assert.Contains(inputs!, input => input.ParameterName == "signed_diff");
        Assert.Contains(inputs!, input => input.ParameterName == "is_ok");
        Assert.Single(outputs!);
        Assert.Equal("output_mat", outputs[0].ParameterName);
        Assert.Contains(configs!, config => config.Name == "okText");
        Assert.Contains(configs!, config => config.Name == "ngText");
    }

    [Fact]
    public void SaveImageToBlob_Should_Expose_Image_Input_And_Download_Outputs()
    {
        List<IVisionParameter>? inputs = save_image_to_blob.InputVisionParameters;
        List<IVisionParameter>? outputs = save_image_to_blob.OutputVisionParameters;

        Assert.NotNull(inputs);
        Assert.NotNull(outputs);
        Assert.Single(inputs!);
        Assert.IsType<MatImg>(inputs[0]);
        Assert.Collection(
            outputs!,
            output => Assert.Equal("blob_name", output.ParameterName),
            output => Assert.Equal("download_url", output.ParameterName)
        );
    }
}
