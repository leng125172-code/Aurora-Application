namespace Lion.AbpPro.DynamicMenuManagement.Permissions
{
    public class DynamicMenuManagementPermissions
    {
        public const string GroupName = "AbpIdentity";

        public static class DynamicMenuManagement
        {
            public const string Default = GroupName + ".DynamicMenuManagement";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
        }

        public static string[] GetAll()
        {
            return ReflectionHelper.GetPublicConstantsRecursively(
                typeof(DynamicMenuManagementPermissions)
            );
        }
    }
}
