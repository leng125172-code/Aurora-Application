using System.Text.Json;
using System.Text.Json.Nodes;
using AuroraStruct3D.OpenCV.File.Images;
using AuroraStruct3D.OpenCV.File.PointCloud;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.OpenCV.Workflow.Statements;
using AuroraStruct3D.OpenCV.Workflow.Values;
using AuroraStruct3D.Workflow.Dtos;
using AuroraStruct3D.Workflow.Runtime;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Workflow;

public class WorkflowVariableResultSerializationTests
{
    [Fact]
    public void Json_string_output_should_be_written_as_native_json_value()
    {
        WorkflowVariableResultDto result = new()
        {
            Name = "inspection_result",
            ValueType = "string",
            ScalarValue = """{"regions":[{"name":"A","passed":true}]}""",
        };

        using JsonDocument json = JsonDocument.Parse(
            JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        );
        JsonElement root = json.RootElement;

        Assert.False(root.TryGetProperty("ScalarValue", out _));
        Assert.Equal(JsonValueKind.Object, root.GetProperty("value").ValueKind);
        Assert.Equal(
            JsonValueKind.Array,
            root.GetProperty("value").GetProperty("regions").ValueKind
        );
    }

    [Theory]
    [InlineData("int", "42", JsonValueKind.Number)]
    [InlineData("long", "9223372036854775806", JsonValueKind.Number)]
    [InlineData("float", "3.14", JsonValueKind.Number)]
    [InlineData("double", "3.14", JsonValueKind.Number)]
    [InlineData("decimal", "1234567890.123456789", JsonValueKind.Number)]
    [InlineData("bool", "true", JsonValueKind.True)]
    [InlineData("string", "plain text", JsonValueKind.String)]
    [InlineData("object", """{"passed":true}""", JsonValueKind.Object)]
    [InlineData("array", "[1,2,3]", JsonValueKind.Array)]
    public void Scalar_output_should_keep_its_native_json_type(
        string valueType,
        string scalarValue,
        JsonValueKind expectedKind
    )
    {
        WorkflowVariableResultDto result = new()
        {
            Name = "result",
            ValueType = valueType,
            ScalarValue = scalarValue,
        };

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(result));

