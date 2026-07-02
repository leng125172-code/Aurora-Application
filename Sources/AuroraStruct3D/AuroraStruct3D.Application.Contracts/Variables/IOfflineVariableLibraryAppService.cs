using AuroraStruct3D.Variables.Dtos;

namespace AuroraStruct3D.Variables;

/// <summary>
/// 离线变量库应用服务：提供编译式校验与变量可见性查询。
/// </summary>
public interface IOfflineVariableLibraryAppService : IApplicationService
{
    /// <summary>
    /// 对指定工作流的变量声明与引用执行离线编译校验，并持久化可发布快照。
    /// </summary>
    Task<VariableCompileResultDto> CompileAsync(VariableCompileRequestDto input);

    /// <summary>
    /// 获取指定工作流可见的变量集合（本地声明 + 导入可读变量）。
    /// </summary>
    Task<List<VariableDefinitionDto>> GetVisibleVariablesAsync(Guid projectId, Guid workflowId);
}
