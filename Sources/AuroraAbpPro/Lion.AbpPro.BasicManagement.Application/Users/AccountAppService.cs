using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Duende.IdentityModel;
using Lion.AbpPro.BasicManagement.ConfigurationOptions;
using Lion.AbpPro.BasicManagement.UserRefreshTokens;
using Lion.AbpPro.BasicManagement.Users.Dtos;
using Lion.AbpPro.TwoFactory;
using Mapster;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Volo.Abp.Identity.AspNetCore;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;
using IdentityUser = Volo.Abp.Identity.IdentityUser;
using IExternalLoginProvider = Lion.AbpPro.Oidc.IExternalLoginProvider;

namespace Lion.AbpPro.BasicManagement.Users
{
    public class AccountAppService : BasicManagementAppService, IAccountAppService
    {
        private readonly IdentityUserManager _userManager;

        private readonly JwtOptions _jwtOptions;

        //private readonly Microsoft.AspNetCore.Identity.SignInManager<IdentityUser> _signInManager;
        private readonly IdentitySecurityLogManager _identitySecurityLogManager;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly AbpSignInManager _signInManager;
        protected IOptions<IdentityOptions> IdentityOptions { get; }

        private readonly ITwoFactorProvider _twoFactorProvider;

        private readonly IServiceProvider _serviceProvider;

        private readonly IUnitOfWorkManager _unitOfWorkManager;

        private readonly UserRefreshTokenManager _userRefreshTokenManager;

        public AccountAppService(
            IdentityUserManager userManager,
            IOptionsSnapshot<JwtOptions> jwtOptions,
            IdentitySecurityLogManager identitySecurityLogManager,
            IHttpContextAccessor httpContextAccessor,
            AbpSignInManager signInManager,
            ISettingProvider settingProvider,
            IOptions<IdentityOptions> identityOptions,
            ITwoFactorProvider twoFactorProvider,
            IServiceProvider serviceProvider,
            IUnitOfWorkManager unitOfWorkManager,
            UserRefreshTokenManager userRefreshTokenManager
        )
        {
            _userManager = userManager;
            _jwtOptions = jwtOptions.Value;
            _identitySecurityLogManager = identitySecurityLogManager;
            _httpContextAccessor = httpContextAccessor;
            _signInManager = signInManager;
            IdentityOptions = identityOptions;
            _twoFactorProvider = twoFactorProvider;
            _serviceProvider = serviceProvider;
            _unitOfWorkManager = unitOfWorkManager;
            _userRefreshTokenManager = userRefreshTokenManager;
        }

        public virtual async Task<LoginOutput> LoginAsync(LoginInput input)
        {
            await IdentityOptions.SetAsync();

            var result = await _signInManager.PasswordSignInAsync(
                input.Name,
                input.Password,
                false,
                true
            );

            if (result.IsNotAllowed)
            {
                var notAllowedUser = await _userManager.FindByNameAsync(input.Name);
                await ThrowNotAllowedExceptionAsync(notAllowedUser);
            }

            if (result.IsLockedOut)
            {
                throw new BusinessException(BasicManagementErrorCodes.UserLockedOut);
            }

            if (!result.Succeeded)
            {
                throw new BusinessException(BasicManagementErrorCodes.UserOrPasswordMismatch);
            }

            var user = await _userManager.FindByNameAsync(input.Name);

            await _identitySecurityLogManager.SaveAsync(
                new IdentitySecurityLogContext()
                {
                    Action = _httpContextAccessor.HttpContext?.Request.Path,
                    UserName = input.Name,
                    Identity = "Bearer",
                }
            );
            return await BuildResult(user);
        }

        public virtual async Task<LoginOutput> Login2FAAsync(Login2FAInput input)
        {
            await IdentityOptions.SetAsync();
            var user = await _userManager.FindByNameAsync(input.Name);
            if (user != null && user.TwoFactorEnabled)
            {
                if (input.Code.IsNullOrWhiteSpace())
                {
                    throw new BusinessException(BasicManagementErrorCodes.TwoFactorCodeNull);
                }

                var secret = user.ExtraProperties[BasicManagementConsts.TwoFactorySecret]
                    .ToString();
                if (secret.IsNullOrWhiteSpace())
                {
                    throw new BusinessException(BasicManagementErrorCodes.TwoFactorSecretNull);
                }

                var valid = await _twoFactorProvider.VerifyCodeAsync(secret, input.Code);
                if (!valid)
                {
                    throw new BusinessException(BasicManagementErrorCodes.CodeIsNotValid);
                }
            }

            var result = await _signInManager.PasswordSignInAsync(
                input.Name,
                input.Password,
                false,
                true
            );

            if (result.IsNotAllowed)
            {
                await ThrowNotAllowedExceptionAsync(user);
            }

            if (result.IsLockedOut)
            {
                throw new BusinessException(BasicManagementErrorCodes.UserLockedOut);
            }

            if (!result.Succeeded)
            {
                throw new BusinessException(BasicManagementErrorCodes.UserOrPasswordMismatch);
            }

            await _identitySecurityLogManager.SaveAsync(
                new IdentitySecurityLogContext()
                {
                    Action = _httpContextAccessor.HttpContext?.Request.Path,
                    UserName = input.Name,
                    Identity = "Bearer",
                }
            );
            return await BuildResult(user);
        }

