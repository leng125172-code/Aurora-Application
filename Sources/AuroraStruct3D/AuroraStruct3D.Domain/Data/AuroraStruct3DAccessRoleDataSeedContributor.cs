using AuroraStruct3D.Permissions;
using Microsoft.AspNetCore.Identity;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;
using IdentityRole = Volo.Abp.Identity.IdentityRole;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace AuroraStruct3D.Data;

/// <summary>
/// 创建并同步系统固定角色。角色名称、默认属性及权限只能通过修改本种子代码调整；
/// 每次执行数据初始化都会撤销不在固定清单中的角色授权。
/// </summary>
public class AuroraStruct3DAccessRoleDataSeedContributor
    : IDataSeedContributor,
        ITransientDependency
{
    // 复用 ABP 内置 admin 角色作为“开发”，避免初始化后出现第五个超级管理员角色。
    public const string DeveloperRoleName = "admin";
    public const string ManagerRoleName = "manager";
    public const string OperatorRoleName = "operator";
    public const string ViewerRoleName = "viewer";

    public const string DeveloperUserName = "admin";
    public const string ManagerUserName = "manager";
    public const string OperatorUserName = "operator";
    public const string ViewerUserName = "viewer";

    private const string RolePermissionProviderName = RolePermissionValueProvider.ProviderName;

    // 这四项权限不授予任何固定角色，角色结构和授权只能改种子代码。
    private static readonly HashSet<string> FixedRoleManagementPermissions =
    [
        "AbpIdentity.Roles.Create",
        "AbpIdentity.Roles.Update",
        "AbpIdentity.Roles.Delete",
        "AbpIdentity.Roles.ManagePermissions",
    ];

    public static readonly IReadOnlyList<FixedRoleDefinition> FixedRoles =
    [
        new(
            DeveloperRoleName,
            false,
            true,
            []
        ),
        new(
            ManagerRoleName,
            false,
            false,
            [
                AuroraStruct3DAccessPermissions.Operation,
                AuroraStruct3DAccessPermissions.Management,
                "AuroraStruct3D.ProjectInfo",
                "AuroraStruct3D.ProjectInfo.Create",
                "AuroraStruct3D.ProjectInfo.Update",
                "AuroraStruct3D.ProjectInfo.ChangeStatus",
                "AuroraStruct3D.ProjectInfo.Delete",
                "AuroraStruct3D.ProductModel",
                "AuroraStruct3D.ProductModel.Upload",
                "AuroraStruct3D.ProductModel.Rename",
                "AuroraStruct3D.ProductModel.Delete",
                "AuroraStruct3D.ProductModel.Download",
                "AuroraStruct3D.ProductModel.RetryConversion",
                "AuroraStruct3D.ProductModel.CleanUp",
                "AbpIdentity.Users",
                "AbpIdentity.Users.Create",
                "AbpIdentity.Users.Update",
                "AbpIdentity.Users.Delete",
                "AbpIdentity.Users.Update.ManageRoles",
                "AbpIdentity.Roles",
            ]
        ),
        new(
            OperatorRoleName,
            false,
            false,
            [
                AuroraStruct3DAccessPermissions.Operation,
                "AuroraStruct3D.ProjectInfo",
                "AuroraStruct3D.ProjectInfo.Create",
                "AuroraStruct3D.ProjectInfo.Update",
                "AuroraStruct3D.ProjectInfo.ChangeStatus",
                "AuroraStruct3D.ProductModel",
                "AuroraStruct3D.ProductModel.Upload",
                "AuroraStruct3D.ProductModel.Rename",
                "AuroraStruct3D.ProductModel.Download",
                "AuroraStruct3D.ProductModel.RetryConversion",
            ]
        ),
        new(
            ViewerRoleName,
            true,
            false,
            [
                // Operation 是当前应用服务的基础访问策略；写操作再由子权限限制。
                AuroraStruct3DAccessPermissions.Operation,
                "AuroraStruct3D.ProjectInfo",
                "AuroraStruct3D.ProductModel",
                "AuroraStruct3D.ProductModel.Download",
            ]
        ),
    ];

    public static readonly IReadOnlyList<FixedUserDefinition> FixedUsers =
    [
        new(DeveloperUserName, "admin@abp.io", DeveloperRoleName, false),
        new(ManagerUserName, "manager@aurora.local", ManagerRoleName, true),
        new(OperatorUserName, "operator@aurora.local", OperatorRoleName, true),
        new(ViewerUserName, "viewer@aurora.local", ViewerRoleName, true),
    ];

    private readonly IdentityRoleManager _roleManager;
    private readonly IdentityUserManager _userManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IPermissionDataSeeder _permissionDataSeeder;
    private readonly IPermissionGrantRepository _permissionGrantRepository;
    private readonly IPermissionDefinitionManager _permissionDefinitionManager;
    private readonly ICurrentTenant _currentTenant;

    public AuroraStruct3DAccessRoleDataSeedContributor(
        IdentityRoleManager roleManager,
        IdentityUserManager userManager,
        IGuidGenerator guidGenerator,
        IPermissionDataSeeder permissionDataSeeder,
        IPermissionGrantRepository permissionGrantRepository,
        IPermissionDefinitionManager permissionDefinitionManager,
        ICurrentTenant currentTenant
    )
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _guidGenerator = guidGenerator;
        _permissionDataSeeder = permissionDataSeeder;
        _permissionGrantRepository = permissionGrantRepository;
        _permissionDefinitionManager = permissionDefinitionManager;
        _currentTenant = currentTenant;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        using (_currentTenant.Change(context.TenantId))
        {
            IReadOnlyCollection<string> developerPermissions =
                await GetDeveloperPermissionsAsync(context.TenantId);

            foreach (FixedRoleDefinition definition in FixedRoles)
            {
                await CreateOrUpdateRoleAsync(definition, context.TenantId);
                IReadOnlyCollection<string> permissions = definition.GrantAllDefinedPermissions
                    ? developerPermissions
                    : definition.Permissions;
                await SynchronizePermissionsAsync(
                    definition.Name,
                    permissions,
                    context.TenantId
                );
            }

            string initialPassword =
                context[IdentityDataSeedContributor.AdminPasswordPropertyName] as string
                ?? IdentityDataSeedContributor.AdminPasswordDefaultValue;
            string administratorEmail =
                context[IdentityDataSeedContributor.AdminEmailPropertyName] as string
                ?? IdentityDataSeedContributor.AdminEmailDefaultValue;

            foreach (FixedUserDefinition definition in FixedUsers)
            {
                string email = definition.UserName == DeveloperUserName
                    ? administratorEmail
                    : definition.Email;
                await CreateOrUpdateUserAsync(
                    definition,
                    email,
                    initialPassword,
                    context.TenantId
                );
            }
        }
    }

    private async Task CreateOrUpdateUserAsync(
        FixedUserDefinition definition,
        string email,
        string initialPassword,
        Guid? tenantId)
    {
        IdentityUser? user = await _userManager.FindByNameAsync(definition.UserName);
        if (user is null)
        {
            user = new IdentityUser(
                _guidGenerator.Create(),
                definition.UserName,
                email,
                tenantId
            );
            user.SetEmailConfirmed(true);
            user.SetShouldChangePasswordOnNextLogin(
                definition.ShouldChangePasswordOnNextLogin
            );
            // 与 ABP 管理员种子保持一致：初始化密码由数据种子提供，不在此重复套用密码策略。
            (
                await _userManager.CreateAsync(
                    user,
                    initialPassword,
                    validatePassword: false
                )
            ).CheckErrors();
        }

        // 固定账号始终只归属其对应的一个固定角色；不会重置已有账号的密码和资料。
        (await _userManager.SetRolesAsync(user, [definition.RoleName])).CheckErrors();
    }

    private async Task CreateOrUpdateRoleAsync(
        FixedRoleDefinition definition,
        Guid? tenantId)
    {
        IdentityRole? role = await _roleManager.FindByNameAsync(definition.Name);
        if (role is null)
        {
            role = new IdentityRole(_guidGenerator.Create(), definition.Name, tenantId)
            {
                IsDefault = definition.IsDefault,
                IsStatic = true,
                IsPublic = true,
            };
            (await _roleManager.CreateAsync(role)).CheckErrors();
            return;
        }

        if (role.IsDefault == definition.IsDefault && role.IsStatic && role.IsPublic)
        {
            return;
        }

        role.IsDefault = definition.IsDefault;
        role.IsStatic = true;
        role.IsPublic = true;
        (await _roleManager.UpdateAsync(role)).CheckErrors();
    }

    private async Task<IReadOnlyCollection<string>> GetDeveloperPermissionsAsync(Guid? tenantId)
    {
        MultiTenancySides side = tenantId.HasValue
            ? MultiTenancySides.Tenant
            : MultiTenancySides.Host;
        IReadOnlyList<PermissionDefinition> definitions =
            await _permissionDefinitionManager.GetPermissionsAsync();

        return BuildDeveloperPermissions(
            definitions
                .Where(x =>
                    x.IsEnabled
                    && x.MultiTenancySide.HasFlag(side)
                    && (!x.Providers.Any() || x.Providers.Contains(RolePermissionProviderName))
                )
                .Select(x => x.Name)
        );
    }

    internal static IReadOnlyCollection<string> BuildDeveloperPermissions(
        IEnumerable<string> definedPermissions)
    {
        return definedPermissions
            .Where(x => !IsFixedRoleManagementPermission(x))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    public static bool IsFixedRoleManagementPermission(string permissionName)
    {
        return FixedRoleManagementPermissions.Contains(permissionName);
    }

    private async Task SynchronizePermissionsAsync(
        string roleName,
        IReadOnlyCollection<string> desiredPermissions,
        Guid? tenantId)
    {
        HashSet<string> desired = desiredPermissions.ToHashSet(StringComparer.Ordinal);
        List<PermissionGrant> current = await _permissionGrantRepository.GetListAsync(
            RolePermissionProviderName,
            roleName
        );
        List<PermissionGrant> obsolete = current
            .Where(x => !desired.Contains(x.Name))
            .ToList();
        if (obsolete.Count > 0)
        {
            await _permissionGrantRepository.DeleteManyAsync(obsolete, autoSave: true);
        }

        await _permissionDataSeeder.SeedAsync(
            RolePermissionProviderName,
            roleName,
            desired,
            tenantId
        );
    }
}

public sealed record FixedRoleDefinition(
    string Name,
    bool IsDefault,
    bool GrantAllDefinedPermissions,
    IReadOnlyCollection<string> Permissions
);

public sealed record FixedUserDefinition(
    string UserName,
    string Email,
    string RoleName,
    bool ShouldChangePasswordOnNextLogin
);
