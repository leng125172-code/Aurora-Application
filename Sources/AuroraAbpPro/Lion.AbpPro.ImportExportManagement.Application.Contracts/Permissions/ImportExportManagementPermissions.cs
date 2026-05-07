namespace Lion.AbpPro.ImportExportManagement.Permissions
{
    public class ImportExportManagementPermissions
    {
        public const string GroupName = "AbpIdentity";

        public static class ImportExportManagement
        {
            public const string Default = GroupName + ".ImportExportManagement";
            public const string Create = Default + ".Create";
        }

        public static string[] GetAll()
        {
            return ReflectionHelper.GetPublicConstantsRecursively(
                typeof(ImportExportManagementPermissions)
            );
        }
    }
}
