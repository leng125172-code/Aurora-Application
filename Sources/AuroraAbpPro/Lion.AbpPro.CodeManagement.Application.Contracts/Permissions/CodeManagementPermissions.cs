namespace Lion.AbpPro.CodeManagement.Permissions
{
    public class CodeManagementPermissions
    {
        public const string GroupName = "AbpCodeManagement";

        public static class CodeManagement
        {
            public static class Project
            {
                public const string Default = GroupName + ".Project";
                public const string Create = Default + ".Create";
                public const string Update = Default + ".Update";
                public const string Delete = Default + ".Delete";
                public const string Model = Default + ".Model";
            }

            public static class Template
            {
                public const string Default = GroupName + ".Template";
                public const string Create = Default + ".Create";
                public const string Update = Default + ".Update";
                public const string Delete = Default + ".Delete";
                public const string Copy = Default + ".Copy";
            }

            public static class Generator
            {
                public const string Default = GroupName + ".Generator";
                public const string Preview = Default + ".Preview";
                public const string Download = Default + ".Download";
            }
        }

        public static string[] GetAll()
        {
            return ReflectionHelper.GetPublicConstantsRecursively(
                typeof(CodeManagementPermissions)
            );
        }
    }
}
