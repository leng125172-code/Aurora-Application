namespace Lion.AbpPro.CacheManagement.Permissions
{
    public class CacheManagementPermissionDefinitionProvider : PermissionDefinitionProvider
    {
        public override void Define(IPermissionDefinitionContext context)
        {
            var abpIdentityGroup = context.GetGroup(CacheManagementPermissions.GroupName);
            var cacheManagement = abpIdentityGroup.AddPermission(
                CacheManagementPermissions.CacheManagement.Default,
                L("Permission:CacheManagement")
            );
            cacheManagement.AddChild(
                CacheManagementPermissions.CacheManagement.Delete,
                L("Permission:Delete")
            );
            cacheManagement.AddChild(
                CacheManagementPermissions.CacheManagement.LookValue,
                L("Permission:LookValue")
            );
        }

        private static LocalizableString L(string name)
        {
            return LocalizableString.Create<CacheManagementResource>(name);
        }
    }
}
