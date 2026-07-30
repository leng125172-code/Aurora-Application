using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.OpenCV.Workflow.Scripting;
using AuroraStruct3D.Workflow.Runtime;
using System.Text.Json;
using AuroraStruct3D.OpenCV.File.PointCloud;
using AuroraStruct3D.OpenCV.VisionParameters;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Workflow;

public class WorkflowProgramCacheTests
{
    [Fact]
    public async Task Concurrent_requests_should_share_the_same_compiled_program()
    {
        GraphDataModel graph = BoundaryGraph();
        string source = CSharpWorkflowScript.Generate("cache-test", graph);
        WorkflowProgramCache cache = new(new EmptyOperatorRegistry());

        WorkflowCompiledProgram[] programs = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => cache.GetOrAddAsync("HASH", source))
        );

        Assert.All(programs, program => Assert.Same(programs[0], program));
        Assert.Equal("结果点云", programs[0].OutputDisplayNames["result"]);

        NodeModel end = Assert.Single(graph.Nodes, x => x.Type == "end-node");
        end.Properties!.InputBindingDisplayNames = null;
        WorkflowCompiledProgram fallbackProgram = await cache.GetOrAddAsync(
            "HASH-WITHOUT-DISPLAY",
            CSharpWorkflowScript.Generate("cache-test-fallback", graph)
        );
        Assert.Equal("result", fallbackProgram.OutputDisplayNames["result"]);
    }

    [Fact]
    public void Output_accessor_should_parse_path_once_and_read_nested_array_value()
    {
        WorkflowOutputAccessor accessor = WorkflowOutputAccessor.Compile(
            "retstatus.regions[0].name"
        );

        bool found = accessor.TryGetValue(
            """{"regions":[{"name":"A"}]}""",
            out object? value
        );

        Assert.True(found);
        Assert.Equal("A", value!.ToString());
    }

    [Fact]
    public void Output_accessor_should_not_serialize_mat_pointer_properties()
    {
        WorkflowOutputAccessor accessor = WorkflowOutputAccessor.Compile("image.DataPointer");
        using Mat mat = new(1, 1, MatType.CV_8UC1, Scalar.Black);

        bool found = accessor.TryGetValue(mat, out object? value);

        Assert.False(found);
        Assert.Same(mat, value);
    }

    private static GraphDataModel BoundaryGraph() =>
        new()
        {
            Nodes =
            [
                new NodeModel
                {
                    Id = "start",
                    Type = "start-node",
                },
                new NodeModel
                {
                    Id = "read",
                    Type = "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
                    Properties = new NodePropertiesModel
                    {
                        InputBindings = new() { ["point_cloud_path"] = "test.ply" },
                        InputBindingSources = new() { ["point_cloud_path"] = "literal" },
                        OutputBindings = new() { ["output_point_cloud"] = "result" },
                        OutputBindingSources = new() { ["output_point_cloud"] = "variable" },
                    },
                },
                new NodeModel
                {
                    Id = "end",
                    Type = "end-node",
                    Properties = new NodePropertiesModel
                    {
                        InputBindings = new() { ["result"] = "result" },
                        InputBindingSources = new() { ["result"] = "variable" },
                        InputBindingDisplayNames = new() { ["result"] = "结果点云" },
                    },
                },
            ],
            Edges =
            [
                new EdgeModel
                {
                    Id = "edge-1",
                    SourceNodeId = "start",
                    TargetNodeId = "read",
                },
                new EdgeModel
                {
                    Id = "edge-2",
                    SourceNodeId = "read",
                    TargetNodeId = "end",
                },
            ],
        };

    private sealed class EmptyOperatorRegistry : IOperatorRegistry
    {
        public Task<IReadOnlyList<OperatorDescriptor>> GetAllOperatorsAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<OperatorDescriptor>>([]);

        public Task<OperatorParametersDescriptor?> GetParametersAsync(
            Guid operatorId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<OperatorParametersDescriptor?>(
            operatorId == Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890")
                ? new OperatorParametersDescriptor
                {
                    OperatorId = operatorId,
                    Inputs =
                    [
                        new ParameterDescriptor
                        {
                            ParameterName = "point_cloud_path",
                            DisplayName = "Path",
                            ParameterTypeName = typeof(string).FullName!,
                        },
                    ],
                    Outputs =
                    [
                        new ParameterDescriptor
                        {
                            ParameterName = "output_point_cloud",
                            DisplayName = "Cloud",
                            ParameterTypeName = typeof(PointCloudData).FullName!,
                        },
                    ],
                }
                : null);

        public Task<Type?> GetOperatorTypeAsync(
            Guid operatorId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<Type?>(
            operatorId == Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890")
                ? typeof(read_point_cloud)
                : null);
    }
}
