using Microsoft.AspNetCore.Mvc;
using AMS.Models;
using AMS.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
using AMS.Models.Entities;
using Microsoft.AspNetCore.Authorization;

namespace AMS.Controllers
{
    [Authorize(Roles = "Admin")]
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
		[Authorize(Roles = "Admin")]

        public IActionResult Dashboard()
        {
			ViewBag.Username = User.Identity.Name;
            return View();
        }
		public async Task<IActionResult> Users()
		{
		
			var users= await _context.Users.ToListAsync();
			return View(users);
		}


        //=======================================Manage Admin Controllers ========================================//
        //see admins
        public async Task<IActionResult> Admins(DateTime? fromDate, DateTime? toDate, string? status, string? name)
		{

            var query = _context.Admins
        .Include(a => a.User)
        .AsQueryable();
           
            // Apply date filter
            if (fromDate.HasValue)
            {
                query = query.Where(a => a.User.CreatedAt >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                // Include the entire day
                var endDate = toDate.Value.AddDays(1);
                query = query.Where(a => a.User.CreatedAt < endDate);
            }
            if (!string.IsNullOrEmpty(status))
            {
                bool isActive = status.ToLower() == "active";
                query = query.Where(a => a.User.IsActive == isActive);
            }

            var admins = await query.OrderByDescending(a => a.User.CreatedAt).ToListAsync();

            var viewModel = new AdminFilterViewModel
            {
                FromDate = fromDate,
                ToDate = toDate,
                Status = status,
                Admins = admins
            };

            return View(viewModel);
            // Apply status filter
            
        }
		//add admin 
		[HttpGet]
		public IActionResult AddAdmin()
        {
           
			
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> AddAdmin(AddUserViewModel model)
        {
          
            if (!ModelState.IsValid)
            {
				TempData["Error"]= "Invalid data submitted.";

                return View(model);
            }
            //check duplicate email
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == model.Email);
            if (existingUser != null)
			{
				TempData["Error"] = "Email already in use";
                return View(model);

            }
            var user = new User
            {
                Username = model.Username,
                Email = model.Email,
                PasswordHash = model.Password,
                Role = "Admin",
                IsActive = true,
                CreatedAt = DateTime.UtcNow

            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var admin = new Admin
            {
                UserId = user.UserId,   // foreign key link
                FirstName = model.FirstName,
                LastName = model.LastName
            };

            _context.Admins.Add(admin);
            await _context.SaveChangesAsync();
			TempData["Success"] = "The Admin was addedd succesfully";
            return RedirectToAction("Admins");


        }





        [HttpGet]
        //return only the form
        public IActionResult EditAdmin(int UserId)
        {

            if(UserId <= 0)
            {
                TempData["Error"] = "Invalid User Id";
                return View();
            }
            //create admin and return in model

            var admin = _context.Admins
                .Include(a => a.User)
                .FirstOrDefault(a => a.UserId == UserId);


            if (admin == null || admin.User==null)
            {
                TempData["Error"] = "Admin not found";
                return View();
            }
            

            
            
            var viewModel = new AddUserViewModel
            {
                UserId = admin.UserId,
                Username = admin.User.Username,
                Email = admin.User.Email,
                FirstName = admin.FirstName,
                LastName = admin.LastName,
                Role = "Admin"
            };

            return View(viewModel);

        }

        [HttpPost]
        public IActionResult EditAdmin(AddUserViewModel model)
        {
            //remove password for 
            if (string.IsNullOrWhiteSpace(model.Password))
            {
                ModelState.Remove("Password");
            }
            if(!ModelState.IsValid)
            {
                TempData["Error"] = "Invalid data submitted.";
                return View(model);
            }

            var admin = _context.Admins
                .Include(a => a.User)
                .FirstOrDefault(a => a.UserId == model.UserId);
            //model before editing:

           

            if (admin == null || admin.User == null)
            {
                TempData["Error"] = "Admin not found";
                return View(model);
            }
            
            //check for email existing for another user
            var existingUser = _context.Users
                .FirstOrDefault(u => u.Email == model.Email && u.UserId != model.UserId);
            if (existingUser != null)
            {
                TempData["Error"] = "Email already in use by another user.";
                var viewModel = new AddUserViewModel
                {
                    UserId = admin.UserId,
                    Username = admin.User.Username,
                    Email = admin.User.Email,
                    FirstName = admin.FirstName,
                    LastName = admin.LastName,
                    Role = "Admin"
                };
                return View(viewModel);
            }

                //update user details

                admin.User.Username = model.Username;
            admin.User.Email = model.Email;
            if(!string.IsNullOrEmpty(model.Password))
            {
                admin.User.PasswordHash = model.Password;
            }
            //update admin details
            admin.FirstName = model.FirstName;
            admin.LastName = model.LastName;

            _context.SaveChanges();
            TempData["Success"] = "Admin details updated successfully.";
            return RedirectToAction("Admins");
        }

        //Delete admin
        [HttpPost]
        public IActionResult DeleteAdmin(int UserId)
        {
            var admin = _context.Admins
                .Include(a => a.User)
                .FirstOrDefault(a => a.UserId == UserId);

            if (admin == null || admin.User == null)
            {
                TempData["Error"] = "Admin not found";
                return RedirectToAction("Admins");
            }

            // Remove admin and associated user
            _context.Users.Remove(admin.User);
            _context.Admins.Remove(admin);
            _context.SaveChanges();

            TempData["Success"] = "Admin deleted successfully.";
            return RedirectToAction("Admins");
        }




        [HttpGet]
		public IActionResult AddUser()
		{
			
		
			return View();
		}

		[HttpPost]
		public async Task<IActionResult> AddUser(AddUserViewModel model)
		{
			
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
