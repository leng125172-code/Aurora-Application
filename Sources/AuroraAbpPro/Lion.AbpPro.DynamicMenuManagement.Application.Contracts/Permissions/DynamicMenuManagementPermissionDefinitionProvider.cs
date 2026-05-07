namespace Lion.AbpPro.DynamicMenuManagement.Permissions
{
    public class DynamicMenuManagementPermissionDefinitionProvider : PermissionDefinitionProvider
    {
        public override void Define(IPermissionDefinitionContext context)
        {
            var abpIdentityGroup = context.GetGroup(DynamicMenuManagementPermissions.GroupName);

            var dynamicMenuManagement = abpIdentityGroup.AddPermission(
                DynamicMenuManagementPermissions.DynamicMenuManagement.Default,
                L("Permission:DynamicMenuManagement")
            );
            dynamicMenuManagement.AddChild(
                DynamicMenuManagementPermissions.DynamicMenuManagement.Create,
                L("Permission:Create")
            );
            dynamicMenuManagement.AddChild(
                DynamicMenuManagementPermissions.DynamicMenuManagement.Update,
                L("Permission:Update")
            );
            dynamicMenuManagement.AddChild(
                DynamicMenuManagementPermissions.DynamicMenuManagement.Delete,
                L("Permission:Delete")
            );
        }

        private static LocalizableString L(string name)
        {
            return LocalizableString.Create<DynamicMenuManagementResource>(name);
        }
    }
}
