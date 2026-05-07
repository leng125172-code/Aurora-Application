using System;

namespace Lion.AbpPro.MasterDataManagement.MasterDataValues;

public class MasterDataValueWithMasterDataDto
{
    /// <summary>
    /// 主数据值Id
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 主数据Id
    /// </summary>
    public Guid MasterDataId { get; set; }

    /// <summary>
    /// 主属性属性Id
    /// </summary>
    public Guid MasterDataAttributeId { get; set; }

    /// <summary>
    /// 属性值
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// 主数据编码
    /// </summary>
    public string MasterDataCode { get; set; }

    /// <summary>
    /// 主数据名称
    /// </summary>
    public string MasterDataName { get; set; }

    /// <summary>
    /// 主数据属性编码
    /// </summary>
    public string MasterDataAttributeCode { get; set; }

    /// <summary>
    /// 主数据属性名称
    /// </summary>
    public string MasterDataAttributeName { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; }
}
