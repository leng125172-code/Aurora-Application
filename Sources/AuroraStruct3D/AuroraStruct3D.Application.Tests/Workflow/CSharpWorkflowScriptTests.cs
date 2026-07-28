using System.Text.Json;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.OpenCV.Workflow.Scripting;
using Xunit;

namespace AuroraStruct3D.Workflow;

public class CSharpWorkflowScriptTests
{
    [Fact]
    public void Graph_should_round_trip_through_restricted_csharp_script()
    {
        using JsonDocument parameter = JsonDocument.Parse("""{"threshold":1.5}""");
        GraphDataModel graph = new()
        {
            Nodes =
            [
                new NodeModel
                {
                    Id = "inspect",
                    Type = "11111111-1111-1111-1111-111111111111",
                    X = 10,
                    Y = 20,
                    Text = new NodeTextModel { Value = "检测" },
                    Properties = new NodePropertiesModel
                    {
                        InputBindings = new() { ["input"] = "cloud" },
                        InputBindingSources = new() { ["input"] = "variable" },
                        OutputBindings = new() { ["result"] = "retstatus" },
                        OutputBindingSources = new() { ["result"] = "variable" },
                        Params = new() { ["options"] = parameter.RootElement.Clone() },
                        ParamSources = new() { ["options"] = "literal" },
                    },
                },
            ],
            Edges =
            [
                new EdgeModel
                {
                    Id = "edge-1",
                    SourceNodeId = "start",
                    TargetNodeId = "inspect",
                    SourceAnchorIndex = 1,
                    TargetAnchorIndex = 3,
                },
            ],
        };

        string source = CSharpWorkflowScript.Generate("检测流程", graph);
        (string name, GraphDataModel restored) = CSharpWorkflowScript.Parse(source);

        Assert.Equal("检测流程", name);
        Assert.Equal("retstatus", restored.Nodes[0].Properties!.OutputBindings!["result"]);
        Assert.Equal(
            1.5,
            restored.Nodes[0].Properties!.Params!["options"].GetProperty("threshold").GetDouble()
        );
        Assert.Equal("inspect", restored.Edges[0].TargetNodeId);
    }

    [Fact]
    public void Unknown_statement_should_be_rejected()
    {
        string source =
            """
            Workflow("x", 1);
            GraphBegin(null);
            System.IO.File.Delete("x");
            GraphEnd();
            """;

        FormatException error = Assert.ThrowsAny<FormatException>(() =>
            CSharpWorkflowScript.Parse(source)
        );
        Assert.Contains("不允许的语句", error.Message);
    }
}
