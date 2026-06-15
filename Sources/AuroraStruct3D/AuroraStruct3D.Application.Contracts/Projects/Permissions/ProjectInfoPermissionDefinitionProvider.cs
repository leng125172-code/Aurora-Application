namespace AuroraStruct3D.Permissions;

/// <summary>
/// 项目管理权限定义提供程序。
/// 向 ABP 权限系统注册项目管理的所有权限项。
/// </summary>
public class ProjectInfoPermissionDefinitionProvider : PermissionDefinitionProvider
{
    /// <inheritdoc/>
    public override void Define(IPermissionDefinitionContext context)
    {
        PermissionGroupDefinition group = context.AddGroup(
            ProjectInfoPermissions.GroupName,
            L("Permission:ProjectInfo")
        );

        PermissionDefinition defaultPermission = group.AddPermission(
            ProjectInfoPermissions.Default,
            L("Permission:ProjectInfo.Default")
        );

        defaultPermission.AddChild(
            ProjectInfoPermissions.Create,
            L("Permission:ProjectInfo.Create")
        );

        defaultPermission.AddChild(
            ProjectInfoPermissions.Update,
            L("Permission:ProjectInfo.Update")
        );

        defaultPermission.AddChild(
            ProjectInfoPermissions.ChangeStatus,
            L("Permission:ProjectInfo.ChangeStatus")
        );

        defaultPermission.AddChild(
            ProjectInfoPermissions.Delete,
            L("Permission:ProjectInfo.Delete")
        );
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<AuroraStruct3DResource>(name);
    }
}
