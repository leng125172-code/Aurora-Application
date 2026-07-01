using AuroraStruct3D.Workflow.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 工作流执行应用服务接口。
/// ABP 自动生成 REST 端点：<c>POST /api/app/workflow-execution/run</c>。
/// 加载工作流定义 → 编译 → 注入 Redis 暂存初始变量 → 执行 → 写回指定结果变量 → 返回摘要。
/// </summary>
public interface IWorkflowExecutionAppService : IApplicationService
{
    /// <summary>
    /// 执行一个已保存的工作流。
    /// </summary>
    /// <param name="input">执行参数（项目 / 工作流 ID + 变量进出约定）。</param>
    Task<WorkflowRunResultDto> RunAsync(RunWorkflowInput input);

    /// <summary>
    /// 模拟执行：返回示例点云的下载链接和统计信息，用于开发调试和前端演示。
    /// <c>GET /api/app/workflow-execution/mock-execute</c>
    /// </summary>
    Task<MockExecuteResultDto> MockExecuteAsync();

    /// <summary>
    /// 下载示例点云文件。
    /// <c>GET /api/app/workflow-execution/download-sample?blobName=xxx</c>
    /// </summary>
    /// <param name="blobName">BLOB 名称。</param>
    Task<Volo.Abp.Content.IRemoteStreamContent> DownloadSampleAsync(string blobName);
}
