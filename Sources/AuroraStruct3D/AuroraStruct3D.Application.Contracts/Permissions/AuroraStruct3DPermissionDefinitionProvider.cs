namespace AuroraStruct3D.Permissions
{
    public class AuroraStruct3DPermissionDefinitionProvider : PermissionDefinitionProvider
    {
        public override void Define(IPermissionDefinitionContext context)
        {
            PermissionGroupDefinition group = context.AddGroup(
                AuroraStruct3DPermissions.GroupName,
                L("Permission:Access")
            );

            group.AddPermission(
                AuroraStruct3DPermissions.Operation,
                L("Permission:Access.Operation")
            );
            group.AddPermission(
                AuroraStruct3DPermissions.Management,
                L("Permission:Access.Management")
            );
        }

        private static LocalizableString L(string name)
        {
            return LocalizableString.Create<AuroraStruct3DResource>(name);
        }
    }
}
