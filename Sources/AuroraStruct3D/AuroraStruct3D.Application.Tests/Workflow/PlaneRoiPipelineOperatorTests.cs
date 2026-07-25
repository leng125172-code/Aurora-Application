using System.Text.Json;
using AuroraStruct3D.OpenCV.PointCloudPlaneOps;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.Workflow;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class PlaneRoiPipelineOperatorTests
{
    [Fact]
    public void Contract_Should_Separate_Plane_Selection_Roi_Definition_And_Full_Resolution_Extraction()
    {
        Assert.Equal(6, select_fitted_plane.InputVisionParameters!.Count);
        Assert.Contains(
            select_fitted_plane.ConfigParameters!,
            parameter => parameter.Name == "planeName" && parameter.ControlType == PortControlType.Select
        );
        Assert.Contains(
            define_plane_roi.ConfigParameters!,
            parameter => parameter.Name == "roiJson"
                && parameter.ControlType == PortControlType.PlaneRoiEditor
        );
        Assert.Equal(
            "input_point_cloud",
            extract_points_in_plane_roi.InputVisionParameters![0].ParameterName
        );
        Assert.Contains(
            extract_points_in_plane_roi.OutputVisionParameters!,
            output => output.ParameterName == "selected_points"
        );
    }

    [Fact]
    public void Pipeline_Should_Select_B_And_Extract_All_Original_Points_Inside_Its_Roi()
    {
        using Mat planeA = CreatePlane(0);
        using Mat planeB = CreatePlane(0);
        using Mat planeC = CreatePlane(1);
        PointCloudData pointsA = CreateCloud([[-2, -2, 0], [2, -2, 0], [0, 2, 0]]);
        PointCloudData pointsB = CreateCloud([[-2, -2, 0], [2, -2, 0], [2, 2, 0], [-2, 2, 0]]);
        PointCloudData pointsC = CreateCloud([[-2, -2, 1], [2, -2, 1], [0, 2, 1]]);

        using WorkflowContext selectContext = new();
        selectContext.Set("plane_a_params", planeA);
        selectContext.Set("plane_a_points", pointsA);
        selectContext.Set("plane_b_params", planeB);
        selectContext.Set("plane_b_points", pointsB);
        selectContext.Set("plane_c_params", planeC);
        selectContext.Set("plane_c_points", pointsC);
        using var selector = new select_fitted_plane("B");
        selector.Execute(selectContext);

        Assert.Equal("B", selectContext.Get<string>("selected_plane_name"));
        Mat selectedPlane = Assert.IsType<Mat>(
            selectContext.Get<Mat>("selected_plane_params")
        );
        PointCloudData selectedPlanePoints = Assert.IsType<PointCloudData>(
            selectContext.Get<PointCloudData>("selected_plane_points")
        );

        const string roiInput =
            """{"points":[{"u":-0.6,"v":-0.6},{"u":0.6,"v":-0.6},{"u":0.6,"v":0.6},{"u":-0.6,"v":0.6}]}""";
        using WorkflowContext roiContext = new();
        roiContext.Set("plane_params", selectedPlane);
        roiContext.Set("plane_points", selectedPlanePoints);
        using var roiOperator = new define_plane_roi(roiInput);
        roiOperator.Execute(roiContext);
        string roiJson = Assert.IsType<string>(roiContext.Get<string>("plane_roi_json"));
        using JsonDocument roiDocument = JsonDocument.Parse(roiJson);
        Assert.Equal(4, roiDocument.RootElement.GetProperty("points").GetArrayLength());

        PointCloudData original = CreateCloud(
            [[0, 0, 0], [0.2f, 0.2f, 0.005f], [1.5f, 0, 0], [0, 0, 0.1f]],
            withColors: true
        );
        using WorkflowContext extractContext = new();
        extractContext.Set("input_point_cloud", original);
        extractContext.Set("plane_params", selectedPlane);
        extractContext.Set("plane_roi_json", roiJson);
        using var extractor = new extract_points_in_plane_roi(0.01);
        extractor.Execute(extractContext);

        Assert.Equal(2, extractContext.Get<int>("selected_count"));
        PointCloudData selected = Assert.IsType<PointCloudData>(
            extractContext.Get<PointCloudData>("selected_points")
        );
        PointCloudData remaining = Assert.IsType<PointCloudData>(
            extractContext.Get<PointCloudData>("remaining_points")
        );
        Assert.Equal(2, selected.PointCloud!.Rows);
        Assert.Equal(2, remaining.PointCloud!.Rows);
        Assert.True(selected.HasColors);
        Assert.Equal(2, selected.Colors!.Rows);
    }

    private static Mat CreatePlane(double z)
    {
        Mat plane = new(4, 1, MatType.CV_64FC1);
        plane.Set(0, 0, 0d);
        plane.Set(1, 0, 0d);
        plane.Set(2, 0, 1d);
        plane.Set(3, 0, -z);
        return plane;
    }

    private static PointCloudData CreateCloud(float[][] values, bool withColors = false)
    {
        Mat points = new(values.Length, 3, MatType.CV_32FC1);
        for (int row = 0; row < values.Length; row++)
        {
            points.Set(row, 0, values[row][0]);
            points.Set(row, 1, values[row][1]);
            points.Set(row, 2, values[row][2]);
        }

        PointCloudData cloud = new() { Value = points };
        if (withColors)
        {
            Mat colors = new(values.Length, 3, MatType.CV_8UC1, Scalar.All(0));
            for (int row = 0; row < values.Length; row++)
            {
                colors.Set(row, 0, (byte)(10 + row));
                colors.Set(row, 1, (byte)(20 + row));
                colors.Set(row, 2, (byte)(30 + row));
            }
            cloud.SetColors(colors);
        }
        return cloud;
    }
}
