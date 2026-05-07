using System;
using System.ComponentModel.DataAnnotations;
using Lion.AbpPro.Core;

namespace Lion.AbpPro.MasterDataManagement.MasterDataValues;

/// <summary>
/// 获取主数据信息输入参数
/// </summary>
public class GetMasterDataInfoInput : PagingBase
{
    /// <summary>
    /// 主数据类型Id
    /// </summary>
    public Guid? MasterDataTypeId { get; set; }

    /// <summary>
    /// 主数据类型编码
    /// </summary>
    public string MasterDataTypeCode { get; set; }

    /// <summary>
    /// 主数据编码（可选）
    /// </summary>
    public string MasterDataCode { get; set; }
}
