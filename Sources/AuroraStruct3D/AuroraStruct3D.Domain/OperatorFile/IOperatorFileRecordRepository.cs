using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.OperatorFile;

/// <summary>
/// 算子文件记录仓储接口。
/// </summary>
public interface IOperatorFileRecordRepository : IRepository<OperatorFileRecord, Guid>
{
    /// <summary>
    /// 获取所有过期且未使用的文件记录。
    /// </summary>
    /// <param name="now">当前时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<List<OperatorFileRecord>> GetExpiredUnusedAsync(DateTime now, CancellationToken cancellationToken = default);
}