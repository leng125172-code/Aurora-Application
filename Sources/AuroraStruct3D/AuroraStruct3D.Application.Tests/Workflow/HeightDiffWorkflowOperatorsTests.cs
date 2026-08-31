using System.Text.Json.Nodes;
using AuroraStruct3D.OpenCV.File.Images;
using AuroraStruct3D.OpenCV.PointCloudFit;
using AuroraStruct3D.OpenCV.PointCloudOps;
using AuroraStruct3D.OpenCV.RoiOps;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.Workflow;
using OpenCvSharp;
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
        Assert.Collection(outputs!, output => Assert.Equal("result", output.ParameterName));
        Assert.Contains(configs!, config => config.Name == "minDiff");
        Assert.Contains(configs!, config => config.Name == "maxDiff");
    }

    [Fact]
    public void HeightDiffRangeEval_Should_Calculate_Bound_Height_Variables()
    {
        using WorkflowContext context = new();
        context.Set("height_a", 2.824d);
        context.Set("height_b", 0.9296d);

        using var eval = new height_diff_range_eval(-0.2, 0.2);
        eval.Execute(context);

        InspectionResultBase result = Assert.IsAssignableFrom<InspectionResultBase>(context.Get("result"));
        JsonNode details = result.ToDetailsNode()!;
        Assert.Equal(2.824d, details["heightA"]!.GetValue<double>());
        Assert.Equal(0.9296d, details["heightB"]!.GetValue<double>());
        Assert.Equal(1.8944d, details["signedDiff"]!.GetValue<double>());
        Assert.Equal(InspectionResultCode.NG, result.resultCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("measure_height")]
    public void HeightDiffRangeEval_Should_Reject_Missing_Or_Literal_Variable_Name(object? invalidValue)
    {
        using WorkflowContext context = new();
        if (invalidValue is not null)
            context.Set("height_a", invalidValue);
        context.Set("height_b", 0.9296d);

        using var eval = new height_diff_range_eval(-0.2, 0.2);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => eval.Execute(context)
        );

        Assert.Contains("区域A高度", exception.Message);
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
        Assert.Contains(inputs!, input => input.ParameterName == "inspection_result");
        Assert.Single(outputs!);
        Assert.Equal("output_mat", outputs[0].ParameterName);
        Assert.Contains(configs!, config => config.Name == "okText");
        Assert.Contains(configs!, config => config.Name == "ngText");
    }

    [Fact]
    public void AnnotateHeightDiffResult_Should_Render_Status_And_Measurements()
    {
        using Mat input = Mat.Zeros(160, 320, MatType.CV_8UC3);
        using WorkflowContext context = new();
        context.Set("input_mat", input);
        context.Set("inspection_result", InspectionResults.CreateTyped(true, false,
            new HeightDiffInspectionDetails(12.345, 10.125, 2.22, 2.22, 0, 1, false)));
        context.Set(
            "roi_metadata_a",
            """{"rois":[{"name":"b","index":0}]}"""
        );
        context.Set(
            "roi_metadata_b",
            """{"rois":[{"name":"a","index":0}]}"""
        );

        using var annotate = new annotate_height_diff_result();
        annotate.Execute(context);

        Mat output = Assert.IsType<Mat>(context.Get<Mat>("output_mat"));
        Assert.Equal(4, output.Channels());
        using Mat gray = new();
        Cv2.CvtColor(output, gray, ColorConversionCodes.BGRA2GRAY);
        Assert.True(Cv2.CountNonZero(gray) > 0);
    }

    [Fact]
    public void AnnotateHeightDiffResult_Should_Report_Invalid_Metadata_Input()
    {
        using Mat input = Mat.Zeros(160, 320, MatType.CV_8UC3);
        using WorkflowContext context = new();
        context.Set("input_mat", input);
        context.Set("roi_metadata_a", "{invalid");
        context.Set("inspection_result", InspectionResults.CreateTyped(true, true,
            new HeightDiffInspectionDetails(1, 0, 1, 1, 0, 2, true)));

        using var annotate = new annotate_height_diff_result();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => annotate.Execute(context)
        );

        Assert.Contains("roi_metadata_a", exception.Message);
        Assert.Contains("JSON 解析失败", exception.Message);
    }

    [Fact]
    public void PlaneHeightDiff_Should_Prefer_Metadata_Names_And_Fallback_To_Config()
    {
        Assert.Equal(4, plane_height_diff.InputVisionParameters!.Count);
        Assert.Contains(
            plane_height_diff.ConfigParameters!,
            parameter => parameter.Name == "refRoiMetadata" && parameter.Required == false
        );
        Assert.Contains(
            plane_height_diff.ConfigParameters!,
            parameter => parameter.Name == "targetRoiMetadata" && parameter.Required == false
        );

        using Mat refPlane = CreatePlane(0);
        using Mat targetPlane = CreatePlane(-2);
        PointCloudData refCloud = CreateCloud(0);
        PointCloudData targetCloud = CreateCloud(2);

        using WorkflowContext metadataContext = CreatePlaneHeightContext(
            refPlane,
            targetPlane,
            refCloud,
            targetCloud
        );

        using var withMetadata = new plane_height_diff(
            "配置基准",
            "配置目标",
            -5,
            5,
            """{"rois":[{"name":"基准 区域","index":0}]}""",
            """{"rois":[{"name":"Target-b","index":0}]}"""
        );
        withMetadata.Execute(metadataContext);

        InspectionResultBase metadataResult = Assert.IsAssignableFrom<InspectionResultBase>(metadataContext.Get("result"));
        JsonNode metadataDetails = metadataResult.ToDetailsNode()!;
        Assert.Equal(
            "基准 区域",
            metadataDetails["refRegion"]!["name"]!.GetValue<string>()
        );
        Assert.Equal(
            "Target-b",
            metadataDetails["targetRegion"]!["name"]!.GetValue<string>()
        );

        using WorkflowContext fallbackContext = CreatePlaneHeightContext(
            refPlane,
            targetPlane,
            refCloud,
            targetCloud
        );
        using var withoutMetadata = new plane_height_diff("配置基准", "配置目标", -5, 5);
        withoutMetadata.Execute(fallbackContext);

        InspectionResultBase fallbackResult = Assert.IsAssignableFrom<InspectionResultBase>(fallbackContext.Get("result"));
        JsonNode fallbackDetails = fallbackResult.ToDetailsNode()!;
        Assert.Equal(
            "配置基准",
            fallbackDetails["refRegion"]!["name"]!.GetValue<string>()
        );
        Assert.Equal(
            "配置目标",
            fallbackDetails["targetRegion"]!["name"]!.GetValue<string>()
        );
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

    private static Mat CreatePlane(double d)
    {
        Mat plane = new(4, 1, MatType.CV_64FC1);
        plane.Set(0, 0, 0d);
        plane.Set(1, 0, 0d);
        plane.Set(2, 0, 1d);
        plane.Set(3, 0, d);
        return plane;
    }

    private static PointCloudData CreateCloud(float z)
    {
        Mat points = new(2, 3, MatType.CV_32FC1);
        points.Set(0, 0, 0f);
        points.Set(0, 1, 0f);
        points.Set(0, 2, z);
        points.Set(1, 0, 1f);
        points.Set(1, 1, 1f);
        points.Set(1, 2, z);
        return new PointCloudData { Value = points };
    }

    private static WorkflowContext CreatePlaneHeightContext(
        Mat refPlane,
        Mat targetPlane,
        PointCloudData refCloud,
        PointCloudData targetCloud
    )
    {
        WorkflowContext context = new();
        context.Set("ref_plane_params", refPlane);
        context.Set("target_plane_params", targetPlane);
        context.Set("ref_cloud", refCloud);
        context.Set("target_cloud", targetCloud);
        return context;
    }
}
