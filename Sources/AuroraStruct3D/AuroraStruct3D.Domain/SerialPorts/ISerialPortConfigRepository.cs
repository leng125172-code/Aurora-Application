using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.SerialPorts;

/// <summary>
/// 串口通讯配置仓储接口
/// </summary>
public interface ISerialPortConfigRepository : IRepository<SerialPortConfig, Guid>
{
    /// <summary>
    /// 按系统串口名称查找配置（如 COM3、/dev/ttyS6）
    /// </summary>
    /// <param name="portName">系统串口名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>匹配的串口配置，不存在则返回 null</returns>
    Task<SerialPortConfig?> FindByPortNameAsync(
        string portName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 获取串口配置列表
    /// </summary>
    /// <param name="isEnabled">筛选启用状态，null 表示不筛选</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>串口配置列表（按显示名称排序）</returns>
    Task<List<SerialPortConfig>> GetListAsync(
        bool? isEnabled = null,
        CancellationToken cancellationToken = default
    );
}
