namespace AuroraStruct3D.Permissions
{
    public class AuroraStruct3DPermissionDefinitionProvider : PermissionDefinitionProvider
    {
        public override void Define(IPermissionDefinitionContext context)
        {
           

       
        }

        private static LocalizableString L(string name)
        {
            return LocalizableString.Create<AuroraStruct3DResource>(name);
        }
    }
}