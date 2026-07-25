using AuroraStruct3D.OpenCV.File.PointCloud;
using AuroraStruct3D.OpenCV.PointCloudDistance;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.Workflow;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class ProductModelComparisonOperatorTests
{
    [Fact]
    public void ReadProductModel_Should_Expose_Model_Selector()
    {
        IConfigParameter config = Assert.Single(read_product_model.ConfigParameters!);
        Assert.Equal("productModelId", config.Name);
        Assert.True(config.Required);
        Assert.Equal(PortControlType.ProductModelSelect, config.ControlType);
        Assert.Contains(
            read_product_model.OutputVisionParameters!,
            output =>
                output.ParameterName == "model_point_cloud" && output is PointCloudData
        );
    }

    [Fact]
    public void CloudCompare_Should_Expose_Industrial_Judgement_Outputs()
    {
        Assert.Contains(
            cloud_compare.OutputVisionParameters!,
            output => output.ParameterName == "is_ok"
        );
        Assert.Contains(
            cloud_compare.OutputVisionParameters!,
            output => output.ParameterName == "max_distance"
        );
        Assert.Contains(
            cloud_compare.OutputVisionParameters!,
            output => output.ParameterName == "mean_distance"
        );
        Assert.Contains(
            cloud_compare.OutputVisionParameters!,
            output => output.ParameterName == "defect_ratio"
        );
        Assert.Contains(
            cloud_compare.OutputVisionParameters!,
            output => output.ParameterName == "missing_ratio"
        );
        Assert.Contains(
            cloud_compare.ConfigParameters!,
            config => config.Name == "maxDefectRatio"
        );
        Assert.Contains(
            cloud_compare.ConfigParameters!,
            config => config.Name == "maxMeanDistance"
        );
        Assert.Contains(
            cloud_compare.ConfigParameters!,
            config => config.Name == "maxMissingRatio"
        );
    }
}
