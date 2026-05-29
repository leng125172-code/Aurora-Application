using AuroraStruct3D.ProductModels;

namespace AuroraStruct3D.Permissions;

/// <summary>
/// 产品三维数模权限定义提供程序。
/// 向 ABP 权限系统注册数模管理的所有权限项。
/// </summary>
public class ProductModelPermissionDefinitionProvider : PermissionDefinitionProvider
{
    /// <inheritdoc/>
    public override void Define(IPermissionDefinitionContext context)
    {
        PermissionGroupDefinition group = context.AddGroup(
            ProductModelPermissions.GroupName,
            L("Permission:ProductModel")
        );

        PermissionDefinition defaultPermission = group.AddPermission(
            ProductModelPermissions.Default,
            L("Permission:ProductModel.Default")
        );

        defaultPermission.AddChild(
            ProductModelPermissions.Upload,
            L("Permission:ProductModel.Upload")
        );

        defaultPermission.AddChild(
            ProductModelPermissions.Rename,
            L("Permission:ProductModel.Rename")
        );

        defaultPermission.AddChild(
            ProductModelPermissions.Delete,
            L("Permission:ProductModel.Delete")
        );

        defaultPermission.AddChild(
            ProductModelPermissions.Download,
            L("Permission:ProductModel.Download")
        );

        defaultPermission.AddChild(
            ProductModelPermissions.RetryConversion,
            L("Permission:ProductModel.RetryConversion")
        );

        defaultPermission.AddChild(
            ProductModelPermissions.CleanUp,
            L("Permission:ProductModel.CleanUp")
        );
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<AuroraStruct3DResource>(name);
    }
}
