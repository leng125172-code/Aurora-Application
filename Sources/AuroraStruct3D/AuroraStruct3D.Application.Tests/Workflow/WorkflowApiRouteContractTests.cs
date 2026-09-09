using System.Reflection;
using AuroraStruct3D.Workflow;
using AuroraStruct3D.Workflow.Runtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class WorkflowApiRouteContractTests
{
    [Theory]
    [InlineData(nameof(IWorkflowAppService.GetListAsync))]
    [InlineData(nameof(IWorkflowAppService.CreateAsync))]
    [InlineData(nameof(IWorkflowAppService.GetAsync))]
    [InlineData(nameof(IWorkflowAppService.UpdateAsync))]
    [InlineData(nameof(IWorkflowAppService.DeleteAsync))]
    [InlineData(nameof(IWorkflowAppService.ValidateAsync))]
    [InlineData(nameof(IWorkflowAppService.SimulateAsync))]
    public void Interface_Method_Should_Not_Define_Explicit_Http_Contract(string methodName)
    {
        MethodInfo method = typeof(IWorkflowAppService).GetMethod(methodName)!;

        Assert.NotNull(method);
        Assert.Null(GetHttpMethodAttribute(method));
    }

    [Theory]
    [InlineData(nameof(WorkflowAppService.GetListAsync), typeof(HttpGetAttribute), null)]
    [InlineData(nameof(WorkflowAppService.CreateAsync), typeof(HttpPostAttribute), null)]
    [InlineData(nameof(WorkflowAppService.GetAsync), typeof(HttpGetAttribute), "{id:guid}")]
    [InlineData(nameof(WorkflowAppService.UpdateAsync), typeof(HttpPutAttribute), "{id:guid}")]
    [InlineData(nameof(WorkflowAppService.DeleteAsync), typeof(HttpDeleteAttribute), "{id:guid}")]
    [InlineData(
        nameof(WorkflowAppService.ValidateAsync),
        typeof(HttpPostAttribute),
        "{id:guid}/validate"
    )]
    [InlineData(
        nameof(WorkflowAppService.SimulateAsync),
        typeof(HttpPostAttribute),
        "{id:guid}/simulate"
    )]
    public void Implementation_Method_Should_Define_Unambiguous_Http_Contract(
        string methodName,
        Type attributeType,
        string? template
    )
    {
        MethodInfo method = typeof(WorkflowAppService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(x => x.Name == methodName);

        Attribute route = Assert.IsAssignableFrom<HttpMethodAttribute>(
            GetHttpMethodAttribute(method)
        );
        Assert.Equal(attributeType, route.GetType());
        Assert.Equal(template, ((HttpMethodAttribute)route).Template);
    }

    [Fact]
    public void Frozen_Runtime_Execution_Should_Not_Be_An_Http_Action()
    {
        MethodInfo method = typeof(WorkflowRuntimeAppService).GetMethod(
            nameof(WorkflowRuntimeAppService.ExecuteFrozenWorkflowAsync)
        )!;

        Assert.NotNull(method.GetCustomAttribute<NonActionAttribute>());
    }

    [Fact]
    public void ListByProject_Should_Not_Define_FromQuery_Or_FromRoute_On_ProjectId()
    {
        MethodInfo interfaceMethod = typeof(IWorkflowAppService).GetMethod(
            nameof(IWorkflowAppService.GetListAsync)
        )!;
        MethodInfo implementationMethod = typeof(WorkflowAppService).GetMethod(
            nameof(WorkflowAppService.GetListAsync)
        )!;

        Assert.Single(interfaceMethod.GetParameters());
        Assert.Single(implementationMethod.GetParameters());

        Assert.Null(interfaceMethod.GetParameters()[0].GetCustomAttribute<FromQueryAttribute>());
        Assert.Null(interfaceMethod.GetParameters()[0].GetCustomAttribute<FromRouteAttribute>());
        Assert.Null(
            implementationMethod.GetParameters()[0].GetCustomAttribute<FromQueryAttribute>()
        );
        Assert.Null(
            implementationMethod.GetParameters()[0].GetCustomAttribute<FromRouteAttribute>()
        );
    }

    [Fact]
    public void Workflow_App_Service_Should_Be_Authorized()
    {
        Assert.NotEmpty(typeof(WorkflowAppService).GetCustomAttributes<AuthorizeAttribute>());
        Assert.NotEmpty(
            typeof(WorkflowExecutionAppService).GetCustomAttributes<AuthorizeAttribute>()
        );
    }

    private static Attribute? GetHttpMethodAttribute(MemberInfo memberInfo)
    {
        return memberInfo
            .GetCustomAttributes(inherit: false)
            .OfType<HttpMethodAttribute>()
            .SingleOrDefault();
    }
}
