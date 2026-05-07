namespace Lion.AbpPro.CacheManagement.Permissions
{
    public class CacheManagementPermissions
    {
        public const string GroupName = "AbpIdentity";

        public class CacheManagement
        {
            public const string Default = "AbpIdentity.CacheManagement";

            public const string Delete = "AbpIdentity.CacheManagement.Delete";

            public const string LookValue = "AbpIdentity.CacheManagement.LookValue";
        }

        public static string[] GetAll()
        {
            return ReflectionHelper.GetPublicConstantsRecursively(
                typeof(CacheManagementPermissions)
            );
        }
    }
}
