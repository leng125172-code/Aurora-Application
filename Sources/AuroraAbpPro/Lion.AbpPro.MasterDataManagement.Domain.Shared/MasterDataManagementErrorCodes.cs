namespace Lion.AbpPro.MasterDataManagement
{
    /// <summary>
    /// 主数据管理模块错误码定义
    /// </summary>
    public static class MasterDataManagementErrorCodes
    {
        /// <summary>
        /// 主数据类型编码已存在
        /// </summary>
        public const string MasterDataTypeCodeExists = "Lion.AbpPro.MasterDataManagement:010001";

        /// <summary>
        /// 主数据编码已存在
        /// </summary>
        public const string MasterDataCodeExists = "Lion.AbpPro.MasterDataManagement:010002";

        /// <summary>
        /// 主数据属性编码已存在
        /// </summary>
        public const string MasterDataAttributeCodeExists =
            "Lion.AbpPro.MasterDataManagement:010003";
    }
}
