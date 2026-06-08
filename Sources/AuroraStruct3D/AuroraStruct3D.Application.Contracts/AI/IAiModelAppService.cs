using AuroraStruct3D.AI.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型应用服务接口。
/// 路由基础路径：/api/app/ai-model
/// </summary>
public interface IAiModelAppService : IApplicationService
{
    /// <summary>
    /// 获取当前 AI 运行平台能力信息。
    /// </summary>
    Task<AiRuntimePlatformInfoDto> GetRuntimePlatformInfoAsync();

    /// <summary>
    /// 获取模型标识下拉列表。
    /// </summary>
    Task<List<AiModelIdentifierDto>> GetIdentifierLookupAsync(string? filter = null);

    /// <summary>
    /// 按 MD5 检查文件是否已上传。
    /// </summary>
    Task<AiModelFileMd5CheckResultDto> CheckFileMd5Async(CheckAiModelFileMd5Input input);

    /// <summary>
    /// 分页查询 AI 模型列表。
    /// </summary>
    Task<PagedResultDto<AiModelDto>> GetListAsync(GetAiModelListInput input);

    /// <summary>
    /// 获取 AI 模型详情。
    /// </summary>
    Task<AiModelDto> GetAsync(Guid id);

    /// <summary>
    /// 查询 AI 模型操作日志。
    /// </summary>
    Task<PagedResultDto<AiModelOperationLogDto>> GetLogsAsync(GetAiModelLogListInput input);

    /// <summary>
    /// 上传 AI 模型文件。
    /// </summary>
    Task<AiModelDto> UploadAsync(IRemoteStreamContent file, UploadAiModelInput input);

    /// <summary>
    /// 初始化 AI 模型分片上传会话。
    /// </summary>
    Task<InitializeAiModelUploadResultDto> InitializeUploadAsync(
        InitializeAiModelUploadInput input
    );

    /// <summary>
    /// 上传单个 AI 模型分片。
    /// </summary>
    Task<AiModelUploadChunkResultDto> UploadChunkAsync(
        Guid sessionId,
        int chunkIndex,
        IRemoteStreamContent chunk
    );

    /// <summary>
    /// 查询 AI 模型分片上传会话状态。
    /// </summary>
    Task<AiModelUploadSessionStatusDto> GetUploadSessionStatusAsync(Guid sessionId);

    /// <summary>
    /// 按文件特征查找 AI 模型分片上传会话。
    /// </summary>
    Task<AiModelUploadSessionStatusDto?> GetFindUploadSessionAsync(
        FindAiModelUploadSessionInput input
    );

    /// <summary>
    /// 完成 AI 模型分片上传并入库。
    /// </summary>
    Task<AiModelDto> CompleteUploadAsync(CompleteAiModelUploadInput input);

    /// <summary>
    /// 启动 AI 模型转换。
    /// </summary>
    Task<AiModelConversionStartResultDto> StartConversionAsync(Guid id);

    /// <summary>
    /// 取消 AI 模型分片上传会话。
    /// </summary>
    Task AbortUploadAsync(Guid sessionId);

    /// <summary>
    /// 更新 AI 模型元数据。
    /// </summary>
    Task<AiModelDto> UpdateAsync(Guid id, UpdateAiModelInput input);

    /// <summary>
    /// 删除 AI 模型。
    /// </summary>
    Task DeleteAsync(Guid id);

    /// <summary>
    /// 删除指定模型的转换产物文件。
    /// </summary>
    Task DeleteConvertedFileAsync(Guid id, Guid fileId);

    /// <summary>
    /// 加载 AI 模型。
    /// </summary>
    Task<AiModelDto> LoadAsync(Guid id);

    /// <summary>
    /// 卸载 AI 模型。
    /// </summary>
    Task<AiModelDto> UnloadAsync(Guid id);

    /// <summary>
    /// 清理孤立 AI 模型数据。
    /// 会同步处理缺失原始文件的模型记录、损坏的转换产物引用、无记录物理文件和空目录。
    /// </summary>
    Task<int> CleanUpOrphanedRecordsAsync();
}
