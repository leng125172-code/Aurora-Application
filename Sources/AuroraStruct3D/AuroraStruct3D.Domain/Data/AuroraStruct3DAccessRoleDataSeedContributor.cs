using AuroraStruct3D.Permissions;
using Microsoft.AspNetCore.Identity;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Guids;
using Volo.Abp.PermissionManagement;
using IdentityRole = Volo.Abp.Identity.IdentityRole;

namespace AuroraStruct3D.Data;

/// <summary>
/// 建立默认的操作员角色。未登录用户不对应数据库角色；新建用户默认成为操作员，
/// 管理员角色不再作为新用户的默认角色。
/// </summary>
public class AuroraStruct3DAccessRoleDataSeedContributor
    : IDataSeedContributor,
        ITransientDependency
{
    public const string OperatorRoleName = "operator";

    private readonly IdentityRoleManager _roleManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IPermissionDataSeeder _permissionDataSeeder;

    public AuroraStruct3DAccessRoleDataSeedContributor(
        IdentityRoleManager roleManager,
        IGuidGenerator guidGenerator,
        IPermissionDataSeeder permissionDataSeeder
    )
    {
        _roleManager = roleManager;
        _guidGenerator = guidGenerator;
        _permissionDataSeeder = permissionDataSeeder;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        IdentityRole? adminRole = await _roleManager.FindByNameAsync("admin");
        if (adminRole is not null && adminRole.IsDefault)
        {
            adminRole.IsDefault = false;
            (await _roleManager.UpdateAsync(adminRole)).CheckErrors();
        }

        IdentityRole? operatorRole = await _roleManager.FindByNameAsync(OperatorRoleName);
        if (operatorRole is null)
        {
            operatorRole = new IdentityRole(
                _guidGenerator.Create(),
                OperatorRoleName,
                context.TenantId
            )
            {
                IsDefault = true,
                IsPublic = true,
            };
            (await _roleManager.CreateAsync(operatorRole)).CheckErrors();
        }
        else if (!operatorRole.IsDefault || !operatorRole.IsPublic)
        {
            operatorRole.IsDefault = true;
            operatorRole.IsPublic = true;
            (await _roleManager.UpdateAsync(operatorRole)).CheckErrors();
        }

        await _permissionDataSeeder.SeedAsync(
            RolePermissionValueProvider.ProviderName,
            OperatorRoleName,
            new[]
            {
                AuroraStruct3DAccessPermissions.Operation,
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
            },
            context.TenantId
        );
    }
}
