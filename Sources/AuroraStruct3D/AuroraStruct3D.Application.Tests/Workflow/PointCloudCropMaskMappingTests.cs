using AuroraStruct3D.OpenCV.PointCloudOps;
using AuroraStruct3D.OpenCV.RoiOps;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.OpenCV.Workflow;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class PointCloudCropMaskMappingTests
{
    [Fact]
    public void CropByMask_Should_Use_RoiMetadata_ProjectionMapping_When_Present()
    {
        using Mat cloudMat = new(2, 3, MatType.CV_32FC1, Scalar.All(0));
        cloudMat.Set(0, 0, 0f);
        cloudMat.Set(0, 1, 0f);
        cloudMat.Set(0, 2, 0f);

        cloudMat.Set(1, 0, 10f);
        cloudMat.Set(1, 1, 0f);
        cloudMat.Set(1, 2, 0f);

        PointCloudData input = new() { Value = cloudMat };

        using Mat mask = Mat.Zeros(11, 11, MatType.CV_8UC1);
        mask.Set(5, 5, (byte)255);

        var roiMetadata = new RoiPartitionMetadata
        {
            ProjectionMapping = new RoiProjectionMapping
            {
                ViewLabel = "XY",
                WorldMinX = -5,
                WorldMaxX = 5,
                WorldMinY = -5,
                WorldMaxY = 5,
                ImageWidth = 11,
                ImageHeight = 11,
            },
        };

        using WorkflowContext context = new();
        context.Set("input_point_cloud", input);
        context.Set("roi_mask", mask);
        context.Set("roi_metadata", System.Text.Json.JsonSerializer.Serialize(roiMetadata));

        using var op = new point_cloud_crop(cropMode: "mask");
        op.Execute(context);

        PointCloudData? output = context.Get<PointCloudData>("output_point_cloud");
        Assert.NotNull(output);
        Assert.NotNull(output!.PointCloud);
        Assert.Equal(1, output.PointCloud!.Rows);

        float x = output.PointCloud.Get<float>(0, 0);
        float y = output.PointCloud.Get<float>(0, 1);
        Assert.Equal(0f, x);
        Assert.Equal(0f, y);
    }

    [Fact]
    public void CropByMask_Should_Use_Xz_Mapping_Axes_When_ViewLabel_Is_Xz()
    {
        using Mat cloudMat = new(2, 3, MatType.CV_32FC1, Scalar.All(0));
        cloudMat.Set(0, 0, 0f);
        cloudMat.Set(0, 1, 100f);
        cloudMat.Set(0, 2, 0f);

        cloudMat.Set(1, 0, 10f);
        cloudMat.Set(1, 1, 100f);
        cloudMat.Set(1, 2, 10f);

        PointCloudData input = new() { Value = cloudMat };

        using Mat mask = Mat.Zeros(11, 11, MatType.CV_8UC1);
        mask.Set(5, 5, (byte)255);

        var roiMetadata = new RoiPartitionMetadata
        {
            ProjectionMapping = new RoiProjectionMapping
            {
                ViewLabel = "XZ",
                WorldMinX = -5,
                WorldMaxX = 5,
                WorldMinY = -5,
                WorldMaxY = 5,
                ImageWidth = 11,
                ImageHeight = 11,
            },
        };

        using WorkflowContext context = new();
        context.Set("input_point_cloud", input);
        context.Set("roi_mask", mask);
        context.Set("roi_metadata", System.Text.Json.JsonSerializer.Serialize(roiMetadata));

        using var op = new point_cloud_crop(cropMode: "mask");
        op.Execute(context);

        PointCloudData? output = context.Get<PointCloudData>("output_point_cloud");
        Assert.NotNull(output);
        Assert.NotNull(output!.PointCloud);
        Assert.Equal(1, output.PointCloud!.Rows);

        float x = output.PointCloud.Get<float>(0, 0);
        float z = output.PointCloud.Get<float>(0, 2);
        Assert.Equal(0f, x);
        Assert.Equal(0f, z);
    }

    [Fact]
    public void CropByMask_Should_Use_Yz_Mapping_Axes_When_ViewLabel_Is_Yz()
    {
        using Mat cloudMat = new(2, 3, MatType.CV_32FC1, Scalar.All(0));
        cloudMat.Set(0, 0, 100f);
        cloudMat.Set(0, 1, 0f);
        cloudMat.Set(0, 2, 0f);

        cloudMat.Set(1, 0, 100f);
        cloudMat.Set(1, 1, 10f);
        cloudMat.Set(1, 2, 10f);

        PointCloudData input = new() { Value = cloudMat };

        using Mat mask = Mat.Zeros(11, 11, MatType.CV_8UC1);
        mask.Set(5, 5, (byte)255);

        var roiMetadata = new RoiPartitionMetadata
        {
            ProjectionMapping = new RoiProjectionMapping
            {
                ViewLabel = "YZ",
                WorldMinX = -5,
                WorldMaxX = 5,
                WorldMinY = -5,
                WorldMaxY = 5,
                ImageWidth = 11,
                ImageHeight = 11,
            },
        };

        using WorkflowContext context = new();
        context.Set("input_point_cloud", input);
        context.Set("roi_mask", mask);
        context.Set("roi_metadata", System.Text.Json.JsonSerializer.Serialize(roiMetadata));

        using var op = new point_cloud_crop(cropMode: "mask");
        op.Execute(context);

        PointCloudData? output = context.Get<PointCloudData>("output_point_cloud");
        Assert.NotNull(output);
        Assert.NotNull(output!.PointCloud);
        Assert.Equal(1, output.PointCloud!.Rows);

        float y = output.PointCloud.Get<float>(0, 1);
        float z = output.PointCloud.Get<float>(0, 2);
        Assert.Equal(0f, y);
        Assert.Equal(0f, z);
    }
}
