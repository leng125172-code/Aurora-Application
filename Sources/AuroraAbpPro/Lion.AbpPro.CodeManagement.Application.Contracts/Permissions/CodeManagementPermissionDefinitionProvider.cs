namespace Lion.AbpPro.CodeManagement.Permissions
{
    public class CodeManagementPermissionDefinitionProvider : PermissionDefinitionProvider
    {
        public override void Define(IPermissionDefinitionContext context)
        {
            var group = context.AddGroup(
                CodeManagementPermissions.GroupName,
                L("Permission:CodeManagement")
            );

            var codeManagement = group.AddPermission(
                CodeManagementPermissions.CodeManagement.Project.Default,
                L("Permission:ProjectManagement")
            );
            codeManagement.AddChild(
                CodeManagementPermissions.CodeManagement.Project.Create,
                L("Permission:Create")
            );
            codeManagement.AddChild(
                CodeManagementPermissions.CodeManagement.Project.Update,
                L("Permission:Update")
            );
            codeManagement.AddChild(
                CodeManagementPermissions.CodeManagement.Project.Delete,
                L("Permission:Delete")
            );
            codeManagement.AddChild(
                CodeManagementPermissions.CodeManagement.Project.Model,
                L("Permission:Model")
            );

            var templateManagement = group.AddPermission(
                CodeManagementPermissions.CodeManagement.Template.Default,
                L("Permission:TemplateManagement")
            );
            templateManagement.AddChild(
                CodeManagementPermissions.CodeManagement.Template.Create,
                L("Permission:Create")
            );
            templateManagement.AddChild(
                CodeManagementPermissions.CodeManagement.Template.Update,
                L("Permission:Update")
            );
            templateManagement.AddChild(
                CodeManagementPermissions.CodeManagement.Template.Delete,
                L("Permission:Delete")
            );
            templateManagement.AddChild(
                CodeManagementPermissions.CodeManagement.Template.Copy,
                L("Permission:Copy")
            );

            var generatorManagement = group.AddPermission(
                CodeManagementPermissions.CodeManagement.Generator.Default,
                L("Permission:GeneratorManagement")
            );
            generatorManagement.AddChild(
                CodeManagementPermissions.CodeManagement.Generator.Preview,
                L("Permission:Preview")
            );
            generatorManagement.AddChild(
                CodeManagementPermissions.CodeManagement.Generator.Download,
                L("Permission:Download")
            );
        }

        private static LocalizableString L(string name)
        {
            return LocalizableString.Create<CodeManagementResource>(name);
        }
    }
}
