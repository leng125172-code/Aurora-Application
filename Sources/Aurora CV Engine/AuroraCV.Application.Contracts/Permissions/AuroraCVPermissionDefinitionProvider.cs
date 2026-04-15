namespace AuroraCV.Permissions
{
    public class AuroraCVPermissionDefinitionProvider : PermissionDefinitionProvider
    {
        public override void Define(IPermissionDefinitionContext context) { }

        private static LocalizableString L(string name)
        {
            return LocalizableString.Create<AuroraCVResource>(name);
        }
    }
}
