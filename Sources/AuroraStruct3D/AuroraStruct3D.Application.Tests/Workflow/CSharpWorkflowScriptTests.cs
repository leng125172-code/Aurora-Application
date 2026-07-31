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
        GraphDataModel graph = new()
        {
            Nodes =
            [
                new NodeModel
                {
                    Id = "start",
                    Type = "start-node",
                    X = 10,
                    Y = 20,
                    Text = new NodeTextModel { Value = "开始" },
                },
                new NodeModel
                {
                    Id = "reference",
                    Type = "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
                    X = 10,
                    Y = 120,
                    Text = new NodeTextModel { Value = "读取参考点云" },
                    Properties = new NodePropertiesModel
                    {
                        InputBindings = new() { ["point_cloud_path"] = "reference.ply" },
                        InputBindingSources = new() { ["point_cloud_path"] = "literal" },
                        OutputBindings = new() { ["output_point_cloud"] = "referenceCloud" },
                        OutputBindingSources = new() { ["output_point_cloud"] = "variable" },
                    },
                },
                new NodeModel
                {
                    Id = "target",
                    Type = "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
                    Properties = new NodePropertiesModel
                    {
                        InputBindings = new() { ["point_cloud_path"] = "target.ply" },
                        InputBindingSources = new() { ["point_cloud_path"] = "literal" },
                        OutputBindings = new() { ["output_point_cloud"] = "targetCloud" },
                        OutputBindingSources = new() { ["output_point_cloud"] = "variable" },
                    },
                },
                new NodeModel
                {
                    Id = "icp",
                    Type = "a1b2c3d4-0008-4000-8000-000000000029",
                    Properties = new NodePropertiesModel
                    {
                        InputBindings = new()
                        {
                            ["source_cloud"] = "targetCloud",
                            ["target_cloud"] = "referenceCloud",
                        },
                        InputBindingSources = new()
                        {
                            ["source_cloud"] = "variable",
                            ["target_cloud"] = "variable",
                        },
                        OutputBindings = new()
                        {
                            ["aligned_cloud"] = "alignedCloud",
                            ["transform_matrix"] = "transformMatrix",
                            ["stats_json"] = "registrationResult",
                        },
                        OutputBindingSources = new()
                        {
                            ["aligned_cloud"] = "variable",
                            ["transform_matrix"] = "variable",
                            ["stats_json"] = "variable",
                        },
                        Params = new()
                        {
                            ["maxIterations"] = JsonSerializer.SerializeToElement(50),
                            ["maxCorrespondenceDistance"] =
                                JsonSerializer.SerializeToElement(0.1),
                        },
                        ParamSources = new()
                        {
                            ["maxIterations"] = "literal",
                            ["maxCorrespondenceDistance"] = "literal",
                        },
                    },
                },
                new NodeModel
                {
                    Id = "end",
                    Type = "end-node",
                    Properties = new NodePropertiesModel
                    {
                        InputBindings = new()
                        {
                            ["result"] = "registrationResult",
                        },
                        InputBindingSources = new() { ["result"] = "variable" },
                        InputBindingDisplayNames = new()
                        {
                            ["result"] = "配准结果",
                        },
                    },
                },
            ],
        };

        string source = CSharpWorkflowScript.Generate("检测流程", graph);
        (string name, GraphDataModel restored) = CSharpWorkflowScript.Parse(source);

        Assert.Equal("检测流程", name);
        Assert.Contains("Workflow(\"检测流程\", 2);", source);
        Assert.Contains("\"title\":\"开始\"", source);
        Assert.DoesNotContain("\\u68C0\\u6D4B", source);
        Assert.Contains("var (alignedCloud, transformMatrix, registrationResult)", source);
        Assert.Contains("icp_registration(", source);
        Assert.Contains("\"displayName\":\"配准结果\"", source);
        NodeModel restoredIcp = Assert.Single(restored.Nodes, x => x.Id == "icp");
        Assert.Equal(
            "registrationResult",
            restoredIcp.Properties!.OutputBindings!["stats_json"]);
        Assert.Equal(50, restoredIcp.Properties.Params!["maxIterations"].GetInt32());
        Assert.Contains(
            restored.Edges,
            x => x.SourceNodeId == "target" && x.TargetNodeId == "icp");
        NodeModel restoredEnd = Assert.Single(restored.Nodes, x => x.Type == "end-node");
        Assert.Equal(
            "配准结果",
            restoredEnd.Properties!.InputBindingDisplayNames!["result"]);
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

    [Fact]
    public void Missing_input_should_generate_null_and_legacy_null_identifier_should_parse()
    {
        GraphDataModel graph = new()
        {
            Nodes =
            [
                new NodeModel { Id = "start", Type = "start-node" },
                new NodeModel
                {
                    Id = "read",
                    Type = "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
                    Properties = new NodePropertiesModel
                    {
                        OutputBindings = new()
                        {
                            ["output_point_cloud"] = "cloud",
                        },
                        OutputBindingSources = new()
                        {
                            ["output_point_cloud"] = "variable",
                        },
                    },
                },
                new NodeModel
                {
                    Id = "end",
                    Type = "end-node",
                    Properties = new NodePropertiesModel
                    {
                        InputBindings = new() { ["cloud"] = "cloud" },
                        InputBindingSources = new() { ["cloud"] = "variable" },
                    },
                },
            ],
        };

        string source = CSharpWorkflowScript.Generate("null-input", graph);

        Assert.Contains("    null", source);
        Assert.DoesNotContain("_null", source);
        (_, GraphDataModel restored) = CSharpWorkflowScript.Parse(
            source.Replace("    null", "    _null", StringComparison.Ordinal));
        NodeModel read = Assert.Single(restored.Nodes, x => x.Id == "read");
        Assert.Equal("null", read.Properties!.InputBindings!["point_cloud_path"]);
        Assert.Equal("literal", read.Properties.InputBindingSources!["point_cloud_path"]);
    }
}
