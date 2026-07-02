using System.Text.Json;
using AuroraStruct3D.OpenCV.Workflow.Compilation;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class WorkflowSourceFlagContractValidatorTests
{
    [Fact]
    public void Validate_Should_Pass_When_All_Sections_Have_Explicit_Valid_Flags()
    {
        GraphDataModel graph = new()
        {
            Nodes =
            [
                new NodeModel
                {
                    Id = "n1",
                    Type = "operator",
                    Properties = new NodePropertiesModel
                    {
                        Params = new Dictionary<string, JsonElement>
                        {
                            ["cropMode"] = ParseJsonString("box"),
                            ["minX"] = ParseJsonString("-50"),
                        },
                        ParamSources = new Dictionary<string, string>
                        {
                            ["cropMode"] = "literal",
                            ["minX"] = "literal",
                        },
                        InputBindings = new Dictionary<string, string>
                        {
                            ["point_cloud_path"] =
                                "a1b2c3d4e5f67890abcdef1234567890/20260701174835_614_demo点云.ply",
                            ["input_point_cloud"] = "cloud",
                        },
                        InputBindingSources = new Dictionary<string, string>
                        {
                            ["point_cloud_path"] = "literal",
                            ["input_point_cloud"] = "variable",
                        },
                        OutputBindings = new Dictionary<string, string> { ["out"] = "filtered" },
                        OutputBindingSources = new Dictionary<string, string>
                        {
                            ["out"] = "variable",
                        },
                    },
                },
            ],
        };

        List<string> errors = WorkflowSourceFlagContractValidator.Validate(graph);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_Should_Fail_When_Source_Dictionary_Is_Missing()
    {
        GraphDataModel graph = new()
        {
            Nodes =
            [
                new NodeModel
                {
                    Id = "n1",
                    Type = "operator",
                    Properties = new NodePropertiesModel
                    {
                        Params = new Dictionary<string, JsonElement>
                        {
                            ["cropMode"] = ParseJsonString("box"),
                        },
                        ParamSources = null,
                    },
                },
            ],
        };

        List<string> errors = WorkflowSourceFlagContractValidator.Validate(graph);

        Assert.Contains(
            errors,
            x => x.Contains("missing required source map", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Validate_Should_Fail_When_Key_Set_Does_Not_Match()
    {
        GraphDataModel graph = new()
        {
            Nodes =
            [
                new NodeModel
                {
                    Id = "n1",
                    Type = "operator",
                    Properties = new NodePropertiesModel
                    {
                        InputBindings = new Dictionary<string, string>
                        {
                            ["point_cloud_path"] = "path.ply",
                            ["input_point_cloud"] = "cloud",
                        },
                        InputBindingSources = new Dictionary<string, string>
                        {
                            ["input_point_cloud"] = "variable",
                        },
                    },
                },
            ],
        };

        List<string> errors = WorkflowSourceFlagContractValidator.Validate(graph);

        Assert.Contains(errors, x => x.Contains("missing keys", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Should_Fail_When_Flag_Value_Is_Invalid()
    {
        GraphDataModel graph = new()
        {
            Nodes =
            [
                new NodeModel
                {
                    Id = "n1",
                    Type = "operator",
                    Properties = new NodePropertiesModel
                    {
                        InputBindings = new Dictionary<string, string>
                        {
                            ["input_point_cloud"] = "cloud",
                        },
                        InputBindingSources = new Dictionary<string, string>
                        {
                            ["input_point_cloud"] = "auto",
                        },
                    },
                },
            ],
        };

        List<string> errors = WorkflowSourceFlagContractValidator.Validate(graph);

        Assert.Contains(errors, x => x.Contains("invalid source", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Should_Fail_When_OutputBindingSource_Is_Literal()
    {
        GraphDataModel graph = new()
        {
            Nodes =
            [
                new NodeModel
                {
                    Id = "n1",
                    Type = "operator",
                    Properties = new NodePropertiesModel
                    {
                        OutputBindings = new Dictionary<string, string> { ["out"] = "cloud" },
                        OutputBindingSources = new Dictionary<string, string>
                        {
                            ["out"] = "literal",
                        },
                    },
                },
            ],
        };

        List<string> errors = WorkflowSourceFlagContractValidator.Validate(graph);

        Assert.Contains(
            errors,
            x => x.Contains("must use source 'variable'", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Param_Enum_And_Numeric_Should_Be_Literal_When_Flagged_Literal()
    {
        Dictionary<string, string> sourceMap = new()
        {
            ["cropMode"] = "literal",
            ["minX"] = "literal",
        };

        bool enumAsVariable = BindingSource.TryGetParamVariableNameStrict(
            sourceMap,
            "cropMode",
            ParseJsonString("box"),
            out _,
            out _
        );
        bool numericAsVariable = BindingSource.TryGetParamVariableNameStrict(
            sourceMap,
            "minX",
            ParseJsonString("-50"),
            out _,
            out _
        );

        Assert.False(enumAsVariable);
        Assert.False(numericAsVariable);
    }

    private static JsonElement ParseJsonString(string value)
    {
        using JsonDocument document = JsonDocument.Parse($"\"{value}\"");
        return document.RootElement.Clone();
    }
}
