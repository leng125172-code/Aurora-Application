namespace AuroraCV.Controllers
{
    /* Inherit your controllers from this class.
     */
    public abstract class AuroraCVController : AbpController
    {
        protected AuroraCVController()
        {
            LocalizationResource = typeof(AuroraCVResource);
        }
    }
}
