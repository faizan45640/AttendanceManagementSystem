using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AMS.Controllers
{
    public class StudentController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }


        [Authorize(Roles = "Student")]
        public IActionResult Dashboard()
        {
            if(HttpContext.Session.GetString("Role") != "Student")
            {
                return RedirectToAction("Login", "Auth");
			}
            ViewBag.User = HttpContext.Session.GetString("Username");
            return View();
        }



        [Authorize(Roles =  "Admin") ]
        [HttpGet]
        //filters
        public IActionResult Students(string? username,string? rollNumber, int? userId , int? batchId , int? courseId)
        {


            return View();
        }

        [Authorize(Roles = "Admin   ")]
        [HttpGet]
        public IActionResult AddStudent()
        {
            return View();

        }
    }
}
