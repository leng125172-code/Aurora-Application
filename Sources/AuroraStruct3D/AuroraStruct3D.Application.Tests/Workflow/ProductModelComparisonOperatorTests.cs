using AuroraStruct3D.OpenCV.File.PointCloud;
using AuroraStruct3D.OpenCV.PointCloudDistance;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.Workflow;
using OpenCvSharp;
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
        Assert.Contains(cloud_compare.OutputVisionParameters!, output =>
            output.ParameterName == "result" && typeof(InspectionResultBase).IsAssignableFrom(output.ParameterType));
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

        string[] requiredBusinessThresholds =
        [
            "distanceThreshold",
            "maxDefectRatio",
            "maxMeanDistance",
            "maxMissingRatio",
        ];
        foreach (string name in requiredBusinessThresholds)
        {
            IConfigParameter parameter = Assert.Single(
                cloud_compare.ConfigParameters!,
                config => config.Name == name
            );
            Assert.True(parameter.Required);
            Assert.Null(parameter.DefaultValue);
        }

        Assert.Contains(
            cloud_compare.ConfigParameters!,
            config => config.Name == "minCoarseInlierRatio"
        );
        Assert.Contains(
            cloud_compare.ConfigParameters!,
            config => config.Name == "minFineCorrespondenceRatio"
        );
        Assert.Contains(
            cloud_compare.ConfigParameters!,
            config => config.Name == "maxRegistrationRmse"
        );

        // 新参数只追加到构造器尾部，保留旧的十参数位置调用。
        using var legacyConstruction = new cloud_compare(
            false,
            "icp",
            10,
            1,
            0.1,
            32,
            0.1,
            0.2,
            1,
            0.3
        );
    }

    [Fact]
    public void CloudCompare_Should_Use_Capped_Registration_Cloud_But_Return_Full_Colored_Source()
    {
        const int width = 50;
        const int height = 50;
        using WorkflowContext context = new();
        context.Set("source_cloud", CreateDenseCloud(width, height, withColors: true));
        context.Set("target_cloud", CreateDenseCloud(width, height, withColors: false));
        using var op = new cloud_compare(
            useCoarseRegistration: false,
            registrationMethod: "icp",
            maxIterations: 10,
            maxCorrespondenceDistance: 2,
            distanceThreshold: 0.01,
            imageResolution: 32,
            voxelSize: 0.01,
            maxDefectRatio: 0,
            maxMeanDistance: 0.01,
            maxMissingRatio: 0,
            minFineCorrespondenceRatio: 0.9,
            maxRegistrationRmse: 0.01
        );

        op.Execute(context);

        PointCloudData aligned = Assert.IsType<PointCloudData>(
            context.Get<PointCloudData>("aligned_cloud")
        );
        Assert.Equal(width * height, aligned.PointCount);
        Assert.True(aligned.HasColors);
        Assert.Equal(width * height, aligned.Colors!.Rows);
        Assert.Equal((byte)249, aligned.Colors.Get<byte>(249, 0));

        InspectionResult<cloud_compare.CloudCompareResult> inspection = Assert.IsType<
            InspectionResult<cloud_compare.CloudCompareResult>
        >(context.Get("result"));
        cloud_compare.CloudCompareResult details = Assert.IsType<cloud_compare.CloudCompareResult>(
            inspection.details
        );
        Assert.True(inspection.isValid);
        Assert.True(inspection.isOk);
        Assert.Equal(width * height, details.SourcePointCount);
        Assert.Equal(width * height, details.TargetPointCount);
        Assert.InRange(details.SourceRegistrationPointCount, 3, 2_000);
        Assert.InRange(details.TargetRegistrationPointCount, 3, 2_000);
        Assert.Equal(width * height, details.MatchedSourcePointCount);
        Assert.Equal(0, details.UnmatchedSourcePointCount);
        Assert.Equal(width * height, details.MatchedTargetPointCount);
        Assert.Equal(0, details.UnmatchedTargetPointCount);
        Assert.Equal(0.01, details.EffectiveVoxelSizeMm, 6);
        Assert.Equal(2, details.EffectiveMaxCorrespondenceDistanceMm, 6);
    }

    [Fact]
    public void CloudCompare_Should_Accept_Identical_Asymmetric_Point_Clouds()
    {
        using WorkflowContext context = new();
        context.Set("source_cloud", CreateAsymmetricCloudWithNormals(includeNormals: false));
        context.Set("target_cloud", CreateAsymmetricCloudWithNormals(includeNormals: false));
        using var op = new cloud_compare(
            useCoarseRegistration: false,
            registrationMethod: "icp",
            maxIterations: 10,
            maxCorrespondenceDistance: 0.2,
            distanceThreshold: 0.01,
            imageResolution: 32,
            voxelSize: 0.001,
            maxDefectRatio: 0,
            maxMeanDistance: 0.01,
            maxMissingRatio: 0,
            minFineCorrespondenceRatio: 0.9,
            maxRegistrationRmse: 0.01
        );

        op.Execute(context);

        InspectionResult<cloud_compare.CloudCompareResult> inspection = Assert.IsType<
            InspectionResult<cloud_compare.CloudCompareResult>
        >(context.Get("result"));
        cloud_compare.CloudCompareResult details = Assert.IsType<cloud_compare.CloudCompareResult>(
            inspection.details
        );
        Assert.True(inspection.isValid);
        Assert.True(inspection.isOk);
        Assert.Equal(20, details.MatchedSourcePointCount);
        Assert.Equal(20, details.MatchedTargetPointCount);
        Assert.InRange(details.HeightDifference.MaxDistance, 0, 1e-5);
        Assert.InRange(details.RegistrationRmseMm, 0, 1e-5);
    }

    [Fact]
    public void CloudCompare_Coarse_And_Fine_Should_Recover_Known_Rigid_Transform_Deterministically()
    {
        const double angle = Math.PI / 360;
        const double tx = 0.4;
        const double ty = -0.3;
        const double tz = 0.15;
        using var op = new cloud_compare(
            useCoarseRegistration: true,
            registrationMethod: "icp",
            maxIterations: 30,
            maxCorrespondenceDistance: 0.25,
            distanceThreshold: 0.02,
            imageResolution: 32,
            voxelSize: 0.02,
            maxDefectRatio: 0,
            maxMeanDistance: 0.02,
            maxMissingRatio: 0,
            minCoarseInlierRatio: 0.1,
            minFineCorrespondenceRatio: 0.8,
            maxRegistrationRmse: 0.02
        );
        using WorkflowContext firstContext = CreateKnownTransformContext(angle, tx, ty, tz);
        using WorkflowContext secondContext = CreateKnownTransformContext(angle, tx, ty, tz);

        op.Execute(firstContext);
        op.Execute(secondContext);

        InspectionResult<cloud_compare.CloudCompareResult> firstInspection = Assert.IsType<
            InspectionResult<cloud_compare.CloudCompareResult>
        >(firstContext.Get("result"));
        InspectionResult<cloud_compare.CloudCompareResult> secondInspection = Assert.IsType<
            InspectionResult<cloud_compare.CloudCompareResult>
        >(secondContext.Get("result"));
        cloud_compare.CloudCompareResult first = Assert.IsType<cloud_compare.CloudCompareResult>(
            firstInspection.details
        );
        cloud_compare.CloudCompareResult second = Assert.IsType<cloud_compare.CloudCompareResult>(
            secondInspection.details
        );
        Mat firstTransform = Assert.IsType<Mat>(firstContext.Get("transform_matrix"));
        Mat secondTransform = Assert.IsType<Mat>(secondContext.Get("transform_matrix"));

        Assert.True(firstInspection.isOk);
        Assert.True(first.UsedCoarseRegistration);
        Assert.True(first.CoarseInlierRatio >= 0.1);
        Assert.True(first.FineCorrespondenceRatio >= 0.8);
        Assert.InRange(Math.Abs(firstTransform.Get<double>(0, 0) - Math.Cos(angle)), 0, 0.01);
        Assert.InRange(Math.Abs(firstTransform.Get<double>(1, 0) - Math.Sin(angle)), 0, 0.01);
        Assert.InRange(Math.Abs(firstTransform.Get<double>(0, 3) - tx), 0, 0.03);
        Assert.InRange(Math.Abs(firstTransform.Get<double>(1, 3) - ty), 0, 0.03);
        Assert.InRange(Math.Abs(firstTransform.Get<double>(2, 3) - tz), 0, 0.03);
        Assert.InRange(first.HeightDifference.MaxDistance, 0, 0.02);

        for (int row = 0; row < 4; row++)
        for (int col = 0; col < 4; col++)
            Assert.Equal(
                firstTransform.Get<double>(row, col),
                secondTransform.Get<double>(row, col),
                10
            );
        Assert.Equal(first.CoarseInlierCount, second.CoarseInlierCount);
        Assert.Equal(first.FineCorrespondenceCount, second.FineCorrespondenceCount);
        Assert.Equal(first.RegistrationRmseMm, second.RegistrationRmseMm, 10);
        Assert.Equal(first.HeightDifference.MeanDistance, second.HeightDifference.MeanDistance, 10);
    }

    [Fact]
    public void CloudCompare_Should_Apply_Registration_Rotation_To_Source_Normals()
    {
        const double angle = Math.PI / 60;
        const double tx = 0.1;
        const double ty = -0.08;
        const double tz = 0.05;
        PointCloudData source = CreateAsymmetricCloudWithNormals();
        PointCloudData target = CreateTransformedCoordinates(source.PointCloud!, angle, tx, ty, tz);

        using WorkflowContext context = new();
        context.Set("source_cloud", source);
        context.Set("target_cloud", target);
        using var op = new cloud_compare(
            useCoarseRegistration: false,
            registrationMethod: "icp",
            maxIterations: 20,
            maxCorrespondenceDistance: 0.9,
            distanceThreshold: 0.01,
            imageResolution: 32,
            voxelSize: 0.001,
            maxDefectRatio: 0,
            maxMeanDistance: 0.01,
            maxMissingRatio: 0,
            minFineCorrespondenceRatio: 0.9,
            maxRegistrationRmse: 0.01
        );

        op.Execute(context);

        PointCloudData aligned = Assert.IsType<PointCloudData>(
            context.Get<PointCloudData>("aligned_cloud")
        );
        Mat alignedPoints = Assert.IsType<Mat>(aligned.PointCloud);
        Mat targetPoints = Assert.IsType<Mat>(target.PointCloud);
        Assert.Equal(6, alignedPoints.Cols);
        for (int col = 0; col < 3; col++)
            Assert.InRange(
                Math.Abs(alignedPoints.Get<float>(0, col) - targetPoints.Get<float>(0, col)),
                0,
                0.01
            );

        double invLength = 1 / Math.Sqrt(14);
        double expectedNx = Math.Cos(angle) * invLength - Math.Sin(angle) * 2 * invLength;
        double expectedNy = Math.Sin(angle) * invLength + Math.Cos(angle) * 2 * invLength;
        double expectedNz = 3 * invLength;
        Assert.InRange(Math.Abs(alignedPoints.Get<float>(0, 3) - expectedNx), 0, 0.01);
        Assert.InRange(Math.Abs(alignedPoints.Get<float>(0, 4) - expectedNy), 0, 0.01);
        Assert.InRange(Math.Abs(alignedPoints.Get<float>(0, 5) - expectedNz), 0, 0.01);
    }

    [Fact]
    public void CloudCompare_Should_Report_Forward_Excess_Separately_From_Reverse_Missing()
    {
        PointCloudData target = CreateAsymmetricCloudWithNormals(includeNormals: false);
        PointCloudData source = CreateAsymmetricCloudWithNormals(includeNormals: false, addOutlier: true);

        using WorkflowContext context = new();
        context.Set("source_cloud", source);
        context.Set("target_cloud", target);
        using var op = new cloud_compare(
            useCoarseRegistration: false,
            registrationMethod: "icp",
            maxIterations: 10,
            maxCorrespondenceDistance: 0.2,
            distanceThreshold: 0.01,
            imageResolution: 32,
            voxelSize: 0.001,
            maxDefectRatio: 1,
            maxMeanDistance: 1,
            maxMissingRatio: 1,
            minFineCorrespondenceRatio: 0.5,
            maxRegistrationRmse: 0.01
        );

        op.Execute(context);

        InspectionResult<cloud_compare.CloudCompareResult> inspection = Assert.IsType<
            InspectionResult<cloud_compare.CloudCompareResult>
        >(context.Get("result"));
        cloud_compare.CloudCompareResult details = Assert.IsType<cloud_compare.CloudCompareResult>(
            inspection.details
        );
        Assert.Equal(1, details.UnmatchedSourcePointCount);
        Assert.Equal(0, details.UnmatchedTargetPointCount);
        Assert.Equal(1, details.HeightDifference.DefectCount);
        Assert.Equal(1d / 21, details.HeightDifference.DefectRatio, 6);
        Assert.Equal(0, details.MissingRatio);
    }

    [Fact]
    public void CloudCompare_Should_Emit_Stable_Fine_Registration_Failure_Code()
    {
        using WorkflowContext context = new();
        context.Set("source_cloud", CreateOffsetCloud(0));
        context.Set("target_cloud", CreateOffsetCloud(100));
        using var op = new cloud_compare(
            useCoarseRegistration: false,
            registrationMethod: "icp",
            maxCorrespondenceDistance: 0.1,
            distanceThreshold: 0.01,
            imageResolution: 16,
            voxelSize: 0.001,
            maxDefectRatio: 1,
            maxMeanDistance: 1,
            maxMissingRatio: 1
        );

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            op.Execute(context)
        );
        Assert.StartsWith("[CLOUD_COMPARE_FINE_REGISTRATION_FAILED]", error.Message);
    }

    [Fact]
    public void ReadProductModel_Should_Detach_PointCloud_From_Nested_Context()
    {
        string path = Path.Combine(Path.GetTempPath(), $"aurora-model-{Guid.NewGuid():N}.ply");
        File.WriteAllText(
            path,
            """
            ply
            format ascii 1.0
            element vertex 2
            property float x
            property float y
            property float z
            property uchar red
            property uchar green
            property uchar blue
            end_header
            1 2 3 255 0 0
            4 5 6 0 255 0
            """
        );

        try
        {
            Guid modelId = Guid.NewGuid();
            using IDisposable ambient = ProductModelStoreAmbient.Push(new FileModelStore(path));
            using var op = new read_product_model(modelId.ToString("D"));
            using var context = new WorkflowContext();

            op.Execute(context);

            PointCloudData output = Assert.IsType<PointCloudData>(
                context.Get<PointCloudData>("model_point_cloud")
            );
            Assert.NotNull(output.PointCloud);
            Assert.Equal(2, output.PointCount);
            Assert.True(output.HasColors);
            Assert.Equal(1f, output.PointCloud!.Get<float>(0, 0));
            Assert.Equal((byte)255, output.Colors!.Get<byte>(0, 2));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class FileModelStore(string path) : IProductModelPointCloudStore
    {
        public Task<string> MaterializeAsync(Guid modelId) => Task.FromResult(path);
    }

    private static PointCloudData CreateDenseCloud(int width, int height, bool withColors)
    {
        int count = width * height;
        Mat points = new(count, 3, MatType.CV_32FC1);
        Mat? colors = withColors ? new Mat(count, 3, MatType.CV_8UC1) : null;
        int row = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++, row++)
        {
            points.Set(row, 0, (float)x);
            points.Set(row, 1, (float)y);
            points.Set(row, 2, (float)(0.03 * (x % 7) + 0.02 * (y % 5)));
            if (colors is not null)
            {
                colors.Set(row, 0, (byte)(row % 256));
                colors.Set(row, 1, (byte)((row * 3) % 256));
                colors.Set(row, 2, (byte)((row * 7) % 256));
            }
        }

        PointCloudData result = new() { Value = points };
        if (colors is not null)
            result.SetColors(colors);
        return result;
    }

    private static PointCloudData CreateAsymmetricCloudWithNormals(
        bool includeNormals = true,
        bool addOutlier = false
    )
    {
        int count = 20 + (addOutlier ? 1 : 0);
        int columns = includeNormals ? 6 : 3;
        Mat points = new(count, columns, MatType.CV_32FC1);
        double invLength = 1 / Math.Sqrt(14);
        int row = 0;
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 5; x++, row++)
        {
            float px = x * 2;
            float py = y * 2;
            float pz = (float)(0.15 * x * y + 0.07 * x * x + ((x + y) % 2) * 0.1);
            points.Set(row, 0, px);
            points.Set(row, 1, py);
            points.Set(row, 2, pz);
            if (includeNormals)
            {
                points.Set(row, 3, (float)invLength);
                points.Set(row, 4, (float)(2 * invLength));
                points.Set(row, 5, (float)(3 * invLength));
            }
        }

        if (addOutlier)
        {
            points.Set(row, 0, 50f);
            points.Set(row, 1, 50f);
            points.Set(row, 2, 50f);
        }
        return new PointCloudData { Value = points };
    }

    private static PointCloudData CreateTransformedCoordinates(
        Mat source,
        double angle,
        double tx,
        double ty,
        double tz
    )
    {
        Mat target = new(source.Rows, 3, MatType.CV_32FC1);
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);
        for (int row = 0; row < source.Rows; row++)
        {
            double x = source.Get<float>(row, 0);
            double y = source.Get<float>(row, 1);
            double z = source.Get<float>(row, 2);
            target.Set(row, 0, (float)(cos * x - sin * y + tx));
            target.Set(row, 1, (float)(sin * x + cos * y + ty));
            target.Set(row, 2, (float)(z + tz));
        }
        return new PointCloudData { Value = target };
    }

    private static PointCloudData CreateOffsetCloud(float offset)
    {
        Mat points = new(8, 3, MatType.CV_32FC1);
        for (int row = 0; row < 8; row++)
        {
            points.Set(row, 0, offset + row % 2);
            points.Set(row, 1, offset + (row / 2) % 2);
            points.Set(row, 2, offset + row / 4);
        }
        return new PointCloudData { Value = points };
    }

    private static WorkflowContext CreateKnownTransformContext(
        double angle,
        double tx,
        double ty,
        double tz
    )
    {
        PointCloudData source = CreateCoarseValidationCloud();
        PointCloudData target = CreateTransformedCoordinates(source.PointCloud!, angle, tx, ty, tz);
        WorkflowContext context = new();
        context.Set("source_cloud", source);
        context.Set("target_cloud", target);
        return context;
    }

    private static PointCloudData CreateCoarseValidationCloud()
    {
        const int width = 30;
        const int height = 20;
        Mat points = new(width * height, 3, MatType.CV_32FC1);
        int row = 0;
        for (int v = 0; v < height; v++)
        for (int u = 0; u < width; u++, row++)
        {
            double x = u * 0.12 + 0.018 * Math.Sin(v * 0.71);
            double y = v * 0.12 + 0.014 * Math.Cos(u * 0.53);
            double z =
                0.11 * Math.Sin(u * 0.37)
                + 0.07 * Math.Cos(v * 0.43)
                + 0.0025 * u * v
                + ((u + 2 * v) % 7 == 0 ? 0.025 : 0);
            points.Set(row, 0, (float)x);
            points.Set(row, 1, (float)y);
            points.Set(row, 2, (float)z);
        }
        return new PointCloudData { Value = points };
    }
}
