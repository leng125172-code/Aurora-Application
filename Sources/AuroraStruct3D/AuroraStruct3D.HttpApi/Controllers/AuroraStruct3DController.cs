namespace AuroraStruct3D.Controllers
{
    /* Inherit your controllers from this class.
     */
    public abstract class AuroraStruct3DController : AbpController
    {
        protected AuroraStruct3DController()
        {
            LocalizationResource = typeof(AuroraStruct3DResource);
        }
    }
}