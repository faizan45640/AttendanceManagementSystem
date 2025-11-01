using AMS.Models;
using AMS.Models.ViewModels;
using AMS.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AMS.Controllers
{
    public class StudentController : Controller
    {

        private readonly ApplicationDbContext _context;
        public StudentController(ApplicationDbContext context)
        {
            _context = context;
        }
        public IActionResult Index()
        {
            return View();
        }


        [Authorize(Roles = "Student")]
        public IActionResult Dashboard()
        {
            if (HttpContext.Session.GetString("Role") != "Student")
            {
                return RedirectToAction("Login", "Auth");
            }
            ViewBag.User = HttpContext.Session.GetString("Username");
            return View();
        }



        [Authorize(Roles = "Admin")]
        [HttpGet]
        //filters
        public async Task<IActionResult> Students(string? username, string? rollNumber, int? userId, int? batchId, int? courseId)
        {
            //RETURN a list of all students 
            var students = await _context.Students
        .Include(s => s.Batch)        // ← Load Batch
        .Include(s => s.User)         // ← Load User
        .Where(s => s.IsActive == true) // or your filter
        .ToListAsync();

            var viewModel = new StudentFilterViewModel
            {
                Students = students
            };

            return View(viewModel);
        }

        [Authorize(Roles = "Admin   ")]
        [HttpGet]
        public IActionResult AddStudent()
        { //insert batches in viewbag
            ViewBag.Batches = _context.Batches
          .Where(b => b.IsActive)
          .Select(b => new SelectListItem
          {
              Value = b.BatchId.ToString(),
              Text = $"{b.BatchName} {b.Year}"
          })
          .ToList();


            return View();

        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        public IActionResult AddStudent(AddStudentViewModel model)
        {

            if (!ModelState.IsValid)
            {
                ViewBag.Error = "Invalid data provided.";
                return View(model);
            }
            //check for email, roll number or username duplicates here
            var existingUser = _context.Users.FirstOrDefault(u => u.Email == model.Email || u.Username == model.Username);
            if (existingUser != null)
            {
                ViewBag.Error = "A user with the same email or username already exists.";
                return View(model);
            }
            var existingStudent = _context.Students.FirstOrDefault(s => s.RollNumber == model.RollNumber);
            if (existingStudent != null)
            {
                ViewBag.Error = "A student with the same roll number already exists.";
                return View(model);
            }
            //create user
            var user = new User
            {
                Username = model.Username,
                Email = model.Email,
                PasswordHash = model.Password,
                Role = "Student",
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            _context.Users.Add(user);
            _context.SaveChanges();
            //create student
            var student = new Student
            {
                UserId = user.UserId,
                RollNumber = model.RollNumber,
                FirstName = model.FirstName,
                LastName = model.LastName,
                BatchId = model.BatchId,
                IsActive = true
            };
            _context.Students.Add(student);
            _context.SaveChanges();

            ViewData["success"] = "Student added successfully";

            return RedirectToAction("Students");

        }


        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> EditStudent(int? StudentId)
        {
            if (StudentId == null)
            {
                return NotFound();
            }

            var student = await _context.Students
                .Include(s => s.User)
                .Include(s => s.Batch)
                .FirstOrDefaultAsync(s => s.StudentId == StudentId);

            if (student == null || student.User == null)
            {
                TempData["error"] = "Student not found.";
                return RedirectToAction("Students");
            }

            var model = new EditStudentViewModel
            {
                StudentId = student.StudentId,
                UserId = student.User.UserId,
                Username = student.User.Username,
                Email = student.User.Email,
                RollNumber = student.RollNumber ?? string.Empty,
                FirstName = student.FirstName ?? string.Empty,
                LastName = student.LastName ?? string.Empty,
                BatchId = student.BatchId ?? 0,
                IsActive = student.IsActive ?? true
            };
            ViewBag.Batches = await _context.Batches
       .Where(b => b.IsActive)
       .Select(b => new SelectListItem
       {
           Value = b.BatchId.ToString(),
           Text = $"{b.BatchName} - {b.Year}"
       })
       .ToListAsync();

            return View(model);
        }


        //edit post request

        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> EditStudent(EditStudentViewModel model)
        {
            if (ModelState.IsValid)
            {
                var student = await _context.Students
                    .Include(s => s.User)
                    .FirstOrDefaultAsync(s => s.StudentId == model.StudentId);

                if (student == null || student.User == null)
                {
                    TempData["error"] = "Student not found.";
                    return RedirectToAction(nameof(Students));
                }

                // Check if username is already taken by another user
                var existingUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == model.Username && u.UserId != model.UserId);

                if (existingUser != null)
                {
                    ModelState.AddModelError("Username", "Username is already taken.");

                    // Reload batches for dropdown
                    ViewBag.Batches = await _context.Batches
                        .Where(b => b.IsActive)
                        .Select(b => new SelectListItem
                        {
                            Value = b.BatchId.ToString(),
                            Text = $"{b.BatchName} - {b.Year}"
                        })
                        .ToListAsync();

                    return View(model);
                }
                var existingEmail = await _context.Users
           .FirstOrDefaultAsync(u => u.Email == model.Email && u.UserId != model.UserId);

                if (existingEmail != null)
                {
                    ModelState.AddModelError("Email", "Email is already taken.");

                    // Reload batches for dropdown
                    ViewBag.Batches = await _context.Batches
                        .Where(b => b.IsActive)
                        .Select(b => new SelectListItem
                        {
                            Value = b.BatchId.ToString(),
                            Text = $"{b.BatchName} - {b.Year}"
                        })
                        .ToListAsync();

                    return View(model);
                }
                var existingRollNumber = await _context.Students
                            .FirstOrDefaultAsync(s => s.RollNumber == model.RollNumber && s.StudentId != model.StudentId);

                if (existingRollNumber != null)
                {
                    ModelState.AddModelError("RollNumber", "Roll number is already taken.");

                    // Reload batches for dropdown
                    ViewBag.Batches = await _context.Batches
                        .Where(b => b.IsActive)
                        .Select(b => new SelectListItem
                        {
                            Value = b.BatchId.ToString(),
                            Text = $"{b.BatchName} - {b.Year}"
                        })
                        .ToListAsync();

                    return View(model);
                }
                student.User.Username = model.Username;
                student.User.Email = model.Email;
                student.User.IsActive = model.IsActive;

                // Update password only if provided
                if (!string.IsNullOrWhiteSpace(model.Password))
                {
                    student.User.PasswordHash = model.Password;
                }

                // Update student information
                student.RollNumber = model.RollNumber;
                student.FirstName = model.FirstName;
                student.LastName = model.LastName;
                student.BatchId = model.BatchId;
                student.IsActive = model.IsActive;

                _context.Update(student);
                await _context.SaveChangesAsync();
                TempData["success"] = $"Student '{model.Username}' has been updated successfully!";
                return RedirectToAction("Students");
            }

            // Reload batches for dropdown
            ViewBag.Batches = await _context.Batches
                .Where(b => b.IsActive)
                .Select(b => new SelectListItem
                {
                    Value = b.BatchId.ToString(),
                    Text = $"{b.BatchName} - {b.Year}"
                })
                .ToListAsync();

            return View(model);
        }


        [Authorize(Roles="Admin")]
        [HttpPost]
        public async Task<IActionResult> DeleteStudent(int StudentId)
        {
            var student = await _context.Students
                .Include(s => s.User)
                .Include(s => s.Attendances)
                .Include(s => s.Enrollments)
                .FirstOrDefaultAsync(s => s.StudentId == StudentId);

            if (student == null)
            {
                TempData["error"] = "Student not found.";
                return RedirectToAction(nameof(Students));
            }

            // Check if student has attendance records
            if (student.Attendances.Any())
            {
                TempData["error"] = $"Cannot delete student '{student.User?.Username}' because they have {student.Attendances.Count} attendance record(s).";
                return RedirectToAction(nameof(Students));
            }
            // Check if student has enrollments
            if (student.Enrollments.Any())
            {
                TempData["error"] = $"Cannot delete student '{student.User?.Username}' because they are enrolled in {student.Enrollments.Count} course(s).";
                return RedirectToAction(nameof(Students));
            }

            try
            {
                // Store username for success message
                var username = student.User?.Username ?? "Student";

                // Delete the student record first
                _context.Students.Remove(student);

                // Delete the associated user account
                if (student.User != null)
                {
                    _context.Users.Remove(student.User);
                }

                await _context.SaveChangesAsync();
                TempData["success"] = $"Student '{username}' has been deleted successfully!";
            }
            catch (Exception ex)
            {
                TempData["error"] = $"An error occurred while deleting the student: {ex.Message}";
            }

            return RedirectToAction("Students");
        }

    }
}