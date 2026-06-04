using AuroraStruct3D.ProductModels.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace AuroraStruct3D.ProductModels;

/// <summary>
/// 产品三维数模应用服务接口。
/// ABP 自动生成对应 REST 端点，路由基础路径：/api/app/product-model
/// </summary>
public interface IProductModelAppService : IApplicationService
{
    /// <summary>
    /// 分页查询数模列表（支持名称/格式/状态/时间/上传人等多条件过滤）。
    /// GET /api/app/product-model
    /// </summary>
    /// <param name="input">查询条件与分页参数</param>
    Task<PagedResultDto<ProductModelDto>> GetListAsync(GetProductModelListInput input);

    /// <summary>
    /// 获取单个数模详情。
    /// GET /api/app/product-model/{id}
    /// </summary>
    /// <param name="id">数模唯一标识</param>
    Task<ProductModelDto> GetAsync(Guid id);

    /// <summary>
    /// 分页查询数模操作日志。
    /// GET /api/app/product-model/logs
    /// </summary>
    /// <param name="input">日志查询条件</param>
    Task<PagedResultDto<ProductModelOperationLogDto>> GetLogsAsync(
        GetProductModelLogListInput input
    );

    /// <summary>
    /// 上传三维数模文件（单文件，批量由前端多次调用）。
    /// 非 PLY/OBJ 格式会自动入队 Hangfire 后台任务进行异步转换。
    /// POST /api/app/product-model/upload
    /// </summary>
    /// <param name="file">文件流（IRemoteStreamContent，含原始文件名）</param>
    /// <param name="name">用户指定的显示名称（为空时使用文件名去除扩展名）</param>
    Task<ProductModelDto> UploadAsync(IRemoteStreamContent file, string? name);

    /// <summary>
    /// 仅修改数模的显示名称（不影响文件内容和转换状态）。
    /// PUT /api/app/product-model/{id}/name
    /// </summary>
    /// <param name="id">数模唯一标识</param>
    /// <param name="input">新名称</param>
    Task<ProductModelDto> UpdateNameAsync(Guid id, UpdateProductModelNameInput input);

    /// <summary>
    /// 删除数模（同时删除数据库记录和 BLOB 存储文件）。
    /// DELETE /api/app/product-model/{id}
    /// </summary>
    /// <param name="id">数模唯一标识</param>
    Task DeleteAsync(Guid id);

    /// <summary>
    /// 手动重试格式转换（仅对 ConversionStatus == Failed 的数模有效）。
    /// POST /api/app/product-model/{id}/retry-conversion
    /// </summary>
    /// <param name="id">数模唯一标识</param>
    Task RetryConversionAsync(Guid id);

    /// <summary>
    /// 清理文件已丢失的孤立数模记录（仅删除数据库记录，不操作 BLOB）。
    /// POST /api/app/product-model/clean-up-orphaned-records
    /// </summary>
    /// <returns>清理的记录数量</returns>
    Task<int> CleanUpOrphanedRecordsAsync();

    /// <summary>
    /// 下载数模文件（就绪后可下载原始文件或转换后的 PLY 文件）。
    /// GET /api/app/product-model/{id}/download
    /// </summary>
    /// <param name="id">数模唯一标识</param>
    Task<IRemoteStreamContent> GetDownloadAsync(Guid id);
}
