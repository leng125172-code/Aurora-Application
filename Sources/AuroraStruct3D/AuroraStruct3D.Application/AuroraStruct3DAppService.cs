namespace AuroraStruct3D
{
    /* Inherit your application services from this class.
     */
    [Authorize(AuroraStruct3D.Permissions.AuroraStruct3DAccessPermissions.Operation)]
    public abstract class AuroraStruct3DAppService : ApplicationService
    {
        protected AuroraStruct3DAppService()
        {
            LocalizationResource = typeof(AuroraStruct3DResource);
        }
    }
}
