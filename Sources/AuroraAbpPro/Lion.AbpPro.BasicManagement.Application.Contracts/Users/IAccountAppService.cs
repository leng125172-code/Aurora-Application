using Lion.AbpPro.BasicManagement.Users.Dtos;

namespace Lion.AbpPro.BasicManagement.Users
{
    public interface IAccountAppService : IApplicationService
    {
        /// <summary>
        /// 用户名密码登录
        /// </summary>
        Task<LoginOutput> LoginAsync(LoginInput input);

        /// <summary>
        /// 双因素验证登录
        /// </summary>
        Task<LoginOutput> Login2FAAsync(Login2FAInput input);

        /// <summary>
        /// 第三方扩展登录
        /// </summary>
        Task<LoginOutput> LoginOidcAsync(LoginOidcInput input);

        /// <summary>
        /// 刷新token
        /// </summary>
        Task<RefreshTokenOutput> RefreshTokenAsync(RefreshTokenInput input);
    }
}
