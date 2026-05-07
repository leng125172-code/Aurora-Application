namespace Lion.AbpPro.BasicManagement.Systems
{
    public class AccountController : BasicManagementController, IAccountAppService
    {
        private readonly IAccountAppService _accountAppService;

        public AccountController(IAccountAppService accountAppService)
        {
            _accountAppService = accountAppService;
        }

        [SwaggerOperation(summary: "登录", Tags = new[] { "Account" })]
        public Task<LoginOutput> LoginAsync(LoginInput input)
        {
            return _accountAppService.LoginAsync(input);
        }

        [SwaggerOperation(summary: "2FA登录", Tags = new[] { "Account" })]
        public Task<LoginOutput> Login2FAAsync(Login2FAInput input)
        {
            return _accountAppService.Login2FAAsync(input);
        }

        [SwaggerOperation(summary: "第三方扩展登录", Tags = new[] { "Account" })]
        public Task<LoginOutput> LoginOidcAsync(LoginOidcInput input)
        {
            return _accountAppService.LoginOidcAsync(input);
        }

        [SwaggerOperation(summary: "刷新token", Tags = new[] { "Account" })]
        public Task<RefreshTokenOutput> RefreshTokenAsync(RefreshTokenInput input)
        {
            return _accountAppService.RefreshTokenAsync(input);
        }
    }
}
