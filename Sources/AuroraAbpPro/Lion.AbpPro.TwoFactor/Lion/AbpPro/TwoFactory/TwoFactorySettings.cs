namespace Lion.AbpPro.TwoFactory;

public static class TwoFactorySettings
{
    /// <summary>
    /// 分组
    /// </summary>
    public static class Group
    {
        public const string Default = "Setting.Group";
        public const string TwoFactory = Default + ".TwoFactory";

        /// <summary>
        /// 颁发者
        /// </summary>
        public const string Issuer = TwoFactory + ".Issuer";

        /// <summary>
        /// 二维码大小
        /// </summary>
        public const string QRCodeSize = TwoFactory + ".QRCodeSize";
    }
}
