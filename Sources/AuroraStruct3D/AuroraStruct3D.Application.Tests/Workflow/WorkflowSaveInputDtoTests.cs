using System.ComponentModel.DataAnnotations;
using AuroraStruct3D.Workflow.Dtos;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class WorkflowSaveInputDtoTests
{
    [Fact]
    public void CreateWorkflowInput_Should_Fail_When_Required_Fields_Are_Missing()
    {
        CreateWorkflowInput input = new();

        List<ValidationResult> results = Validate(input);

        Assert.NotEmpty(results);
        Assert.Contains(results, x => x.MemberNames.Contains(nameof(CreateWorkflowInput.Name)));
    }

    [Fact]
    public void UpdateWorkflowInput_Should_Fail_When_Name_Is_Empty()
    {
        UpdateWorkflowInput input = new()
        {
            ProjectId = Guid.NewGuid(),
            Name = string.Empty,
            GraphData = new WorkflowGraphDto(),
        };

        List<ValidationResult> results = Validate(input);

        Assert.Contains(results, x => x.MemberNames.Contains(nameof(UpdateWorkflowInput.Name)));
    }

    [Fact]
    public void CreateWorkflowInput_Should_Pass_When_Required_Fields_Are_Present()
    {
        CreateWorkflowInput input = new()
        {
            ProjectId = Guid.NewGuid(),
            Name = "demo",
            GraphData = new WorkflowGraphDto
            {
                Nodes =
                [
                    new WorkflowNodeDto { Id = "n1", Type = "start-node" },
                    new WorkflowNodeDto { Id = "n2", Type = "end-node" },
                ],
            },
        };

        List<ValidationResult> results = Validate(input);

        Assert.Empty(results);
    }

    private static List<ValidationResult> Validate(object model)
    {
        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }
}
