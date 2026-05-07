using Volo.Abp.Auditing;

namespace Lion.AbpPro.BasicManagement.Users.Dtos
{
    /// <summary>
    /// 登录
    /// </summary>
    public class LoginOidcInput
    {
        /// <summary>
        /// code
        /// </summary>
        [Required]
        public string Code { get; set; }

        /// <summary>
        ///  Provider
        /// </summary>
        [Required]
        public string State { get; set; }
    }
}
