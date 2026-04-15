namespace AuroraCV
{
    /* Inherit your application services from this class.
     */
    public abstract class AuroraCVAppService : ApplicationService
    {
        protected AuroraCVAppService()
        {
            LocalizationResource = typeof(AuroraCVResource);
        }
    }
}
