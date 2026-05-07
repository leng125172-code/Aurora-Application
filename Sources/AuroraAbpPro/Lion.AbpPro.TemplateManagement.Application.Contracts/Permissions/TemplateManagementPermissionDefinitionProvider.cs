using Lion.AbpPro.TemplateManagement.Localization;

namespace Lion.AbpPro.TemplateManagement.Permissions
{
    public class TemplateManagementPermissionDefinitionProvider : PermissionDefinitionProvider
    {
        public override void Define(IPermissionDefinitionContext context)
        {
            var group = context.AddGroup(
                TemplateManagementPermissions.GroupName,
                L("Permission:TemplateManagement")
            );

            var codeManagement = group.AddPermission(
                TemplateManagementPermissions.TemplateManagement.Default,
                L("Permission:TemplateManagementList")
            );
            codeManagement.AddChild(
                TemplateManagementPermissions.TemplateManagement.Create,
                L("Permission:Create")
            );
            codeManagement.AddChild(
                TemplateManagementPermissions.TemplateManagement.Update,
                L("Permission:Update")
            );
            codeManagement.AddChild(
                TemplateManagementPermissions.TemplateManagement.Delete,
                L("Permission:Delete")
            );
            codeManagement.AddChild(
                TemplateManagementPermissions.TemplateManagement.Export,
                L("Permission:Export")
            );
        }

        private static LocalizableString L(string name)
        {
            return LocalizableString.Create<TemplateManagementResource>(name);
        }
    }
}
