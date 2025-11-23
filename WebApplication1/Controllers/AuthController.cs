using Microsoft.AspNetCore.Mvc;
using AMS.Models;
using AMS.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;


namespace AMS.Controllers
{
    public class AuthController : Controller
    {
        private readonly ApplicationDbContext _context;
        public AuthController(ApplicationDbContext context)
        {
            _context = context;
        }
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }
        

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == model.Email && u.PasswordHash == model.Password);
            if (user == null)
            {
                ViewBag.Error = "Invalid Email or Password";
                return View(model);
            }
           var claims = new List<System.Security.Claims.Claim>
           {
                new System.Security.Claims.Claim("UserId", user.UserId.ToString()),
                new System.Security.Claims.Claim(ClaimTypes.Name, user.Username),
                new System.Security.Claims.Claim(ClaimTypes.Role, user.Role)
            };
            var identity = new System.Security.Claims.ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(
          CookieAuthenticationDefaults.AuthenticationScheme,
          new ClaimsPrincipal(identity)
      );
            
            return user.Role switch
            {
                "Admin" => RedirectToAction("Dashboard", "Admin"),
                "Teacher" => RedirectToAction("Dashboard", "TeacherPortal"),
                "Student" => RedirectToAction("Dashboard", "StudentPortal"),
                _ => RedirectToAction("Login")
            };
        }
            public IActionResult Logout()
            {
            HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Auth");

        }

        }
    }
