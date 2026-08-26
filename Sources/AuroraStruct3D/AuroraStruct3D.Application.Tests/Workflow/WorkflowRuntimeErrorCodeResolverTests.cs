using AuroraStruct3D.Workflow.Runtime;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class WorkflowRuntimeErrorCodeResolverTests
{
    [Theory]
    [InlineData("[SCAN_TIMEOUT] 扫描超时。", "SCAN_TIMEOUT")]
    [InlineData("工作流执行失败：[CLOUD_COMPARE_FINE_REGISTRATION_FAILED] 配准失败。", "CLOUD_COMPARE_FINE_REGISTRATION_FAILED")]
    [InlineData("节点失败：[POINT_CLOUD_UNIT_MISMATCH] 单位不一致。", "POINT_CLOUD_UNIT_MISMATCH")]
    public void Resolve_should_extract_bracketed_operator_code(string message, string expected)
    {
        Assert.Equal(expected, WorkflowRuntimeErrorCodeResolver.Resolve(message, "EXECUTION_FAULT"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[not_a_machine_code] 普通消息")]
    [InlineData("没有结构化代码")]
    public void Resolve_should_use_fallback_when_message_has_no_valid_code(string? message)
    {
        Assert.Equal(
            "EXECUTION_FAULT",
            WorkflowRuntimeErrorCodeResolver.Resolve(message, "EXECUTION_FAULT")
        );
    }
}
