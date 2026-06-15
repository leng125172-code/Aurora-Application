using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Projects;

/// <summary>
/// 项目主表仓储接口。
/// 提供超出基础 CRUD 的自定义查询能力。
/// </summary>
public interface IProjectInfoRepository : IRepository<ProjectInfo, Guid>
{
    /// <summary>
    /// 分页查询项目列表，支持多条件过滤。
    /// </summary>
    /// <param name="skipCount">跳过记录数</param>
    /// <param name="maxResultCount">最大返回数</param>
    /// <param name="filter">关键字（匹配编号或名称）</param>
    /// <param name="status">项目状态过滤（null 表示不过滤）</param>
    /// <param name="sorting">排序字段</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<List<ProjectInfo>> GetListAsync(
        int skipCount,
        int maxResultCount,
        string? filter = null,
        ProjectStatus? status = null,
        string? sorting = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 获取满足条件的项目总数。
    /// </summary>
    /// <param name="filter">关键字（匹配编号或名称）</param>
    /// <param name="status">项目状态过滤（null 表示不过滤）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<long> GetCountAsync(
        string? filter = null,
        ProjectStatus? status = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 检查项目编号是否已存在（排除指定 ID，用于创建/修改时的唯一性校验）。
    /// </summary>
    /// <param name="projectCode">项目编号</param>
    /// <param name="excludeId">排除的项目 ID（修改时传入当前记录 ID）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<bool> ProjectCodeExistsAsync(
        string projectCode,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default
    );
}
