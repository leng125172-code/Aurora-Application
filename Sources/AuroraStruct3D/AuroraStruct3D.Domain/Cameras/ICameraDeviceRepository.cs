using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Cameras;

/// <summary>
/// 相机设备仓储接口
/// </summary>
public interface ICameraDeviceRepository : IRepository<CameraDevice, Guid>
{
    /// <summary>
    /// 根据设备物理索引查找相机
    /// </summary>
    /// <param name="deviceIndex">设备索引（SDK中的物理位置）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<CameraDevice?> FindByDeviceIndexAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 获取所有启用的相机列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task<List<CameraDevice>> GetEnabledListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取相机列表（包含参数集信息，分页）
    /// </summary>
    /// <param name="skipCount">跳过条数</param>
    /// <param name="maxResultCount">最大条数</param>
    /// <param name="filter">名称过滤关键字</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<List<CameraDevice>> GetListAsync(
        int skipCount,
        int maxResultCount,
        string? filter = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 获取相机总数
    /// </summary>
    /// <param name="filter">名称过滤关键字</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<long> GetCountAsync(string? filter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取相机详情（含所有参数集）
    /// </summary>
    /// <param name="id">相机ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<CameraDevice?> GetWithParameterSetsAsync(
        Guid id,
        CancellationToken cancellationToken = default
    );
}
