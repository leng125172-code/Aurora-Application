using System.Reflection;
using AuroraStruct3D.Workflow;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class WorkflowApiRouteContractTests
{
    [Theory]
    [InlineData(
        nameof(IWorkflowAppService.GetListByProjectIdAsync),
        typeof(HttpGetAttribute),
        "projects/{projectId}/workflows"
    )]
    [InlineData(nameof(IWorkflowAppService.CreateAsync), typeof(HttpPostAttribute), null)]
    [InlineData(nameof(IWorkflowAppService.GetAsync), typeof(HttpGetAttribute), "{id}")]
    [InlineData(nameof(IWorkflowAppService.UpdateAsync), typeof(HttpPutAttribute), "{id}")]
    [InlineData(nameof(IWorkflowAppService.DeleteAsync), typeof(HttpDeleteAttribute), "{id}")]
    [InlineData(
        nameof(IWorkflowAppService.ValidateAsync),
        typeof(HttpPostAttribute),
        "{id}/validate"
    )]
    [InlineData(
        nameof(IWorkflowAppService.SimulateAsync),
        typeof(HttpPostAttribute),
        "{id}/simulate"
    )]
    public void Interface_Method_Should_Define_Explicit_Http_Contract(
        string methodName,
        Type httpAttributeType,
        string? template
    )
    {
        MethodInfo method = typeof(IWorkflowAppService).GetMethod(methodName)!;

        Assert.NotNull(method);
        Attribute? attribute = method
            .GetCustomAttributes(httpAttributeType, inherit: false)
            .Cast<Attribute>()
            .SingleOrDefault();
        Assert.NotNull(attribute);

        string? actualTemplate = GetTemplate(attribute!);
        Assert.Equal(template, actualTemplate);
    }

    [Theory]
    [InlineData(
        nameof(WorkflowAppService.GetListByProjectIdAsync),
        typeof(HttpGetAttribute),
        "projects/{projectId}/workflows"
    )]
    [InlineData(nameof(WorkflowAppService.CreateAsync), typeof(HttpPostAttribute), null)]
    [InlineData(nameof(WorkflowAppService.GetAsync), typeof(HttpGetAttribute), "{id}")]
    [InlineData(nameof(WorkflowAppService.UpdateAsync), typeof(HttpPutAttribute), "{id}")]
    [InlineData(nameof(WorkflowAppService.DeleteAsync), typeof(HttpDeleteAttribute), "{id}")]
    [InlineData(
        nameof(WorkflowAppService.ValidateAsync),
        typeof(HttpPostAttribute),
        "{id}/validate"
    )]
    [InlineData(
        nameof(WorkflowAppService.SimulateAsync),
        typeof(HttpPostAttribute),
        "{id}/simulate"
    )]
    public void Implementation_Method_Should_Define_Explicit_Http_Contract(
        string methodName,
        Type httpAttributeType,
        string? template
    )
    {
        MethodInfo method = typeof(WorkflowAppService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(x => x.Name == methodName);

        Attribute? attribute = method
            .GetCustomAttributes(httpAttributeType, inherit: false)
            .Cast<Attribute>()
            .SingleOrDefault();
        Assert.NotNull(attribute);

        string? actualTemplate = GetTemplate(attribute!);
        Assert.Equal(template, actualTemplate);
    }

    [Fact]
    public void ListByProject_Should_Use_FromRoute_On_ProjectId()
    {
        MethodInfo interfaceMethod = typeof(IWorkflowAppService).GetMethod(
            nameof(IWorkflowAppService.GetListByProjectIdAsync)
        )!;
        MethodInfo implementationMethod = typeof(WorkflowAppService).GetMethod(
            nameof(WorkflowAppService.GetListByProjectIdAsync)
        )!;

        Assert.Single(interfaceMethod.GetParameters());
        Assert.Single(implementationMethod.GetParameters());

        Assert.NotNull(interfaceMethod.GetParameters()[0].GetCustomAttribute<FromRouteAttribute>());
        Assert.NotNull(
            implementationMethod.GetParameters()[0].GetCustomAttribute<FromRouteAttribute>()
        );
    }

    private static string? GetTemplate(Attribute attribute)
    {
        return attribute switch
        {
            HttpGetAttribute x => x.Template,
            HttpPostAttribute x => x.Template,
            HttpPutAttribute x => x.Template,
            HttpDeleteAttribute x => x.Template,
            _ => null,
        };
    }
}
