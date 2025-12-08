using Microsoft.AspNetCore.Mvc;

namespace AMS.Controllers
{
    public class AccountController : Controller
    {
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
