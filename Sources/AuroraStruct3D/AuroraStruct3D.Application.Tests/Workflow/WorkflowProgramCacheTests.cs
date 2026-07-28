using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.OpenCV.Workflow.Scripting;
using AuroraStruct3D.Workflow.Runtime;
using System.Text.Json;
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
                    Id = "end",
                    Type = "end-node",
                    Properties = new NodePropertiesModel
                    {
                        InputBindings = new() { ["result"] = "result" },
                        InputBindingSources = new() { ["result"] = "variable" },
                    },
                },
                new NodeModel
                {
                    Id = "assign",
                    Type = "builtin::assign",
                    Properties = new NodePropertiesModel
                    {
                        Params = new()
                        {
                            ["variableName"] = JsonSerializer.SerializeToElement("result"),
                            ["value"] = JsonSerializer.SerializeToElement(1),
                        },
                        ParamSources = new() { ["value"] = "literal" },
                    },
                },
            ],
            Edges =
            [
                new EdgeModel
                {
                    Id = "edge-1",
                    SourceNodeId = "start",
                    TargetNodeId = "assign",
                },
                new EdgeModel
                {
                    Id = "edge-2",
                    SourceNodeId = "assign",
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
        ) => Task.FromResult<OperatorParametersDescriptor?>(null);

        public Task<Type?> GetOperatorTypeAsync(
            Guid operatorId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<Type?>(null);
    }
}
