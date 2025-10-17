using Microsoft.AspNetCore.Mvc;

namespace AMS.Controllers
{
    public class StudentController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Dashboard()
        {
            if(HttpContext.Session.GetString("Role") != "Student")
            {
                return RedirectToAction("Login", "Auth");
			}
            ViewBag.User = HttpContext.Session.GetString("Username");
            return View();
        }
    }
}
