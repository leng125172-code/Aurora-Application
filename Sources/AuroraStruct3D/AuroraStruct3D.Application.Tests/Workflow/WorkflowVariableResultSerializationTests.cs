using System.Text.Json;
using AuroraStruct3D.Workflow.Dtos;
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

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(result));
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
    [InlineData("double", "3.14", JsonValueKind.Number)]
    [InlineData("bool", "true", JsonValueKind.True)]
    [InlineData("string", "plain text", JsonValueKind.String)]
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
}
