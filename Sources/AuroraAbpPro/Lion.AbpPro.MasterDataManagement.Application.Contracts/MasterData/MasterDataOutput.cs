using System;

namespace Lion.AbpPro.MasterDataManagement.MasterData;

/// <summary>
/// 主数据输出参数
/// </summary>
public class MasterDataOutput
{
    /// <summary>
    /// 主键Id
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 主数据类型Id
    /// </summary>
    public Guid MasterDataTypeId { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 编码
    /// </summary>
    public string Code { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; }
}
