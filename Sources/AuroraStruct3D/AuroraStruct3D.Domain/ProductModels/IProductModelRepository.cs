using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.ProductModels;

/// <summary>
/// 产品三维数模仓储接口。
/// 在标准 ABP 仓储基础上扩展多条件过滤查询。
/// </summary>
public interface IProductModelRepository : IRepository<ProductModel, Guid>
{
    /// <summary>
    /// 多条件分页查询数模列表。
    /// </summary>
    /// <param name="filter">名称模糊搜索（null 表示不过滤）</param>
    /// <param name="fileFormat">按格式过滤（null 表示不过滤）</param>
    /// <param name="conversionStatus">按转换状态过滤（null 表示不过滤）</param>
    /// <param name="startTime">上传时间起始（null 表示不过滤）</param>
    /// <param name="endTime">上传时间截止（null 表示不过滤）</param>
    /// <param name="uploaderUserId">上传人用户 ID（null 表示不过滤）</param>
    /// <param name="skipCount">跳过条数（分页）</param>
    /// <param name="maxResultCount">最多返回条数</param>
    /// <param name="sorting">排序表达式（如 "CreationTime desc"）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<List<ProductModel>> GetListAsync(
        string? filter = null,
        ProductModelFormat? fileFormat = null,
        ProductModelConversionStatus? conversionStatus = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        Guid? uploaderUserId = null,
        int skipCount = 0,
        int maxResultCount = 20,
        string? sorting = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 多条件查询数模总数（与 GetListAsync 参数对应，用于分页 TotalCount）。
    /// </summary>
    Task<int> GetCountAsync(
        string? filter = null,
        ProductModelFormat? fileFormat = null,
        ProductModelConversionStatus? conversionStatus = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        Guid? uploaderUserId = null,
        CancellationToken cancellationToken = default
    );
}
