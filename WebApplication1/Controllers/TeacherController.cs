
using AMS.Models;
using AMS.Models.Entities;
using AMS.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AMS.Controllers
{
    public class TeacherController : Controller
    {
        private readonly ApplicationDbContext _context;

        public TeacherController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Teacher/Teachers
        public async Task<IActionResult> Teachers(TeacherFilterViewModel filter)
        {
            var query = _context.Teachers
                .Include(t => t.User)
                .Include(t => t.CourseAssignments)
                .AsQueryable();

            // Apply filters
            if (filter.FromDate.HasValue)
            {
                query = query.Where(t => t.User!.CreatedAt >= filter.FromDate.Value);
            }

            if (filter.ToDate.HasValue)
            {
                query = query.Where(t => t.User!.CreatedAt <= filter.ToDate.Value);
            }

            if (!string.IsNullOrEmpty(filter.Status))
            {
                bool isActive = filter.Status == "Active";
                query = query.Where(t => t.IsActive == isActive);
            }

            filter.Teachers = await query
                .OrderByDescending(t => t.User!.CreatedAt)
                .ToListAsync();

            return View(filter);
        }

        // GET: Teacher/AddTeacher
        public IActionResult AddTeacher()
        {
            return View();
        }

        // POST: Teacher/AddTeacher
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddTeacher(AddTeacherViewModel model)
        {
            if (ModelState.IsValid)
            {
                // Check if username already exists
                var existingUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == model.Username);

                if (existingUser != null)
                {
                    ModelState.AddModelError("Username", "Username is already taken.");
                    return View(model);
                }

                // Check if email already exists
                var existingEmail = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == model.Email);

                if (existingEmail != null)
                {
                    ModelState.AddModelError("Email", "Email is already registered.");
                    return View(model);
                }

                // Create user
                var user = new User
                {
                    Username = model.Username,
                    Email = model.Email,
                    PasswordHash =(model.Password),
                    Role = "Teacher", // Teacher role
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                // Create teacher
                var teacher = new Teacher
                {
                    UserId = user.UserId,
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    IsActive = true
                };

                _context.Teachers.Add(teacher);
                await _context.SaveChangesAsync();

                TempData["success"] = $"Teacher '{model.Username}' has been added successfully!";
                return RedirectToAction(nameof(Teachers));
            }

            return View(model);
        }

        // GET: Teacher/EditTeacher/5
        public async Task<IActionResult> EditTeacher(int? TeacherId)
        {
            if (TeacherId == null)
            {
                return NotFound();
            }

            var teacher = await _context.Teachers
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.TeacherId == TeacherId);

            if (teacher == null || teacher.User == null)
            {
                TempData["error"] = "Teacher not found.";
                return RedirectToAction(nameof(Teachers));
            }

            var model = new EditTeacherViewModel
            {
                TeacherId = teacher.TeacherId,
                UserId = teacher.User.UserId,
                Username = teacher.User.Username,
                Email = teacher.User.Email,
                FirstName = teacher.FirstName ?? string.Empty,
                LastName = teacher.LastName ?? string.Empty,
                IsActive = teacher.IsActive ?? true
            };

            return View(model);
        }

        // POST: Teacher/EditTeacher
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTeacher(EditTeacherViewModel model)
        {
            if (ModelState.IsValid)
            {
                var teacher = await _context.Teachers
                    .Include(t => t.User)
                    .FirstOrDefaultAsync(t => t.TeacherId == model.TeacherId);

                if (teacher == null || teacher.User == null)
                {
                    TempData["error"] = "Teacher not found.";
                    return RedirectToAction(nameof(Teachers));
                }

                // Check if username is already taken by another user
                var existingUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == model.Username && u.UserId != model.UserId);

                if (existingUser != null)
                {
                    ModelState.AddModelError("Username", "Username is already taken.");
                    return View(model);
                }

                // Check if email is already taken by another user
                var existingEmail = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == model.Email && u.UserId != model.UserId);

                if (existingEmail != null)
                {
                    ModelState.AddModelError("Email", "Email is already taken.");
                    return View(model);
                }

                // Update user information
                teacher.User.Username = model.Username;
                teacher.User.Email = model.Email;
                teacher.User.IsActive = model.IsActive;

                // Update password only if provided
                if (!string.IsNullOrWhiteSpace(model.Password))
                {
                    teacher.User.PasswordHash = (model.Password);
                }

                // Update teacher information
                teacher.FirstName = model.FirstName;
                teacher.LastName = model.LastName;
                teacher.IsActive = model.IsActive;

                _context.Update(teacher);
                await _context.SaveChangesAsync();

                TempData["success"] = $"Teacher '{model.Username}' has been updated successfully!";
                return RedirectToAction(nameof(Teachers));
            }

            return View(model);
        }

        // POST: Teacher/DeleteTeacher
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTeacher(int TeacherId)
        {
            var teacher = await _context.Teachers
                .Include(t => t.User)
                .Include(t => t.CourseAssignments)
                .FirstOrDefaultAsync(t => t.TeacherId == TeacherId);

            if (teacher == null)
            {
                TempData["error"] = "Teacher not found.";
                return RedirectToAction(nameof(Teachers));
            }

            // Check if teacher has course assignments
            if (teacher.CourseAssignments.Any())
            {
                TempData["error"] = $"Cannot delete teacher '{teacher.User?.Username}' because they are assigned to {teacher.CourseAssignments.Count} course(s).";
                return RedirectToAction(nameof(Teachers));
            }

            try
            {
                // Store username for success message
                var username = teacher.User?.Username ?? "Teacher";

                // Delete the teacher record first
                _context.Teachers.Remove(teacher);

                // Delete the associated user account
                if (teacher.User != null)
                {
                    _context.Users.Remove(teacher.User);
                }

                await _context.SaveChangesAsync();

                TempData["success"] = $"Teacher '{username}' has been deleted successfully!";
            }
            catch (Exception ex)
            {
                TempData["error"] = $"An error occurred while deleting the teacher: {ex.Message}";
            }

            return RedirectToAction(nameof(Teachers));
        }
    }
}