using System;
using Volo.Abp.Application.Dtos;

namespace Lion.AbpPro.MasterDataManagement.MasterDataValues;

/// <summary>
/// 主数据值输出参数
/// </summary>
public class MasterDataValueOutput : EntityDto<Guid>
{
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
}
