using System.Text.Json;
using System.Text.Json.Nodes;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.OpenCV.Workflow.Statements;
using AuroraStruct3D.OpenCV.Workflow.Scripting;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.OpenCV.Workflow.Values;
using Xunit;

namespace AuroraStruct3D.Workflow;

public class WorkflowValueAccessorTests
{
    [Fact]
    public void Typed_result_serializes_as_object_and_remains_member_addressable()
    {
        InspectionResult<HeightDiffInspectionDetails> result = InspectionResults.CreateTyped(
            true, true, new HeightDiffInspectionDetails(2, 1, 1, 1, 0, 2, true));

        string valueType = WorkflowValueSerializer.InferValueType(result);
        Assert.Equal(WorkflowValueTypes.Object, valueType);
        byte[] payload = WorkflowValueSerializer.Serialize(result, valueType);
        object restored = Assert.IsAssignableFrom<object>(
            WorkflowValueSerializer.Deserialize(payload, valueType));
        using WorkflowContext context = new();
        context.Set("inspection_result", restored);
        Assert.Equal("OK", ((JsonNode)new VariableRefBinding(
            "inspection_result.resultCode").Resolve(context)!).GetValue<string>());
        Assert.Equal(1d, ((JsonNode)new VariableRefBinding(
            "inspection_result.details.signedDiff").Resolve(context)!).GetValue<double>());
    }

    [Fact]
    public void Registered_nested_generic_and_read_only_list_are_supported()
    {
        Type? resolved = WorkflowTypeRegistry.Resolve(
            "IReadOnlyList<InspectionResult<HeightDiffInspectionDetails>>");
        Assert.Equal(typeof(IReadOnlyList<InspectionResult<HeightDiffInspectionDetails>>), resolved);
        using WorkflowContext context = new();
        context.Set("values", new ReadOnlyValues<string>(["first", "second"]));
        Assert.Equal("second", new VariableRefBinding("values[1]").Resolve(context));
    }

    [Fact]
    public void Product_result_preserves_nested_branch_details_when_serialized()
    {
        InspectionResult<HeightDiffInspectionDetails> branch = InspectionResults.CreateTyped(
            true, true, new HeightDiffInspectionDetails(3, 1, 2, 2, 0, 3, true));
        ProductInspectionResult product = new()
        {
            isValid = true, isOk = true, resultCode = InspectionResultCode.OK,
            comprehensive = branch.ToNode(), installation = branch.ToNode(),
        };
        string valueType = WorkflowValueSerializer.InferValueType(product);
        JsonNode restored = Assert.IsAssignableFrom<JsonNode>(WorkflowValueSerializer.Deserialize(
            WorkflowValueSerializer.Serialize(product, valueType), valueType));
        using WorkflowContext context = new();
        context.Set("inspection_result", restored);
        Assert.Equal(2d, ((JsonNode)new VariableRefBinding(
            "inspection_result.comprehensive.details.signedDiff").Resolve(context)!).GetValue<double>());
    }

    [Fact]
    public void Typed_result_uses_schema_camel_case_member_names_at_runtime()
    {
        using WorkflowContext context = new();
        context.Set("inspection_result", InspectionResults.CreateTyped(true, true,
            new InstallationPlaneInspectionDetails("OK", true, true, 1, 2, 3, 0, 0,
                1, 2, 3, 0.01, 0.02, 10, 11, ["reason"],
                new InstallationPlaneToleranceDetails(-1, 1, -1, 1, 2, 0.1, 3))));

        Assert.Equal(1d, new VariableRefBinding(
            "inspection_result.details.tiltX").Resolve(context));
        Assert.Equal("reason", new VariableRefBinding(
            "inspection_result.details.qualityReasons[0]").Resolve(context));
    }

    [Fact]
    public void Variable_binding_resolves_nested_json_members_arrays_and_length()
    {
        using WorkflowContext context = new();
        context.Set("inspection_result", JsonSerializer.SerializeToElement(new
        {
            resultCode = "OK",
            artifacts = new[] { new { name = "height-map" } },
        }));

        Assert.Equal("OK", ((JsonElement)new VariableRefBinding(
            "inspection_result.resultCode").Resolve(context)!).GetString());
        Assert.Equal("height-map", ((JsonElement)new VariableRefBinding(
            "inspection_result.artifacts[0].name").Resolve(context)!).GetString());
        Assert.Equal(1, new VariableRefBinding(
            "inspection_result.artifacts.Length").Resolve(context));
    }

    [Fact]
    public void V3_script_preserves_member_expression_and_derives_root_edge()
    {
        string source = CSharpWorkflowScript.Generate("member-path", new GraphDataModel
        {
            Nodes =
            [
                new() { Id = "start", Type = "start-node" },
                new()
                {
                    Id = "read", Type = "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
                    Properties = new NodePropertiesModel
                    {
                        InputBindings = new() { ["point_cloud_path"] = "input.ply" },
                        InputBindingSources = new() { ["point_cloud_path"] = "literal" },
                        OutputBindings = new() { ["output_point_cloud"] = "inspection_result" },
                        OutputBindingSources = new() { ["output_point_cloud"] = "variable" },
                    },
                },
                new()
                {
                    Id = "read2", Type = "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
                    Properties = new NodePropertiesModel
                    {
                        InputBindings = new() { ["point_cloud_path"] = "inspection_result.resultCode" },
                        InputBindingSources = new() { ["point_cloud_path"] = "variable" },
                        OutputBindings = new() { ["output_point_cloud"] = "next" },
                        OutputBindingSources = new() { ["output_point_cloud"] = "variable" },
                    },
                },
                new() { Id = "end", Type = "end-node", Properties = new NodePropertiesModel
                    { InputBindings = new() { ["result"] = "next" }, InputBindingSources = new() { ["result"] = "variable" } } },
            ],
        });
        Assert.Contains("inspection_result.resultCode", source);
        (_, GraphDataModel restored) = CSharpWorkflowScript.Parse(source);
        Assert.Equal("inspection_result.resultCode", restored.Nodes.Single(x => x.Id == "read2")
            .Properties!.InputBindings!["point_cloud_path"]);
        Assert.Contains(restored.Edges, x => x.SourceNodeId == "read" && x.TargetNodeId == "read2");
    }

    private sealed class ReadOnlyValues<T>(IReadOnlyList<T> values) : IReadOnlyList<T>
    {
        public T this[int index] => values[index];
        public int Count => values.Count;
        public IEnumerator<T> GetEnumerator() => values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
