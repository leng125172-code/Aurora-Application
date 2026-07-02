using AuroraStruct3D.Variables.Dtos;

namespace AuroraStruct3D.Variables;

/// <summary>
/// 在线变量池应用服务：提供实例初始化、运行时读写与等待机制。
/// </summary>
public interface IOnlineVariablePoolAppService : IApplicationService
{
    /// <summary>
    /// 初始化指定实例的在线变量池槽位。
    /// </summary>
    Task InitializeAsync(InitializeVariablePoolInput input);

    /// <summary>
    /// 读取运行时变量值（支持未初始化等待策略）。
    /// </summary>
    Task<ReadVariableResultDto> ReadAsync(ReadVariableInput input);

    /// <summary>
    /// 写入运行时变量值（带权限与乐观并发校验）。
    /// </summary>
    Task<WriteVariableResultDto> WriteAsync(WriteVariableInput input);
}
