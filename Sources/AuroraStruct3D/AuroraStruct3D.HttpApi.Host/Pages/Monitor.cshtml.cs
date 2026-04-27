using Lion.AbpPro.AspNetCore.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AuroraStruct3D.Pages
{
    /// <summary>
    /// 监控面板页面
    /// </summary>
    public class Monitor : PageModel
    {
        private readonly AbpProCookieOptions _abpProCookieOptions;

        public Monitor(IOptions<AbpProCookieOptions> abpProCookieOptions)
        {
            _abpProCookieOptions = abpProCookieOptions.Value;
        }

        /// <summary>
        /// 页面加载时检查用户是否已登录
        /// </summary>
        public IActionResult OnGet()
        {
            var authCookie = Request.Cookies[_abpProCookieOptions.Name];
            if (string.IsNullOrWhiteSpace(authCookie))
            {
                return Redirect("/Login");
            }

            return Page();
        }

        /// <summary>
        /// 退出登录，清除认证 Cookie 后跳转到登录页
        /// </summary>
        public IActionResult OnPostLogout()
        {
            Response.Cookies.Delete(_abpProCookieOptions.Name);
            return Redirect("/Login");
        }
    }
}
