using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AuroraStruct3D.Workflow;
using AuroraStruct3D.Workflow.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class WorkflowTemplateTests
{
    [Fact]
    public void Template_Should_Keep_An_Independent_Graph_Snapshot_And_Usage_Count()
    {
        Guid sourceWorkflowId = Guid.NewGuid();
        WorkflowTemplate template = new(
            Guid.NewGuid(),
            "平面检测",
            "三维检测",
            "修改阈值后即可使用",
            "{\"nodes\":[],\"edges\":[]}",
            sourceWorkflowId
        );

        template.RecordUsage();
        template.Update(
            "平面检测 V2",
            "三维检测",
            null,
            "{\"nodes\":[{\"id\":\"start\",\"type\":\"start-node\"}],\"edges\":[]}",
            sourceWorkflowId
        );

        Assert.Equal("平面检测 V2", template.Name);
        Assert.Equal(1, template.UsageCount);
        Assert.Contains("start-node", template.GraphData);
        Assert.Null(template.Description);
    }

    [Fact]
    public void Template_Input_Should_Require_Workflow_And_Name()
    {
        CreateWorkflowTemplateInput input = new();
        List<ValidationResult> results = Validate(input);

        Assert.Contains(results, x => x.MemberNames.Contains(nameof(input.Name)));
    }

    [Fact]
    public void Instantiate_Input_Should_Require_Name()
    {
        InstantiateWorkflowTemplateInput input = new() { ProjectId = Guid.NewGuid() };
        List<ValidationResult> results = Validate(input);

        Assert.Contains(results, x => x.MemberNames.Contains(nameof(input.Name)));
    }

    [Fact]
    public void Template_App_Service_Should_Have_Explicit_Routes_And_Authorization()
    {
        Assert.NotEmpty(
            typeof(WorkflowTemplateAppService)
                .GetCustomAttributes(typeof(AuthorizeAttribute), true)
        );
        Assert.Equal(
            "api/app/workflow-template",
            typeof(WorkflowTemplateAppService).GetCustomAttributes(typeof(RouteAttribute), true)
                .Cast<RouteAttribute>().Single().Template
        );
        Assert.Equal(
            "{id:guid}/instantiate",
            typeof(WorkflowTemplateAppService).GetMethod(nameof(WorkflowTemplateAppService.InstantiateAsync))!
                .GetCustomAttributes(typeof(HttpPostAttribute), true)
                .Cast<HttpPostAttribute>().Single().Template
        );
    }

    [Fact]
    public void Template_Dto_Graph_Should_Be_A_Json_Object()
    {
        WorkflowTemplateDto dto = new()
        {
            GraphData = JsonSerializer.SerializeToElement(new { nodes = Array.Empty<object>(), edges = Array.Empty<object>() }),
        };

        Assert.Equal(JsonValueKind.Object, dto.GraphData.ValueKind);
    }

    [Fact]
    public void Template_List_Item_Should_Expose_Only_Id_Name_And_GraphData()
    {
        string[] properties = typeof(WorkflowTemplateListItemDto)
            .GetProperties()
            .Select(x => x.Name)
            .OrderBy(x => x)
            .ToArray();

        Assert.Equal(["GraphData", "Id", "Name"], properties);
        Assert.Equal(
            typeof(Task<List<WorkflowTemplateListItemDto>>),
            typeof(IWorkflowTemplateAppService)
                .GetMethod(nameof(IWorkflowTemplateAppService.GetListAsync))!
                .ReturnType
        );
    }

    private static List<ValidationResult> Validate(object model)
    {
        ValidationContext context = new(model);
        List<ValidationResult> results = [];
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }
}
