namespace Lion.AbpPro.TemplateManagement.Permissions
{
    public class TemplateManagementPermissions
    {
        public const string GroupName = "AbpTemplateManagement";

        public static class TemplateManagement
        {
            public const string Default = GroupName + ".Template";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
            public const string Export = Default + ".Export";
        }

        public static string[] GetAll()
        {
            return ReflectionHelper.GetPublicConstantsRecursively(
                typeof(TemplateManagementPermissions)
            );
        }
    }
}
