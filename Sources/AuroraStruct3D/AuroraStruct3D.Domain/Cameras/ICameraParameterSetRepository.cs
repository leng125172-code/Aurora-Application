using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Cameras;

/// <summary>
/// 相机参数集仓储接口
/// </summary>
public interface ICameraParameterSetRepository : IRepository<CameraParameterSet, Guid>
{
    /// <summary>
    /// 获取指定相机的所有参数集（包含参数列表）
    /// </summary>
    /// <param name="cameraDeviceId">相机设备ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<List<CameraParameterSet>> GetListByCameraAsync(
        Guid cameraDeviceId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 获取参数集详情（包含所有参数）
    /// </summary>
    /// <param name="id">参数集ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<CameraParameterSet?> GetWithParametersAsync(
        Guid id,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 获取指定相机的默认参数集
    /// </summary>
    /// <param name="cameraDeviceId">相机设备ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<CameraParameterSet?> GetDefaultAsync(
        Guid cameraDeviceId,
        CancellationToken cancellationToken = default
    );
}
