using System.Security.Claims;
using Lion.AbpPro.BasicManagement.Users.Dtos;
using Lion.AbpPro.TwoFactory;
using Magicodes.ExporterAndImporter.Excel;
using Magicodes.ExporterAndImporter.Excel.AspNetCore;
using Mapster;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Account;
using Volo.Abp.Auditing;
using Volo.Abp.Security.Encryption;
using IdentityRole = Volo.Abp.Identity.IdentityRole;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Lion.AbpPro.BasicManagement.Users
{
    [Authorize]
    public class UserAppService : BasicManagementAppService, IUserAppService
    {
        private readonly IIdentityUserAppService _identityUserAppService;
        private readonly IdentityUserManager _userManager;
        private readonly IIdentityUserRepository _identityUserRepository;
        private readonly IExcelExporter _excelExporter;
        private readonly IOptions<IdentityOptions> _options;
        private readonly ITwoFactorProvider _twoFactorProvider;
        private readonly IStringEncryptionService _stringEncryptionService;
        private readonly ISettingProvider _settingProvider;

        public UserAppService(
            IIdentityUserAppService identityUserAppService,
            IdentityUserManager userManager,
            IIdentityUserRepository userRepository,
            IExcelExporter excelExporter,
            IOptions<IdentityOptions> options,
            ITwoFactorProvider twoFactorProvider,
            IStringEncryptionService stringEncryptionService,
            ISettingProvider settingProvider
        )
        {
            _identityUserAppService = identityUserAppService;
            _userManager = userManager;
            _identityUserRepository = userRepository;
            _excelExporter = excelExporter;
            _options = options;
            _settingProvider = settingProvider;
            _twoFactorProvider = twoFactorProvider;
            _stringEncryptionService = stringEncryptionService;
        }

        /// <summary>
        /// 分页查询用户
        /// </summary>
        [Authorize(IdentityPermissions.Users.Default)]
        public virtual async Task<PagedResultDto<PageIdentityUserOutput>> ListAsync(
            PagingUserListInput input
        )
        {
            var request = new GetIdentityUsersInput
            {
                Filter = input.Filter?.Trim(),
                MaxResultCount = input.PageSize,
                SkipCount = input.SkipCount,
                Sorting = nameof(IHasModificationTime.LastModificationTime),
            };

            var count = await _identityUserRepository.GetCountAsync(request.Filter);
            var source = await _identityUserRepository.GetListAsync(
                request.Sorting,
                request.MaxResultCount,
                request.SkipCount,
                request.Filter
            );

            return new PagedResultDto<PageIdentityUserOutput>(
                count,
                source.Adapt<List<PageIdentityUserOutput>>()
            );
        }

        public async Task<List<IdentityUserDto>> ListAllAsync(PagingUserListInput input)
        {
            var request = new GetIdentityUsersInput
            {
                Filter = input.Filter?.Trim(),
                MaxResultCount = input.PageSize,
                SkipCount = input.SkipCount,
                Sorting = nameof(IHasModificationTime.LastModificationTime),
            };

            var source = await _identityUserRepository.GetListAsync(
                request.Sorting,
                request.MaxResultCount,
                request.SkipCount,
                request.Filter
            );

            return source.Adapt<List<IdentityUserDto>>();
        }

        /// <summary>
        /// 用户导出列表
        /// </summary>
        [Authorize(BasicManagementPermissions.SystemManagement.UserExport)]
        public virtual async Task<ActionResult> ExportAsync(PagingUserListInput input)
        {
            var request = new GetIdentityUsersInput
            {
                Filter = input.Filter?.Trim(),
                MaxResultCount = input.PageSize,
                SkipCount = input.SkipCount,
                Sorting = " LastModificationTime desc",
            };
            var source = await _identityUserRepository.GetListAsync(
                request.Sorting,
                request.MaxResultCount,
                request.SkipCount,
                request.Filter
            );
            var result = source.Adapt<List<ExportIdentityUserOutput>>();
            var bytes = await _excelExporter.ExportAsByteArray<ExportIdentityUserOutput>(result);
            return new XlsxFileResult(
                bytes: bytes,
                fileDownloadName: $"用户导出列表{Clock.Now:yyyyMMdd}"
            );
        }

        /// <summary>
        /// 新增用户
        /// </summary>
        [Authorize(IdentityPermissions.Users.Create)]
        public virtual async Task<IdentityUserDto> CreateAsync(IdentityUserCreateDto input)
        {
            // abp 5.0 之后新增字段,是否运行用户登录，默认设置为true
            input.IsActive = true;
            input.LockoutEnabled = true;
            return await _identityUserAppService.CreateAsync(input);
        }

        /// <summary>
        /// 更新用户
        /// </summary>
        [Authorize(IdentityPermissions.Users.Update)]
        public virtual async Task<IdentityUserDto> UpdateAsync(UpdateUserInput input)
        {
            return await _identityUserAppService.UpdateAsync(input.UserId, input.UserInfo);
        }

        /// <summary>
        /// 删除用户
        /// </summary>
        [Authorize(IdentityPermissions.Users.Delete)]
        public virtual async Task DeleteAsync(IdInput input)
        {
            await _identityUserAppService.DeleteAsync(input.Id);
        }

        /// <summary>
        /// 获取用户角色信息
        /// </summary>
        public virtual async Task<ListResultDto<IdentityRoleDto>> GetRoleByUserId(IdInput input)
        {
            var roles = await _identityUserRepository.GetRolesAsync(input.Id);
            return new ListResultDto<IdentityRoleDto>(roles.Adapt<List<IdentityRoleDto>>());
        }

        /// <summary>
        /// 修改密码
        /// </summary>
        public virtual async Task<bool> ChangePasswordAsync(ChangePasswordInput input)
        {
            await _options.SetAsync();
            var identityUser = await _userManager.GetByIdAsync(base.CurrentUser.GetId());
            IdentityResult result;
            if (identityUser.PasswordHash == null)
            {
                result = await _userManager.AddPasswordAsync(identityUser, input.NewPassword);
                result.CheckErrors();
            }
            else
            {
                result = await _userManager.ChangePasswordAsync(
                    identityUser,
                    input.CurrentPassword,
                    input.NewPassword
                );
                result.CheckErrors();
            }

            await UpdateChangePasswordTimeAsync(identityUser);
            return result.Succeeded;
        }

        private async Task UpdateChangePasswordTimeAsync(IdentityUser identityUser)
        {
            var now = Clock.Now.ToString("u");
            var firstChangePasswordTime = identityUser.GetFirstChangePasswordTime();
            if (firstChangePasswordTime == null)
            {
                // 用户是否第一次修改密码
                await _userManager.AddClaimAsync(
                    identityUser,
                    new Claim(BasicManagementConsts.FirstChangePasswordTime, now)
                );
                await _userManager.AddClaimAsync(
                    identityUser,
                    new Claim(BasicManagementConsts.LastChangePasswordTime, now)
                );
            }
            else
            {
                // 更新最后一次登录时间
                var lastChangePasswordTimeClaim = identityUser.Claims.FirstOrDefault(e =>
                    e.ClaimType == BasicManagementConsts.LastChangePasswordTime
                );
                if (lastChangePasswordTimeClaim != null)
                {
                    await _userManager.ReplaceClaimAsync(
                        identityUser,
                        new Claim(
                            lastChangePasswordTimeClaim.ClaimType,
                            lastChangePasswordTimeClaim.ClaimValue
                        ),
                        new Claim(BasicManagementConsts.LastChangePasswordTime, now)
                    );
                }
                else
                {
                    await _userManager.AddClaimAsync(
                        identityUser,
                        new Claim(BasicManagementConsts.LastChangePasswordTime, now)
                    );
                }
            }
        }

        /// <summary>
        /// 重置
        /// </summary>
        [Authorize(BasicManagementPermissions.SystemManagement.ResetPassword)]
        public virtual async Task<bool> RestPasswordAsync(ResetPasswordInput input)
        {
            await _options.SetAsync();
            var identityUser = await _userManager.GetByIdAsync(input.UserId);
            await _userManager.RemovePasswordAsync(identityUser);
            var result = await _userManager.AddPasswordAsync(identityUser, input.Password);
            result.CheckErrors();
            await UpdateChangePasswordTimeAsync(identityUser);
            return result.Succeeded;
        }

        /// <summary>
        /// 锁定用户
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        [Authorize(BasicManagementPermissions.SystemManagement.UserEnable)]
        public virtual async Task LockAsync(LockUserInput input)
        {
            var identityUser = await _userManager.GetByIdAsync(input.UserId);
            identityUser.SetIsActive(input.Locked);
            await _userManager.UpdateAsync(identityUser);
        }

        /// <summary>
        /// 通过username获取用户信息
        /// </summary>
        public virtual async Task<IdentityUserDto> FindByUserNameAsync(FindByUserNameInput input)
        {
            var user = await _userManager.FindByNameAsync(input.UserName);
            if (user == null)
            {
                throw new BusinessException(BasicManagementErrorCodes.UserNotExist);
            }

            return user.Adapt<IdentityUserDto>();
        }

        public virtual async Task<MyProfileOutput> MyProfileAsync()
        {
            var user = await _userManager.FindByIdAsync(CurrentUser.GetId().ToString());
            if (user == null)
            {
                throw new BusinessException(BasicManagementErrorCodes.UserNotExist);
            }
            return user.Adapt<MyProfileOutput>();
        }

        /// <summary>
        /// 是否开启双因素验证
        /// </summary>
        public async Task<bool> CanUseTwoFactorAsync()
        {
            var user = await _userManager.FindByIdAsync(CurrentUser.GetId().ToString());
            return user != null && user.TwoFactorEnabled;
        }

        /// <summary>
        /// 获取双因素验证二维码
        /// </summary>
        public async Task<GetQRCodeOutput> GetQRCodeAsync()
        {
            var userName = CurrentTenant.Name + CurrentUser.UserName;
            if (userName.IsNullOrWhiteSpace())
            {
                throw new BusinessException(BasicManagementErrorCodes.UserNotExist);
            }

            var code = await _twoFactorProvider.GetQRCodeAsync(userName);
            var result = new GetQRCodeOutput
            {
                QRCode = code.QRCode,
                Secret = _stringEncryptionService.Encrypt(code.Secret),
            };

            return result;
        }

        public async Task DisabledTwoFactorAsync(DisabledTwoFactorInput input)
        {
            var user = await _userManager.FindByIdAsync(CurrentUser.GetId().ToString());

            if (user == null)
            {
                throw new BusinessException(BasicManagementErrorCodes.UserNotExist);
            }

            var secret = user.ExtraProperties[BasicManagementConsts.TwoFactorySecret]?.ToString();

            if (secret.IsNullOrWhiteSpace())
            {
                throw new BusinessException(BasicManagementErrorCodes.TwoFactorSecretNull);
            }

            var valid = await _twoFactorProvider.VerifyCodeAsync(secret, input.Code);
            if (!valid)
            {
                throw new BusinessException(BasicManagementErrorCodes.CodeIsNotValid);
            }

            user.ExtraProperties[BasicManagementConsts.TwoFactorySecret] = string.Empty;
            await _userManager.SetTwoFactorEnabledAsync(user, false);
        }

        public async Task EnabledTwoFactorAsync(EnabledTwoFactorInput input)
        {
            input.Secret = _stringEncryptionService.Decrypt(input.Secret);
            var valid = await _twoFactorProvider.VerifyCodeAsync(input.Secret, input.Code);
            if (!valid)
            {
                throw new BusinessException(BasicManagementErrorCodes.CodeIsNotValid);
            }

            var user = await _userManager.FindByIdAsync(CurrentUser.GetId().ToString());

            if (user == null)
            {
                throw new BusinessException(BasicManagementErrorCodes.UserNotExist);
            }

            user.ExtraProperties[BasicManagementConsts.TwoFactorySecret] = input.Secret;
            await _userManager.SetTwoFactorEnabledAsync(user, true);
        }

        [Authorize(BasicManagementPermissions.SystemManagement.ResetUserTwoFactor)]
        public async Task ResetTwoFactorAsync(ResetTwoFactorInput input)
        {
            var user = await _userManager.FindByIdAsync(input.UserId.ToString());

            if (user == null)
            {
                throw new BusinessException(BasicManagementErrorCodes.UserNotExist);
            }

            user.ExtraProperties[BasicManagementConsts.TwoFactorySecret] = string.Empty;

            await _userManager.SetTwoFactorEnabledAsync(user, false);
        }

        public virtual async Task<NeedChangePasswordOutput> NeedChangePasswordAsync()
        {
            var result = new NeedChangePasswordOutput();
            // 获取首次修改密码设置
            var value = await _settingProvider.GetOrNullAsync(
                BasicManagementConsts.EnableNewAccountRequiredChangePassword
            );
            bool.TryParse(value, out var convertValue);
            if (!convertValue)
                return result;
            var user = await _userManager.FindByIdAsync(CurrentUser.GetId().ToString());
            if (user == null)
                return result;

            var firstChangePasswordTime = user.GetFirstChangePasswordTime();
            if (firstChangePasswordTime == null)
            {
                result.NeedChangePassword = true;
                result.Message = L[BasicManagementErrorCodes.NewPasswordExpire];
                return result;
            }

            // 获取定期修改密码设置
            var expireValue = await _settingProvider.GetOrNullAsync(
                BasicManagementConsts.EnableExpireRequiredChangePassword
            );
            bool.TryParse(expireValue, out var expire);

            if (!expire)
                return result;

            var lastChangePasswordTime = user.GetLastChangePasswordTime();
            if (!lastChangePasswordTime.HasValue)
                return result;

            // 获取过期天数
            var expireDays = await _settingProvider.GetOrNullAsync(
                BasicManagementConsts.PasswordExpireDay
            );
            int.TryParse(expireDays, out var expireDay);

            // 获取过期提醒天数
            var remindDays = await _settingProvider.GetOrNullAsync(
                BasicManagementConsts.PasswordRemindDay
            );
            int.TryParse(remindDays, out var remindDay);

            // 提前7天提示用户需要修改密码
            var day = expireDay - remindDay;
            if (lastChangePasswordTime.Value > Clock.Now.AddDays(-day))
                return result;

            result.NeedChangePassword = true;
            result.Message = L[BasicManagementErrorCodes.OldPasswordExpire];
            return result;
        }
    }
}
