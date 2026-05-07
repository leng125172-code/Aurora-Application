using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace Lion.AbpPro.MasterDataManagement.EntityFrameworkCore;

/// <summary>
/// 主数据管理模块数据库上下文接口
/// </summary>
[ConnectionStringName(MasterDataManagementDbProperties.ConnectionStringName)]
public interface IMasterDataManagementDbContext : IEfCoreDbContext
{
    /// <summary>
    /// 主数据属性 DbSet
    /// </summary>
    DbSet<MasterDataAttribute> MasterDataAttributes { get; set; }

    /// <summary>
    /// 主数据 DbSet
    /// </summary>
    DbSet<MasterData> MasterDatas { get; set; }

    /// <summary>
    /// 主数据类型 DbSet
    /// </summary>
    DbSet<MasterDataType> MasterDataTypes { get; set; }

    /// <summary>
    /// 主数据值 DbSet
    /// </summary>
    DbSet<MasterDataValue> MasterDataValues { get; set; }
}
