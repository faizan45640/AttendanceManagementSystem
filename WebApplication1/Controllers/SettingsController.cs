using AMS.Models;
using AMS.Models.Entities;
using AMS.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AMS.Controllers
{
    [Authorize]
    public class SettingsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public SettingsController(ApplicationDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        // GET: Settings/Index - Profile View
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Auth");

            var user = await _context.Users.FindAsync(int.Parse(userId));
            if (user == null) return RedirectToAction("Login", "Auth");

            var model = new ProfileViewModel
            {
                Role = user.Role ?? "Unknown",
                Email = user.Email,
                Username = user.Username,
                FullName = user.Username ?? "User" // Default fallback
            };

            if (User.IsInRole("Student"))
            {
                var student = await _context.Students
                    .Include(s => s.Batch)
                    .FirstOrDefaultAsync(s => s.UserId == user.UserId);

                if (student != null)
                {
                    model.FullName = $"{student.FirstName} {student.LastName}".Trim();

                    // Get current semester
                    var today = DateOnly.FromDateTime(DateTime.Now);
                    var currentSemester = await _context.Semesters
                        .FirstOrDefaultAsync(s => s.StartDate <= today && s.EndDate >= today && s.IsActive);

                    // Get enrollment count
                    var enrollmentCount = await _context.Enrollments
                        .CountAsync(e => e.StudentId == student.StudentId && e.Status == "Active");

                    // Calculate overall attendance
                    var attendances = await _context.Attendances
                        .Where(a => a.StudentId == student.StudentId)
                        .ToListAsync();
                    var totalClasses = attendances.Count;
                    var presentClasses = attendances.Count(a => a.Status == "Present" || a.Status == "Late");
                    var overallAttendance = totalClasses > 0 ? (double)presentClasses / totalClasses * 100 : 0;

                    model.StudentData = new StudentProfileData
                    {
                        StudentId = student.StudentId,
                        RollNumber = student.RollNumber,
                        BatchName = student.Batch?.BatchName,
                        CurrentSemester = currentSemester?.SemesterName,
                        TotalCourses = enrollmentCount,
                        OverallAttendance = Math.Round(overallAttendance, 1)
                    };
                }
            }
            else if (User.IsInRole("Teacher"))
            {
                var teacher = await _context.Teachers
                    .FirstOrDefaultAsync(t => t.UserId == user.UserId);

                if (teacher != null)
                {
                    model.FullName = $"{teacher.FirstName} {teacher.LastName}".Trim();

                    var courseCount = await _context.CourseAssignments
                        .Where(ca => ca.TeacherId == teacher.TeacherId && ca.IsActive == true)
                        .Select(ca => ca.CourseId)
                        .Distinct()
                        .CountAsync();

                    var batchCount = await _context.CourseAssignments
                        .Where(ca => ca.TeacherId == teacher.TeacherId && ca.IsActive == true)
                        .Select(ca => ca.BatchId)
                        .Distinct()
                        .CountAsync();

                    model.TeacherData = new TeacherProfileData
                    {
                        TeacherId = teacher.TeacherId,
                        TotalCourses = courseCount,
                        TotalBatches = batchCount
                    };
                }
            }
            else if (User.IsInRole("Admin"))
            {
                var admin = await _context.Admins
                    .FirstOrDefaultAsync(a => a.UserId == user.UserId);

                if (admin != null)
                {
                    model.FullName = $"{admin.FirstName} {admin.LastName}".Trim();

                    model.AdminData = new AdminProfileData
                    {
                        AdminId = admin.AdminId,
                        TotalStudents = await _context.Students.CountAsync(s => s.IsActive == true),
                        TotalTeachers = await _context.Teachers.CountAsync(t => t.IsActive == true),
                        TotalBatches = await _context.Batches.CountAsync(b => b.IsActive)
                    };
                }
            }

            return View(model);
        }

        // GET: Settings/Institution (Admin Only)
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Institution()
        {
            var model = new InstitutionSettingsViewModel
            {
                InstitutionName = await GetSettingAsync("InstitutionName") ?? "AMS",
                CurrentLogoPath = await GetSettingAsync("InstitutionLogo"),
                Address = await GetSettingAsync("InstitutionAddress"),
                Phone = await GetSettingAsync("InstitutionPhone"),
                Email = await GetSettingAsync("InstitutionEmail"),
                AcademicYear = await GetSettingAsync("CurrentAcademicYear")
            };

            return View(model);
        }

        // POST: Settings/Institution
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Institution(InstitutionSettingsViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var userId = int.Parse(User.FindFirstValue("UserId") ?? "0");

            // Handle logo upload
            if (model.LogoFile != null && model.LogoFile.Length > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".svg" };
                var extension = Path.GetExtension(model.LogoFile.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(extension))
                {
                    ModelState.AddModelError("LogoFile", "Only image files (jpg, jpeg, png, gif, svg) are allowed.");
                    model.CurrentLogoPath = await GetSettingAsync("InstitutionLogo");
                    return View(model);
                }

                if (model.LogoFile.Length > 2 * 1024 * 1024) // 2MB limit
                {
                    ModelState.AddModelError("LogoFile", "File size must be less than 2MB.");
                    model.CurrentLogoPath = await GetSettingAsync("InstitutionLogo");
                    return View(model);
                }

                // Delete old logo if exists
                var oldLogo = await GetSettingAsync("InstitutionLogo");
                if (!string.IsNullOrEmpty(oldLogo))
                {
                    var oldPath = Path.Combine(_environment.WebRootPath, oldLogo.TrimStart('/'));
                    if (System.IO.File.Exists(oldPath))
                    {
                        System.IO.File.Delete(oldPath);
                    }
                }

                // Save new logo
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "branding");
                Directory.CreateDirectory(uploadsFolder);

                var uniqueFileName = $"logo_{DateTime.Now:yyyyMMddHHmmss}{extension}";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await model.LogoFile.CopyToAsync(stream);
                }

                await SetSettingAsync("InstitutionLogo", $"/uploads/branding/{uniqueFileName}", userId);
            }

            // Save other settings
            await SetSettingAsync("InstitutionName", model.InstitutionName, userId);
            await SetSettingAsync("InstitutionAddress", model.Address, userId);
            await SetSettingAsync("InstitutionPhone", model.Phone, userId);
            await SetSettingAsync("InstitutionEmail", model.Email, userId);
            await SetSettingAsync("CurrentAcademicYear", model.AcademicYear, userId);

            TempData["success"] = "Institution settings updated successfully.";
            return RedirectToAction(nameof(Institution));
        }

        // GET: Settings/ChangePassword
        public IActionResult ChangePassword()
        {
            return View(new ChangePasswordViewModel());
        }

        // POST: Settings/ChangePassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Auth");

            var user = await _context.Users.FindAsync(int.Parse(userId));
            if (user == null) return RedirectToAction("Login", "Auth");

            // Verify current password (plain text comparison)
            if (model.CurrentPassword != user.PasswordHash)
            {
                ModelState.AddModelError("CurrentPassword", "Current password is incorrect.");
                return View(model);
            }

            // Update password (plain text)
            user.PasswordHash = model.NewPassword;
            await _context.SaveChangesAsync();

            TempData["success"] = "Password changed successfully.";
            return RedirectToAction(nameof(Index));
        }

        // Helper methods for settings
        private async Task<string?> GetSettingAsync(string key)
        {
            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.SettingKey == key);
            return setting?.SettingValue;
        }

        private async Task SetSettingAsync(string key, string? value, int? updatedBy = null)
        {
            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.SettingKey == key);

            if (setting != null)
            {
                setting.SettingValue = value;
                setting.UpdatedAt = DateTime.Now;
                setting.UpdatedBy = updatedBy;
            }
            else
            {
                _context.SystemSettings.Add(new SystemSetting
                {
                    SettingKey = key,
                    SettingValue = value,
                    SettingType = "string",
                    Category = "General",
                    IsEditable = true,
                    UpdatedAt = DateTime.Now,
                    UpdatedBy = updatedBy
                });
            }

            await _context.SaveChangesAsync();
        }
    }
}
