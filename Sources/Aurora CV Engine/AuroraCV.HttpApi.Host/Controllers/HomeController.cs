namespace AuroraCV.Controllers
{
    public class HomeController : AbpController
    {
        public ActionResult Index()
        {
            return Redirect("/Login");
        }
    }
}
