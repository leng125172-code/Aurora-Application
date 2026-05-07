namespace Lion.AbpPro.ImportExportManagement.Permissions
{
    public class ImportExportManagementPermissionDefinitionProvider : PermissionDefinitionProvider
    {
        public override void Define(IPermissionDefinitionContext context)
        {
            var abpIdentityGroup = context.GetGroup(ImportExportManagementPermissions.GroupName);

            var dynamicMenuManagement = abpIdentityGroup.AddPermission(
                ImportExportManagementPermissions.ImportExportManagement.Default,
                L("Permission:ImportExportManagement")
            );
            dynamicMenuManagement.AddChild(
                ImportExportManagementPermissions.ImportExportManagement.Create,
                L("Permission:Create")
            );
        }

        private static LocalizableString L(string name)
        {
            return LocalizableString.Create<ImportExportManagementResource>(name);
        }
    }
}
