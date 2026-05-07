using System;
using System.ComponentModel.DataAnnotations;

namespace Lion.AbpPro.MasterDataManagement.MasterData
{
    /// <summary>
    /// 设置主数据启用状态输入参数
    /// </summary>
    public class SetMasterDataEnabledInput
    {
        /// <summary>
        /// 主数据Id
        /// </summary>
        [Required]
        public Guid Id { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool Enabled { get; set; }
    }
}