        private async Task ThrowNotAllowedExceptionAsync(IdentityUser? user)
        {
            if (user?.IsActive == false)
            {
                throw new BusinessException(BasicManagementErrorCodes.UserDisabled);
            }

            if (user?.ShouldChangePasswordOnNextLogin == true)
            {
                throw new BusinessException(BasicManagementErrorCodes.NewPasswordExpire);
            }

            if (user != null && await _userManager.ShouldPeriodicallyChangePasswordAsync(user))
            {
                throw new BusinessException(BasicManagementErrorCodes.OldPasswordExpire);
            }

            throw new BusinessException(BasicManagementErrorCodes.UserDisabled);
        }

        public async Task<LoginOutput> LoginOidcAsync(LoginOidcInput input)
        {
            var provider = _serviceProvider.GetKeyedService<IExternalLoginProvider>(input.State);
            if (provider == null)
            {
                throw new BusinessException(BasicManagementErrorCodes.UnknownExternalLoginProvider);
            }

            // 通过code获取accesstoken
            var tokenResult = await provider.GetAccessTokenAsync(input.Code);

            // 获取用户信息
            var userInfo = await provider.GetUserInfoAsync(tokenResult.AccessToken);
            // 查询用户是否存在
            var user = await _userManager.FindByNameAsync(userInfo.UserName);
            if (user != null)
            {
                var userLoginAlreadyExists = user?.Logins?.Any(x =>
                    x.TenantId == user.TenantId
                    && x.LoginProvider == input.State
                    && x.ProviderKey == userInfo.Id
                );

                if (userLoginAlreadyExists != null && !userLoginAlreadyExists.Value)
                {
                    (
                        await _userManager.AddLoginAsync(
                            user,
                            new UserLoginInfo(input.State, userInfo.Id, userInfo.Id)
                        )
                    ).CheckErrors();
                }

                return await BuildResult(user);
            }

            var id = GuidGenerator.Create();
            using (var uow = _unitOfWorkManager.Begin())
            {
                user = new IdentityUser(id, userInfo.UserName, userInfo.Email, CurrentTenant.Id)
                {
                    Name = userInfo.Name,
                };
                if (userInfo.Mobile.IsNotNullOrWhiteSpace())
                {
                    user.SetPhoneNumber(userInfo.Mobile, true);
                }

                (await _userManager.CreateAsync(user)).CheckErrors();
                (await _userManager.AddDefaultRolesAsync(user)).CheckErrors();
                var userLoginAlreadyExists = user.Logins.Any(x =>
                    x.TenantId == user.TenantId
                    && x.LoginProvider == input.State
                    && x.ProviderKey == userInfo.Id
                );

                if (!userLoginAlreadyExists)
                {
                    (
                        await _userManager.AddLoginAsync(
                            user,
                            new UserLoginInfo(input.State, userInfo.Id, id.ToString())
                        )
                    ).CheckErrors();
                }

                await uow.CompleteAsync();
            }

            user = await _userManager.FindByIdAsync(id.ToString());

            return await BuildResult(user);
        }

        public async Task<RefreshTokenOutput> RefreshTokenAsync(RefreshTokenInput input)
        {
            var result = new RefreshTokenOutput();
            try
            {
                var user = await _userManager.FindByIdAsync(input.UserId.ToString());
                if (user == null)
                {
                    throw new BusinessException(message: "未解析到用户,无法进行刷新token操作");
                }

                var entity = await _userRefreshTokenManager.FindByRefreshTokenAsync(
                    input.RefreshToken
                );
                if (entity.IsUsed || entity.ExpirationTime < Clock.Now)
                {
                    throw new BusinessException(
                        message: "当前刷新token已过期,无法进行刷新token操作"
                    );
                }

                var roles = await _userManager.GetRolesAsync(user);
                if (roles == null || roles.Count == 0)
                    throw new AbpAuthorizationException();
                var token = GenerateJwt(
                    user.Id,
                    user.UserName,
                    user.Name,
                    user.Email,
                    user.TenantId,
                    roles.ToList()
                );
                var refreshToken = await GenerateRefreshTokenAsync(
                    input.UserId,
                    token,
                    input.RefreshToken
                );
                result.Token = token;
                result.RefreshToken = refreshToken;
                result.Success = true;
                return result;
            }
            catch (Exception e)
            {
                result.Message = e.Message;
                return result;
            }
        }

