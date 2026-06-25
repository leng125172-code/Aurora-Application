using AuroraStruct3D.VariableStage.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace AuroraStruct3D.VariableStage;

/// <summary>
/// 变量暂存管理应用服务接口。
/// 路由基础路径：/api/app/variable-stage
/// <para>
/// 用于暂存工作流算子运行时的输出变量值到 Redis，以及后续算子通过变量名（key）读取暂存值。
/// 变量的生命周期管理通过 <c>DeleteAsync</c> 释放单个变量，<c>DeleteAllAsync</c> 批量释放所有变量。
/// </para>
/// </summary>
public interface IVariableStageAppService : IApplicationService
{
    /// <summary>
    /// 暂存变量值到 Redis。
    /// POST /api/app/variable-stage/set
    /// <para>
    /// 请求体为 multipart/form-data，包含：
    /// <list type="bullet">
    ///   <item><c>key</c> — 变量名</item>
    ///   <item><c>valueType</c> — 值类型标识</item>
    ///   <item><c>file</c> — 二进制文件内容</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="key">变量名。</param>
    /// <param name="valueType">值类型，用于反序列化时还原 CLR 类型。</param>
    /// <param name="file">二进制值内容。</param>
    Task SetAsync(string key, string valueType, IRemoteStreamContent file);

    /// <summary>
    /// 根据变量名获取暂存值。
    /// GET /api/app/variable-stage/{key}
    /// </summary>
    /// <param name="key">变量名。</param>
    /// <returns>变量值 DTO，包含 Base64 编码的值内容和元数据。</returns>
    Task<VariableValueDto> GetAsync(string key);

    /// <summary>
    /// 释放单个暂存变量。
    /// DELETE /api/app/variable-stage/{key}
    /// </summary>
    /// <param name="key">变量名。</param>
    Task DeleteAsync(string key);

    /// <summary>
    /// 获取所有暂存变量名列表。
    /// GET /api/app/variable-stage/keys
    /// </summary>
    /// <returns>变量名列表。</returns>
    Task<List<string>> GetKeysAsync();

    /// <summary>
    /// 释放所有暂存变量。
    /// DELETE /api/app/variable-stage
    /// </summary>
    Task DeleteAllAsync();
}
