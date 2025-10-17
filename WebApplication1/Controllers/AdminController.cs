using Microsoft.AspNetCore.Mvc;
using AMS.Models;
using AMS.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
using AMS.Models.Entities;

namespace AMS.Controllers
{
    public class AdminController : Controller
    {

		private readonly ApplicationDbContext _context;
		public AdminController(ApplicationDbContext context)
		{
			_context = context;
		}
		private bool IsAdmin()
		{
			return HttpContext.Session.GetString("Role") == "Admin";
		}
		public IActionResult Dashboard()
        {
			if (HttpContext.Session.GetString("Role") != "Admin")
			{
				return RedirectToAction("Login", "Auth");
			}
			ViewBag.User= HttpContext.Session.GetString("Username");
            return View();
        }
		public async Task<IActionResult> Users()
		{
			if (!IsAdmin())
			{
				return RedirectToAction("Login", "Auth");
			}
			var users= await _context.Users.ToListAsync();
			return View(users);
		}
		[HttpGet]
		public IActionResult AddUser()
		{
			if(!IsAdmin())
			{
				return RedirectToAction("Login", "Auth");
			}
			return View();
		}

		[HttpPost]
		public async Task<IActionResult> AddUser(AddUserViewModel model)
		{
			if (!IsAdmin())
			{
				return RedirectToAction("Login", "Auth");
			}
			if (!ModelState.IsValid)
			{
				return View(model);
			}
            //check duplicate email
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == model.Email);
            if (existingUser != null)
			{
                ModelState.AddModelError("Email", "Email already in use");
                return View(model);
            }
                var user = new User
			{
				Username = model.Username,
				Email = model.Email,
				PasswordHash = model.Password,
				Role = model.Role,
				IsActive = true,
				CreatedAt = DateTime.UtcNow

			};
			_context.Users.Add(user);
			await _context.SaveChangesAsync();
            ViewBag.Success = "User added successfully";
            return RedirectToAction("Users");
		}

	}
}
