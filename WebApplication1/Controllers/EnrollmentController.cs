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
    public class EnrollmentController : Controller
    {
        private readonly ApplicationDbContext _context;

        public EnrollmentController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Enrollment/Enrollments
        [HttpGet]
        public async Task<IActionResult> Enrollments(EnrollmentFilterViewModel filter)
        {
            var query = _context.Enrollments
                .Include(e => e.Student).ThenInclude(s => s.User)
                .Include(e => e.Student).ThenInclude(s => s.Batch)
                .Include(e => e.Course)
                .Include(e => e.Semester)
                .AsQueryable();

            // Apply filters
            if (filter.StudentId.HasValue && filter.StudentId.Value > 0)
            {
                query = query.Where(e => e.StudentId == filter.StudentId.Value);
            }

            if (filter.CourseId.HasValue && filter.CourseId.Value > 0)
            {
                query = query.Where(e => e.CourseId == filter.CourseId.Value);
            }

            if (filter.SemesterId.HasValue && filter.SemesterId.Value > 0)
            {
                query = query.Where(e => e.SemesterId == filter.SemesterId.Value);
            }

            if (!string.IsNullOrEmpty(filter.Status))
            {
                query = query.Where(e => e.Status == filter.Status);
            }

            filter.Enrollments = await query
                .OrderByDescending(e => e.Status == "Active")
                .ThenBy(e => e.Student.FirstName)
                .ThenBy(e => e.Course.CourseName)
                .ToListAsync();

            // Load dropdowns
            filter.Students = await _context.Students
                .Include(s => s.User)
                .Include(s => s.Batch)
                .Where(s => s.IsActive == true)
                .Select(s => new SelectListItem
                {
                    Value = s.StudentId.ToString(),
                    Text = $"{s.FirstName} {s.LastName} ({s.RollNumber})"
                })
                .ToListAsync();

            filter.Courses = await _context.Courses
                .Where(c => c.IsActive == true)
                .Select(c => new SelectListItem
                {
                    Value = c.CourseId.ToString(),
                    Text = $"{c.CourseCode} - {c.CourseName}"
                })
                .ToListAsync();

            filter.Semesters = await _context.Semesters
                .Where(s => s.IsActive == true)
                .Select(s => new SelectListItem
                {
                    Value = s.SemesterId.ToString(),
                    Text = $"{s.SemesterName} - {s.Year}"
                })
                .ToListAsync();

            return View(filter);
        }

        // GET: Enrollment/AddEnrollment
        public async Task<IActionResult> AddEnrollment(int? studentId = null, int? courseId = null)
        {
            var model = new AddEnrollmentViewModel
            {
                StudentId = studentId,
                CourseId = courseId,
                Status = "Active"
            };

            await LoadDropdowns(model);
            return View(model);
        }

        // POST: Enrollment/AddEnrollment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddEnrollment(AddEnrollmentViewModel model)
        {
            if (ModelState.IsValid)
            {
                // Check for duplicate enrollment
                var existingEnrollment = await _context.Enrollments
                    .FirstOrDefaultAsync(e =>
                        e.StudentId == model.StudentId &&
                        e.CourseId == model.CourseId &&
                        e.SemesterId == model.SemesterId);

                if (existingEnrollment != null)
                {
                    ModelState.AddModelError("", "This student is already enrolled in this course for the selected semester.");
                    await LoadDropdowns(model);
                    return View(model);
                }

                var enrollment = new Enrollment
                {
                    StudentId = model.StudentId,
                    CourseId = model.CourseId,
                    SemesterId = model.SemesterId,
                    Status = model.Status
                };

                _context.Enrollments.Add(enrollment);
                await _context.SaveChangesAsync();

                // Get names for success message
                var student = await _context.Students.Include(s => s.User).FirstOrDefaultAsync(s => s.StudentId == model.StudentId);
                var course = await _context.Courses.FirstOrDefaultAsync(c => c.CourseId == model.CourseId);

                TempData["success"] = $"'{student?.FirstName} {student?.LastName}' has been enrolled in '{course?.CourseName}' successfully!";
                return RedirectToAction(nameof(Enrollments));
            }

            await LoadDropdowns(model);
            return View(model);
        }

        // GET: Enrollment/EditEnrollment/5
        public async Task<IActionResult> EditEnrollment(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var enrollment = await _context.Enrollments
                .Include(e => e.Student).ThenInclude(s => s.User)
                .Include(e => e.Course)
                .Include(e => e.Semester)
                .FirstOrDefaultAsync(e => e.EnrollmentId == id);

            if (enrollment == null)
            {
                return NotFound();
            }

            var model = new AddEnrollmentViewModel
            {
                StudentId = enrollment.StudentId,
                CourseId = enrollment.CourseId,
                SemesterId = enrollment.SemesterId,
                Status = enrollment.Status ?? "Active"
            };

            await LoadDropdowns(model);
            ViewBag.EnrollmentId = id;
            return View(model);
        }

        // POST: Enrollment/EditEnrollment/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditEnrollment(int id, AddEnrollmentViewModel model)
        {
            var enrollment = await _context.Enrollments.FindAsync(id);
            if (enrollment == null)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // Check for duplicate enrollment (excluding current)
                    var existingEnrollment = await _context.Enrollments
                        .FirstOrDefaultAsync(e =>
                            e.EnrollmentId != id &&
                            e.StudentId == model.StudentId &&
                            e.CourseId == model.CourseId &&
                            e.SemesterId == model.SemesterId);

                    if (existingEnrollment != null)
                    {
                        ModelState.AddModelError("", "This student is already enrolled in this course for the selected semester.");
                        await LoadDropdowns(model);
                        ViewBag.EnrollmentId = id;
                        return View(model);
                    }

                    enrollment.StudentId = model.StudentId;
                    enrollment.CourseId = model.CourseId;
                    enrollment.SemesterId = model.SemesterId;
                    enrollment.Status = model.Status;

                    _context.Update(enrollment);
                    await _context.SaveChangesAsync();

                    TempData["success"] = "Enrollment has been updated successfully!";
                    return RedirectToAction(nameof(Enrollments));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!EnrollmentExists(id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            await LoadDropdowns(model);
            ViewBag.EnrollmentId = id;
            return View(model);
        }

        // POST: Enrollment/DeleteEnrollment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteEnrollment(int id)
        {
            var enrollment = await _context.Enrollments
                .Include(e => e.Student).ThenInclude(s => s.User)
                .Include(e => e.Course)
                .FirstOrDefaultAsync(e => e.EnrollmentId == id);

            if (enrollment == null)
            {
                TempData["error"] = "Enrollment not found.";
                return RedirectToAction(nameof(Enrollments));
            }

            _context.Enrollments.Remove(enrollment);
            await _context.SaveChangesAsync();

            TempData["success"] = $"Enrollment for '{enrollment.Student?.FirstName} {enrollment.Student?.LastName}' in '{enrollment.Course?.CourseName}' has been deleted successfully!";
            return RedirectToAction(nameof(Enrollments));
        }

        // Helper method to load dropdowns
        private async Task LoadDropdowns(AddEnrollmentViewModel model)
        {
            model.Students = await _context.Students
                .Include(s => s.User)
                .Include(s => s.Batch)
                .Where(s => s.IsActive == true)
                .Select(s => new SelectListItem
                {
                    Value = s.StudentId.ToString(),
                    Text = $"{s.FirstName} {s.LastName} ({s.RollNumber}) - {s.Batch.BatchName}"
                })
                .ToListAsync();

            model.Courses = await _context.Courses
                .Where(c => c.IsActive == true)
                .Select(c => new SelectListItem
                {
                    Value = c.CourseId.ToString(),
                    Text = $"{c.CourseCode} - {c.CourseName}"
                })
                .ToListAsync();

            model.Semesters = await _context.Semesters
                .Where(s => s.IsActive == true)
                .Select(s => new SelectListItem
                {
                    Value = s.SemesterId.ToString(),
                    Text = $"{s.SemesterName} - {s.Year}"
                })
                .ToListAsync();
        }

        private bool EnrollmentExists(int id)
        {
            return _context.Enrollments.Any(e => e.EnrollmentId == id);
        }

        // AJAX endpoint to get dropdown data for Quick Enroll modals
        [HttpGet]
        public IActionResult GetDropdownData()
        {
            var students = _context.Students
                .Include(s => s.User)
                .Include(s => s.Batch)
                .Where(s => s.IsActive == true)
                .Select(s => new
                {
                    id = s.StudentId,
                    text = s.FirstName + " " + s.LastName + " (" + s.RollNumber + ") - " + s.Batch.BatchName
                })
                .OrderBy(s => s.text)
                .ToList();

            var courses = _context.Courses
                .Where(c => c.IsActive == true)
                .Select(c => new
                {
                    id = c.CourseId,
                    text = c.CourseCode + " - " + c.CourseName
                })
                .OrderBy(c => c.text)
                .ToList();

            var semesters = _context.Semesters
                .Select(s => new
                {
                    id = s.SemesterId,
                    text = s.SemesterName + " - " + s.Year
                })
                .OrderByDescending(s => s.text)
                .ToList();

            return Json(new
            {
                students = students,
                courses = courses,
                semesters = semesters
            });
        }

        // AJAX endpoint for Quick Enroll
        [HttpPost]
        public IActionResult QuickEnroll([FromBody] QuickEnrollRequest request)
        {
            if (request.StudentId == 0 || request.CourseId == 0 || request.SemesterId == 0)
            {
                return Json(new { success = false, message = "Please select all required fields." });
            }

            // Check if enrollment already exists
            var existingEnrollment = _context.Enrollments
                .FirstOrDefault(e => e.StudentId == request.StudentId &&
                                    e.CourseId == request.CourseId &&
                                    e.SemesterId == request.SemesterId);

            if (existingEnrollment != null)
            {
                return Json(new { success = false, message = "This student is already enrolled in this course for the selected semester." });
            }

            // Create enrollment
            var enrollment = new Enrollment
            {
                StudentId = request.StudentId,
                CourseId = request.CourseId,
                SemesterId = request.SemesterId,
                Status = "Active"
            };

            _context.Enrollments.Add(enrollment);
            _context.SaveChanges();

            // Get names for success message
            var student = _context.Students
                .Include(s => s.User)
                .FirstOrDefault(s => s.StudentId == request.StudentId);
            var course = _context.Courses
                .FirstOrDefault(c => c.CourseId == request.CourseId);

            var studentName = student != null ? $"{student.FirstName} {student.LastName}" : "Student";
            var courseName = course != null ? $"{course.CourseCode} - {course.CourseName}" : "Course";

            return Json(new
            {
                success = true,
                message = $"Successfully enrolled {studentName} in {courseName}."
            });
        }
        public async Task<IActionResult> BulkEnroll()
        {
            var model = new BulkEnrollViewModel();
            await LoadBulkEnrollDropdowns(model);
            return View(model);
        }

        // POST: Enrollment/BulkEnroll
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkEnroll(BulkEnrollViewModel model)
        {
            if (ModelState.IsValid)
            {
                // Get all active students in the selected batch
                var students = await _context.Students
                    .Where(s => s.BatchId == model.BatchId && s.IsActive == true)
                    .ToListAsync();

                if (!students.Any())
                {
                    ModelState.AddModelError("", "No active students found in the selected batch.");
                    await LoadBulkEnrollDropdowns(model);
                    return View(model);
                }

                int enrolledCount = 0;
                int skippedCount = 0;

                foreach (var student in students)
                {
                    // Check if already enrolled
                    bool isEnrolled = await _context.Enrollments.AnyAsync(e =>
                        e.StudentId == student.StudentId &&
                        e.CourseId == model.CourseId &&
                        e.SemesterId == model.SemesterId);

                    if (!isEnrolled)
                    {
                        var enrollment = new Enrollment
                        {
                            StudentId = student.StudentId,
                            CourseId = model.CourseId,
                            SemesterId = model.SemesterId,
                            Status = "Active"
                        };
                        _context.Enrollments.Add(enrollment);
                        enrolledCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }
                }

                await _context.SaveChangesAsync();

                TempData["success"] = $"Bulk enrollment completed. {enrolledCount} students enrolled, {skippedCount} skipped (already enrolled).";
                return RedirectToAction(nameof(Enrollments));
            }

            await LoadBulkEnrollDropdowns(model);
            return View(model);
        }

        private async Task LoadBulkEnrollDropdowns(BulkEnrollViewModel model)
        {
            model.Batches = new SelectList(await _context.Batches.ToListAsync(), "BatchId", "BatchName");

            model.Semesters = new SelectList(await _context.Semesters
                .Where(s => s.IsActive == true)
                .Select(s => new { s.SemesterId, Name = s.SemesterName + " - " + s.Year })
                .ToListAsync(), "SemesterId", "Name");

            model.Courses = new SelectList(await _context.Courses
                .Where(c => c.IsActive == true)
                .Select(c => new { c.CourseId, Name = c.CourseCode + " - " + c.CourseName })
                .ToListAsync(), "CourseId", "Name");
        }
        // DTO for Quick Enroll request
        public class QuickEnrollRequest
        {
            public int StudentId { get; set; }
            public int CourseId { get; set; }
            public int SemesterId { get; set; }
        }
    }
}
