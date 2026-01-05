using AMS.Data;
using AMS.Models;
using AMS.Models.Entities;
using AMS.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AMS.Controllers
{
    [Authorize(Roles = "Admin")]
    public class StudentController : Controller
    {
        private readonly ApplicationDbContext _context;

        public StudentController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Student/Students
        [HttpGet]
        public async Task<IActionResult> Students(StudentFilterViewModel filter)
        {
            filter.Page = filter.Page < 1 ? 1 : filter.Page;
            filter.PageSize = filter.PageSize <= 0 ? 20 : filter.PageSize;
            filter.PageSize = Math.Clamp(filter.PageSize, 10, 100);

            var query = _context.Students
                .Include(s => s.Batch)
                .Include(s => s.User)
                .Include(s => s.Attendances)
                .Include(s => s.Enrollments)
                .AsNoTracking()
                .AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(filter.Name))
            {
                query = query.Where(s =>
                    (s.FirstName != null && s.FirstName.Contains(filter.Name)) ||
                    (s.LastName != null && s.LastName.Contains(filter.Name)) ||
                    (s.User != null && s.User.Username != null && s.User.Username.Contains(filter.Name)));
            }

            if (!string.IsNullOrEmpty(filter.RollNumber))
            {
                query = query.Where(s => s.RollNumber != null && s.RollNumber.Contains(filter.RollNumber));
            }

            if (filter.BatchId.HasValue && filter.BatchId.Value > 0)
            {
                query = query.Where(s => s.BatchId == filter.BatchId.Value);
            }

            if (!string.IsNullOrEmpty(filter.Status))
            {
                bool isActive = filter.Status == "Active";
                query = query.Where(s => s.IsActive == isActive);
            }

            filter.TotalCount = await query.CountAsync();
            if (filter.TotalPages > 0 && filter.Page > filter.TotalPages)
            {
                filter.Page = filter.TotalPages;
            }

            filter.Students = await query
                .OrderBy(s => s.RollNumber)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();

            // Load batches for dropdown
            filter.Batches = await _context.Batches
                .Select(b => new SelectListItem
                {
                    Value = b.BatchId.ToString(),
                    Text = $"{b.BatchName} ({b.Year})"
                })
                .ToListAsync();

            return View(filter);
        }

        // GET: Student/AddStudent
        public async Task<IActionResult> AddStudent()
        {
            var batches = await _context.Batches
                .Where(b => b.IsActive == true)
                .Select(b => new SelectListItem
                {
                    Value = b.BatchId.ToString(),
                    Text = $"{b.BatchName} ({b.Year})"
                })
                .ToListAsync();

            ViewBag.Batches = batches;
            return View();
        }

        // POST: Student/AddStudent
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddStudent(Student model, string Email, string Username, string Password)
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(Email))
            {
                ModelState.AddModelError("Email", "Email is required.");
            }
            if (string.IsNullOrWhiteSpace(Username))
            {
                ModelState.AddModelError("Username", "Username is required.");
            }
            if (string.IsNullOrWhiteSpace(Password))
            {
                ModelState.AddModelError("Password", "Password is required.");
            }

            if (ModelState.IsValid)
            {
                // Check if roll number already exists
                var existingStudent = await _context.Students
                    .FirstOrDefaultAsync(s => s.RollNumber == model.RollNumber);

                if (existingStudent != null)
                {
                    return Conflict(new { success = false, message = "A student with this roll number already exists." });
                }

                // Check if email already exists
                var existingUserByEmail = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == Email);

                if (existingUserByEmail != null)
                {
                    return Conflict(new { success = false, message = "A user with this email already exists." });
                }

                // Check if username already exists
                var existingUserByUsername = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == Username);

                if (existingUserByUsername != null)
                {
                    return Conflict(new { success = false, message = "A user with this username already exists." });
                }

                // Create User record first
                var user = new User
                {
                    Username = Username,
                    Email = Email,
                    Role = "Student",
                    IsActive = true,
                    CreatedAt = DateTime.Now,
                    PasswordHash = Password
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                // Create Student record with UserId
                model.UserId = user.UserId;
                model.IsActive = model.IsActive ?? true;
                _context.Students.Add(model);
                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = $"Student '{model.FirstName} {model.LastName}' has been added successfully!" });
            }

            var errors = ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray()
                );
            return BadRequest(new { success = false, message = "Invalid data submitted.", errors });
        }

        // GET: Student/EditStudent/5
        public async Task<IActionResult> EditStudent(int? StudentId)
        {
            if (StudentId == null)
            {
                return NotFound();
            }

            var student = await _context.Students
                .Include(s => s.Batch)
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.StudentId == StudentId);

            if (student == null)
            {
                return NotFound();
            }

            var batches = await _context.Batches
                .Where(b => b.IsActive == true)
                .Select(b => new SelectListItem
                {
                    Value = b.BatchId.ToString(),
                    Text = $"{b.BatchName} ({b.Year})"
                })
                .ToListAsync();
            ViewBag.Batches = batches;

            // Pass User data to View via ViewBag or separate ViewModel if preferred, 
            // but here the View binds to Student which has User navigation property.
            // We'll rely on Student.User being populated.

            return View(student);
        }

        // POST: Student/EditStudent/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditStudent(int StudentId, Student model, string Email, string Username, string Password)
        {
            if (StudentId != model.StudentId)
            {
                return NotFound(new { success = false, message = "Student not found." });
            }

            // Manually remove User validation errors if they come from the Student.User path which might be incomplete
            ModelState.Remove("User"); 

            if (ModelState.IsValid)
            {
                try
                {
                    var studentToUpdate = await _context.Students
                        .Include(s => s.User)
                        .FirstOrDefaultAsync(s => s.StudentId == StudentId);

                    if (studentToUpdate == null)
                    {
                        return NotFound(new { success = false, message = "Student not found." });
                    }

                    // Check if roll number already exists for another student
                    var existingStudent = await _context.Students
                        .FirstOrDefaultAsync(s => s.RollNumber == model.RollNumber && s.StudentId != StudentId);

                    if (existingStudent != null)
                    {
                        return Conflict(new { success = false, message = "A student with this roll number already exists." });
                    }

                    // Update Student fields
                    studentToUpdate.RollNumber = model.RollNumber;
                    studentToUpdate.FirstName = model.FirstName;
                    studentToUpdate.LastName = model.LastName;
                    studentToUpdate.BatchId = model.BatchId;
                    studentToUpdate.IsActive = model.IsActive;

                    // Update User fields if User exists
                    if (studentToUpdate.User != null)
                    {
                        // Check for duplicate Email
                        if (!string.IsNullOrWhiteSpace(Email) && Email != studentToUpdate.User.Email)
                        {
                            var existingEmail = await _context.Users.AnyAsync(u => u.Email == Email && u.UserId != studentToUpdate.UserId);
                            if (existingEmail)
                            {
                                return Conflict(new { success = false, message = "Email is already in use by another user." });
                            }
                            studentToUpdate.User.Email = Email;
                        }

                        // Check for duplicate Username
                        if (!string.IsNullOrWhiteSpace(Username) && Username != studentToUpdate.User.Username)
                        {
                            var existingUsername = await _context.Users.AnyAsync(u => u.Username == Username && u.UserId != studentToUpdate.UserId);
                            if (existingUsername)
                            {
                                return Conflict(new { success = false, message = "Username is already in use by another user." });
                            }
                            studentToUpdate.User.Username = Username;
                        }

                        // Update Password if provided
                        if (!string.IsNullOrWhiteSpace(Password))
                        {
                            studentToUpdate.User.PasswordHash = Password;
                        }
                        
                        // Also update IsActive on User to match Student
                         studentToUpdate.User.IsActive = model.IsActive;
                    }

                    _context.Update(studentToUpdate);
                    await _context.SaveChangesAsync();

                    return Ok(new { success = true, message = $"Student '{model.FirstName} {model.LastName}' has been updated successfully!" });
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!StudentExists(model.StudentId))
                    {
                        return NotFound(new { success = false, message = "Student not found." });
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            var errors = ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray()
                );
            return BadRequest(new { success = false, message = "Invalid data submitted.", errors });
        }

        // POST: Student/DeleteStudent
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteStudent(int StudentId)
        {
            var student = await _context.Students
                .Include(s => s.Attendances)
                .Include(s => s.Enrollments)
                .FirstOrDefaultAsync(s => s.StudentId == StudentId);

            if (student == null)
            {
                return NotFound(new { success = false, message = "Student not found." });
            }

            // Check if student has attendances
            if (student.Attendances.Any())
            {
                return BadRequest(new { success = false, message = $"Cannot delete student '{student.FirstName} {student.LastName}' because they have {student.Attendances.Count} attendance record(s)." });
            }

            // Check if student has enrollments
            if (student.Enrollments.Any())
            {
                return BadRequest(new { success = false, message = $"Cannot delete student '{student.FirstName} {student.LastName}' because they have {student.Enrollments.Count} enrollment(s)." });
            }

            _context.Students.Remove(student);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = $"Student '{student.FirstName} {student.LastName}' has been deleted successfully!" });
        }

        private bool StudentExists(int id)
        {
            return _context.Students.Any(e => e.StudentId == id);
        }
    }
}
