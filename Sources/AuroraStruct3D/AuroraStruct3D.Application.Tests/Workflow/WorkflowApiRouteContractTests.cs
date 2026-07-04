using System.Reflection;
using AuroraStruct3D.Workflow;
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
    [InlineData(nameof(WorkflowAppService.GetListAsync))]
    [InlineData(nameof(WorkflowAppService.CreateAsync))]
    [InlineData(nameof(WorkflowAppService.GetAsync))]
    [InlineData(nameof(WorkflowAppService.UpdateAsync))]
    [InlineData(nameof(WorkflowAppService.DeleteAsync))]
    [InlineData(nameof(WorkflowAppService.ValidateAsync))]
    [InlineData(nameof(WorkflowAppService.SimulateAsync))]
    public void Implementation_Method_Should_Not_Define_Explicit_Http_Contract(string methodName)
    {
        MethodInfo method = typeof(WorkflowAppService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(x => x.Name == methodName);

        Assert.Null(GetHttpMethodAttribute(method));
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
        Assert.NotNull(typeof(WorkflowAppService).GetCustomAttribute<AuthorizeAttribute>());
        Assert.NotNull(
            typeof(WorkflowExecutionAppService).GetCustomAttribute<AuthorizeAttribute>()
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
