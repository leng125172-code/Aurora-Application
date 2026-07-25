using System.Text.Json;
using AuroraStruct3D.OpenCV.PointCloudProjection;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.Workflow;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class FittedPlaneMeshOperatorTests
{
    [Fact]
    public void Contract_Should_Expose_Complete_Mesh_Data()
    {
        Assert.Collection(
            fitted_plane_mesh.InputVisionParameters!,
            input => Assert.Equal("plane_params", input.ParameterName),
            input => Assert.Equal("inlier_points", input.ParameterName)
        );
        Assert.Contains(
            fitted_plane_mesh.OutputVisionParameters!,
            output => output.ParameterName == "mesh_vertices"
        );
        Assert.Contains(
            fitted_plane_mesh.OutputVisionParameters!,
            output => output.ParameterName == "mesh_indices"
        );
        Assert.Contains(
            fitted_plane_mesh.OutputVisionParameters!,
            output => output.ParameterName == "mesh_normals"
        );
        Assert.Contains(
            fitted_plane_mesh.OutputVisionParameters!,
            output => output.ParameterName == "boundary_json"
        );
        Assert.Contains(
            fitted_plane_mesh.OutputVisionParameters!,
            output => output.ParameterName == "mesh_info"
        );
    }

    [Fact]
    public void Execute_Should_Create_Coplanar_Convex_Mesh()
    {
        using Mat plane = new(4, 1, MatType.CV_64FC1);
        plane.Set(0, 0, 0d);
        plane.Set(1, 0, 0d);
        plane.Set(2, 0, 1d);
        plane.Set(3, 0, -2d);

        using Mat points = new(5, 3, MatType.CV_32FC1);
        SetPoint(points, 0, -1, -2, 1.9f);
        SetPoint(points, 1, 1, -2, 2.1f);
        SetPoint(points, 2, 1, 2, 1.95f);
        SetPoint(points, 3, -1, 2, 2.05f);
        SetPoint(points, 4, 0, 0, 2.2f);
        PointCloudData cloud = new() { Value = points };

        using WorkflowContext context = new();
        context.Set("plane_params", plane);
        context.Set("inlier_points", cloud);
        using var op = new fitted_plane_mesh();
        op.Execute(context);

        Mat vertices = Assert.IsType<Mat>(context.Get<Mat>("mesh_vertices"));
        Mat indices = Assert.IsType<Mat>(context.Get<Mat>("mesh_indices"));
        Mat normals = Assert.IsType<Mat>(context.Get<Mat>("mesh_normals"));
        Assert.Equal(4, vertices.Rows);
        Assert.Equal(2, indices.Rows);
        Assert.Equal(4, normals.Rows);

        for (int i = 0; i < vertices.Rows; i++)
        {
            Assert.InRange(vertices.Get<float>(i, 2), 1.99999f, 2.00001f);
            Assert.InRange(normals.Get<float>(i, 2), 0.99999f, 1.00001f);
        }

        string infoJson = Assert.IsType<string>(context.Get<string>("mesh_info"));
        using JsonDocument info = JsonDocument.Parse(infoJson);
        Assert.Equal(4, info.RootElement.GetProperty("vertexCount").GetInt32());
        Assert.Equal(2, info.RootElement.GetProperty("triangleCount").GetInt32());
        Assert.InRange(info.RootElement.GetProperty("area").GetDouble(), 7.99999, 8.00001);
    }

    private static void SetPoint(Mat points, int row, float x, float y, float z)
    {
        points.Set(row, 0, x);
        points.Set(row, 1, y);
        points.Set(row, 2, z);
    }
}