        #region 私有方法

        protected virtual async Task<LoginOutput> BuildResult(IdentityUser? user)
        {
            if (user == null)
                throw new BusinessException(BasicManagementErrorCodes.UserNotExist);

            if (!user.IsActive)
                throw new BusinessException(BasicManagementErrorCodes.UserLockedOut);
            var roles = await _userManager.GetRolesAsync(user);
            if (roles == null || roles.Count == 0)
                throw new AbpAuthorizationException();
            var token = GenerateJwt(
                user.Id,
                user.UserName,
                user.Name,
                user.Email,
                user.TenantId,
                roles.ToList()
            );
            var loginOutput = user.Adapt<LoginOutput>();
            loginOutput.Token = token;
            loginOutput.Roles = roles.ToList();
            loginOutput.RefreshToken = await GenerateRefreshTokenAsync(user.Id, token);
            return loginOutput;
        }

        /// <summary>
        /// 生成jwt token
        /// </summary>
        /// <returns></returns>
        protected virtual string GenerateJwt(
            Guid userId,
            string userName,
            string? name,
            string? email,
            Guid? tenantId,
            List<string> roles
        )
        {
            var dateNow = Clock.Now.ToUniversalTime();
            var expirationTime = dateNow.AddHours(_jwtOptions.ExpirationTime);
            var key = Encoding.ASCII.GetBytes(_jwtOptions.SecurityKey);

            var claims = new List<Claim>
            {
                new Claim(JwtClaimTypes.Audience, _jwtOptions.Audience),
                new Claim(JwtClaimTypes.Issuer, _jwtOptions.Issuer),
                new Claim(AbpClaimTypes.UserId, userId.ToString()),
                new Claim(AbpClaimTypes.UserName, userName),
            };

            // Name、Email 和 TenantId 都是可选字段。普通预置用户可能没有填写 Name，
            // 不能将 null 传给 Claim，否则成功验证密码后会以 ArgumentNullException 结束登录。
            if (!name.IsNullOrWhiteSpace())
            {
                claims.Add(new Claim(AbpClaimTypes.Name, name));
            }

            if (!email.IsNullOrWhiteSpace())
            {
                claims.Add(new Claim(AbpClaimTypes.Email, email));
            }

            if (tenantId.HasValue)
            {
                claims.Add(new Claim(AbpClaimTypes.TenantId, tenantId.Value.ToString()));
            }

            foreach (var item in roles)
            {
                claims.Add(new Claim(AbpClaimTypes.Role, item));
            }

            var tokenDescriptor = new SecurityTokenDescriptor()
            {
                Subject = new ClaimsIdentity(claims),
                Expires = expirationTime,
                NotBefore = dateNow,
                IssuedAt = dateNow,
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature
                ),
            };
            var handler = new JwtSecurityTokenHandler();
            var token = handler.CreateToken(tokenDescriptor);
            return handler.WriteToken(token);
        }

        /// <summary>
        /// 生成刷新token
        /// </summary>
        protected virtual async Task<string> GenerateRefreshTokenAsync(
            Guid userId,
            string token,
            string oldRefreshToken = ""
        )
        {
            var dateNow = Clock.Now;
            // 默认7天
            var refreshExpirationTime =
                _jwtOptions.RefreshExpirationTime == 0 ? 7 * 24 : _jwtOptions.RefreshExpirationTime;
            var expirationTime = dateNow.AddHours(refreshExpirationTime);
            var refreshToken = GuidGenerator.Create().ToString("N");
            await _userRefreshTokenManager.CreateAsync(
                GuidGenerator.Create(),
                refreshToken,
                userId,
                false,
                expirationTime,
                token
            );
            if (oldRefreshToken.IsNotNullOrWhiteSpace())
            {
                await _userRefreshTokenManager.SetUseAsync(oldRefreshToken);
            }

            return refreshToken;
        }

        #endregion
    }
}