        Assert.Equal(expectedKind, json.RootElement.GetProperty("value").ValueKind);
    }

    [Fact]
    public void Runtime_type_inference_should_preserve_public_numeric_tokens()
    {
        Assert.Equal(WorkflowValueTypes.Int, WorkflowValueSerializer.InferValueType(1));
        Assert.Equal(WorkflowValueTypes.Long, WorkflowValueSerializer.InferValueType(1L));
        Assert.Equal(WorkflowValueTypes.Float, WorkflowValueSerializer.InferValueType(1.25f));
        Assert.Equal(WorkflowValueTypes.Double, WorkflowValueSerializer.InferValueType(1.25d));
        Assert.Equal(WorkflowValueTypes.Decimal, WorkflowValueSerializer.InferValueType(1.25m));
        Assert.Equal(WorkflowValueTypes.Bool, WorkflowValueSerializer.InferValueType(true));
    }

    [Fact]
    public void Blob_key_should_remain_an_unchanged_string_value()
    {
        const string blobKey = "workflow-results/2026/07/result.PLY";
        WorkflowExecutionOutputResultDto result = new()
        {
            Name = "result_blob_key",
            DisplayName = "结果点云",
            ValueType = WorkflowValueTypes.String,
            Value = blobKey,
        };

        using JsonDocument json = JsonDocument.Parse(
            JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        );
        JsonElement root = json.RootElement;

        Assert.Equal(4, root.EnumerateObject().Count());
        Assert.Equal(blobKey, root.GetProperty("value").GetString());
        Assert.Equal(WorkflowValueTypes.String, root.GetProperty("valueType").GetString());
    }

    [Theory]
    [InlineData(
        "/api/app/operator-file/preview?blobName=workflow-image%2F20260729210625_132_height-diff-result.png"
    )]
    [InlineData(
        "http://localhost:5000/api/app/operator-file/download?blobName=workflow-image%2Fresult.png&download=true"
    )]
    public void Legacy_operator_file_url_should_be_normalized_to_blob_key(string value)
    {
        object? normalized = WorkflowRuntimeAppService.NormalizeExecutionResultValue(value);

        Assert.Equal(
            value.Contains("height-diff", StringComparison.Ordinal)
                ? "workflow-image/20260729210625_132_height-diff-result.png"
                : "workflow-image/result.png",
            normalized
        );
        Assert.Equal(
            WorkflowValueTypes.String,
            WorkflowValueSerializer.InferValueType(normalized)
        );
    }

    [Fact]
    public void Existing_blob_key_should_not_be_rewritten()
    {
        const string blobKey = "workflow-image/20260729210625_132_height-diff-result.png";

        object? normalized = WorkflowRuntimeAppService.NormalizeExecutionResultValue(blobKey);

        Assert.Equal(blobKey, normalized);
    }

    [Fact]
    public void Blob_should_be_a_distinct_public_value_type()
    {
        Assert.Equal("blob", WorkflowValueTypes.Blob);
        Assert.True(WorkflowValueTypes.IsScalar(WorkflowValueTypes.Blob));
    }

    [Fact]
    public void Blob_type_should_be_derived_from_operator_output_provenance()
    {
        List<IWorkflowStatement> statements =
        [
            new OperatorCallStatement(
                typeof(save_image_to_blob),
                outputBindings: new Dictionary<string, OutputBinding>
                {
                    ["blob_name"] = new("image_blob"),
                    ["download_url"] = new("legacy_image_url"),
                }
            ),
            new OperatorCallStatement(
                typeof(save_point_cloud_to_blob),
                outputBindings: new Dictionary<string, OutputBinding>
                {
                    ["blob_name"] = new("cloud_blob"),
                }
            ),
        ];
        HashSet<string> blobVariables = new(StringComparer.Ordinal);

        WorkflowRuntimeAppService.CollectBlobOutputVariables(statements, blobVariables);

        Assert.Equal(3, blobVariables.Count);
        Assert.Contains("image_blob", blobVariables);
        Assert.Contains("legacy_image_url", blobVariables);
        Assert.Contains("cloud_blob", blobVariables);
        Assert.DoesNotContain("ordinary_text", blobVariables);
    }

    [Fact]
    public void Raw_mat_result_should_serialize_as_null_instead_of_following_native_pointers()
    {
        using Mat mat = new(1, 1, MatType.CV_8UC1, Scalar.Black);
        WorkflowExecutionOutputResultDto result = new()
        {
            Name = "raw_image",
            DisplayName = "原始图像",
            ValueType = WorkflowValueTypes.Mat,
            Value = mat,
        };

        using JsonDocument json = JsonDocument.Parse(
            JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        );

        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("value").ValueKind);
        Assert.Equal(WorkflowValueTypes.Mat, json.RootElement.GetProperty("valueType").GetString());
    }

    [Fact]
    public void Mat_collection_snapshot_should_not_serialize_native_pointers()
    {
        using Mat mat = new(1, 1, MatType.CV_8UC1, Scalar.Black);
        Mat[] values = [mat];

        string? scalarValue = WorkflowValueSerializer.ScalarToString(values);

        Assert.Null(scalarValue);
        Assert.Equal(WorkflowValueTypes.Array, WorkflowValueSerializer.InferValueType(values));
    }

    [Fact]
    public void Runtime_json_boundary_should_reject_mat_and_nested_mat_values()
    {
        using Mat mat = new(1, 1, MatType.CV_8UC1, Scalar.Black);
        Dictionary<string, object?> nested = new() { ["preview"] = mat };

        Assert.False(WorkflowValueSerializer.TrySerializeJson(nested, out string? json));
        Assert.Null(json);
        Assert.False(
            WorkflowValueSerializer.TrySerializeToJsonNode(nested, out JsonNode? node)
        );
        Assert.Null(node);
        Assert.False(
            WorkflowValueSerializer.TryGetJsonUtf8ByteCount(nested, out long byteCount)
        );
        Assert.Equal(0, byteCount);
    }

    [Fact]
    public void Runtime_json_boundary_should_preserve_regular_composite_values()
    {
        Dictionary<string, object?> value = new()
        {
            ["passed"] = true,
            ["height"] = 1.25f,
        };

        Assert.True(
            WorkflowValueSerializer.TrySerializeToJsonNode(value, out JsonNode? node)
        );
        Assert.True(node!["passed"]!.GetValue<bool>());
        Assert.Equal(1.25f, node["height"]!.GetValue<float>());
        Assert.True(
            WorkflowValueSerializer.TryGetJsonUtf8ByteCount(value, out long byteCount)
        );
        Assert.True(byteCount > 0);
    }

    [Fact]
    public void Signalr_status_payload_should_not_include_mat_native_properties()
    {
        using Mat mat = new(1, 1, MatType.CV_8UC1, Scalar.Black);
        WorkflowExecutionOutputResultDto configuredResult = new()
        {
            Name = "raw_image",
            DisplayName = "原始图像",
            ValueType = WorkflowValueTypes.Mat,
            Value = mat,
        };
        WorkflowExecutionStatusDto status = new()
        {
            ExecutionId = Guid.NewGuid(),
            UpdatedAt = DateTime.UtcNow,
            DebugState = "completed",
            IsTerminal = true,
            Outputs =
            [
                new WorkflowVariableResultDto
                {
                    Name = "raw_image",
                    ValueType = WorkflowValueTypes.Mat,
                    ScalarValue = WorkflowValueSerializer.ScalarToString(mat),
                },
            ],
        };

        string signalrArgumentJson = JsonSerializer.Serialize(
            new object[] { status.ExecutionId, "session-completed", status, configuredResult },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );

        Assert.DoesNotContain("DataPointer", signalrArgumentJson, StringComparison.Ordinal);
        Assert.DoesNotContain("DataStart", signalrArgumentJson, StringComparison.Ordinal);
        using JsonDocument json = JsonDocument.Parse(signalrArgumentJson);
        Assert.Equal(
            JsonValueKind.Null,
            json.RootElement[3].GetProperty("value").ValueKind
        );
    }
}
