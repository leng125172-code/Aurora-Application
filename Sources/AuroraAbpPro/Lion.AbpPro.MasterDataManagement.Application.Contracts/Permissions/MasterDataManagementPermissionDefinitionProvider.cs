namespace Lion.AbpPro.MasterDataManagement.Permissions
{
    public class MasterDataManagementPermissionDefinitionProvider : PermissionDefinitionProvider
    {
        public override void Define(IPermissionDefinitionContext context)
        {
            var group = context.AddGroup(
                MasterDataManagementPermissions.GroupName,
                L("Permission:MasterDataManagement")
            );

            var masterDataManagementDataModel = group.AddPermission(
                MasterDataManagementPermissions.MasterDataManagement.DataModel,
                L("Permission:MasterDataManagement:DataModel")
            );
            masterDataManagementDataModel.AddChild(
                MasterDataManagementPermissions.MasterDataManagement.Create,
                L("Permission:Create")
            );
            masterDataManagementDataModel.AddChild(
                MasterDataManagementPermissions.MasterDataManagement.Update,
                L("Permission:Update")
            );
            masterDataManagementDataModel.AddChild(
                MasterDataManagementPermissions.MasterDataManagement.Delete,
                L("Permission:Delete")
            );

            var masterDataManagementData = group.AddPermission(
                MasterDataManagementPermissions.MasterDataManagement.Data,
                L("Permission:MasterDataManagement:Data")
            );
            masterDataManagementData.AddChild(
                MasterDataManagementPermissions.MasterDataManagement.DataCreate,
                L("Permission:Create")
            );
            masterDataManagementData.AddChild(
                MasterDataManagementPermissions.MasterDataManagement.DataUpdate,
                L("Permission:Update")
            );
        }

        private static LocalizableString L(string name)
        {
            return LocalizableString.Create<MasterDataManagementResource>(name);
        }
    }
}
