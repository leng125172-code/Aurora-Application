namespace Lion.AbpPro.MasterDataManagement.Permissions
{
    public class MasterDataManagementPermissions
    {
        public const string GroupName = "AbpMasterDataManagement";

        public static class MasterDataManagement
        {
            public const string Data = GroupName + ".Data";
            public const string DataCreate = Data + ".Create";
            public const string DataUpdate = Data + ".Update";

            public const string DataModel = GroupName + ".DataModel";
            public const string Create = DataModel + ".Create";
            public const string Update = DataModel + ".Update";
            public const string Delete = DataModel + ".Delete";
        }

        public static string[] GetAll()
        {
            return ReflectionHelper.GetPublicConstantsRecursively(
                typeof(MasterDataManagementPermissions)
            );
        }
    }
}
