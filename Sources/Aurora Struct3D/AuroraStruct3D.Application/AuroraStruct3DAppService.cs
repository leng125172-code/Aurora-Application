namespace AuroraStruct3D
{
    /* Inherit your application services from this class.
     */
    public abstract class AuroraStruct3DAppService : ApplicationService
    {
        protected AuroraStruct3DAppService()
        {
            LocalizationResource = typeof(AuroraStruct3DResource);
        }
    }
}
