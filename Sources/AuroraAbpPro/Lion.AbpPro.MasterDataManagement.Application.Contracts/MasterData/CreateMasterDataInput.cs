using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Lion.AbpPro.MasterDataManagement.MasterData
{
    /// <summary>
    /// 创建主数据输入参数
    /// </summary>
    public class CreateMasterDataInput
    {
        /// <summary>
        /// 主数据类型编码
        /// </summary>
        [Required]
        public string MasterDataTypeCode { get; set; }

        /// <summary>
        /// 名称
        /// </summary>
        [Required]
        public string Name { get; set; }

        /// <summary>
        /// 编码
        /// </summary>
        [Required]
        public string Code { get; set; }

        /// <summary>
        /// 属性值字典，键为属性编码，值为属性值
        /// </summary>
        public Dictionary<string, object> Attributes { get; set; } =
            new Dictionary<string, object>();
    }
}
