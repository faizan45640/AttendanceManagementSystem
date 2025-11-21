
using AMS.Models;
using AMS.Models.Entities;
using AMS.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AMS.Controllers
{
    public class CourseController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CourseController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Course/Courses
        public async Task<IActionResult> Courses(CourseFilterViewModel filter)
        {
            var query = _context.Courses
                .AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(filter.CourseCode))
            {
                query = query.Where(c => c.CourseCode!.Contains(filter.CourseCode));
            }

            if (!string.IsNullOrEmpty(filter.CourseName))
            {
                query = query.Where(c => c.CourseName!.Contains(filter.CourseName));
            }

            if (!string.IsNullOrEmpty(filter.Status))
            {
                bool isActive = filter.Status == "Active";
                query = query.Where(c => c.IsActive == isActive);
            }

            filter.Courses = await query
                .OrderBy(c => c.CourseCode)
                .ToListAsync();

            // Load counts separately to avoid circular references
            foreach (var course in filter.Courses)
            {
                course.CourseAssignments = await _context.CourseAssignments
                    .Where(ca => ca.CourseId == course.CourseId)
                    .Select(ca => new CourseAssignment { AssignmentId = ca.AssignmentId })
                    .ToListAsync();

                course.Enrollments = await _context.Enrollments
                    .Where(e => e.CourseId == course.CourseId)
                    .Select(e => new Enrollment { EnrollmentId = e.EnrollmentId })
                    .ToListAsync();
            }

            return View(filter);
        }

        // GET: Course/AddCourse
        public IActionResult AddCourse()
        {
            var model = new AddCourseViewModel
            {
                IsActive = true
            };
            return View(model);
        }

        // POST: Course/AddCourse
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddCourse(AddCourseViewModel model)
        {
            if (ModelState.IsValid)
            {
                // Check if course code already exists
                var existingCourse = await _context.Courses
                    .FirstOrDefaultAsync(c => c.CourseCode == model.CourseCode);

                if (existingCourse != null)
                {
                    ModelState.AddModelError("CourseCode", "A course with this code already exists.");
                    return View(model);
                }

                var course = new Course
                {
                    CourseCode = model.CourseCode,
                    CourseName = model.CourseName,
                    CreditHours = model.CreditHours,
                    IsActive = model.IsActive
                };

                _context.Courses.Add(course);
                await _context.SaveChangesAsync();

                TempData["success"] = $"Course '{model.CourseCode} - {model.CourseName}' has been added successfully!";
                return RedirectToAction(nameof(Courses));
            }

            return View(model);
        }

        // GET: Course/EditCourse/5
        public async Task<IActionResult> EditCourse(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var course = await _context.Courses.FindAsync(id);
            if (course == null)
            {
                return NotFound();
            }

            var model = new AddCourseViewModel
            {
                CourseCode = course.CourseCode ?? string.Empty,
                CourseName = course.CourseName ?? string.Empty,
                CreditHours = course.CreditHours ?? 3,
                IsActive = course.IsActive ?? true
            };

            ViewBag.CourseId = id;
            return View(model);
        }

        // POST: Course/EditCourse/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCourse(int id, AddCourseViewModel model)
        {
            if (ModelState.IsValid)
            {
                var course = await _context.Courses.FindAsync(id);
                if (course == null)
                {
                    return NotFound();
                }

                // Check if course code already exists for another course
                var existingCourse = await _context.Courses
                    .FirstOrDefaultAsync(c => c.CourseCode == model.CourseCode && c.CourseId != id);

                if (existingCourse != null)
                {
                    ModelState.AddModelError("CourseCode", "A course with this code already exists.");
                    ViewBag.CourseId = id;
                    return View(model);
                }

                course.CourseCode = model.CourseCode;
                course.CourseName = model.CourseName;
                course.CreditHours = model.CreditHours;
                course.IsActive = model.IsActive;

                _context.Update(course);
                await _context.SaveChangesAsync();

                TempData["success"] = $"Course '{model.CourseCode} - {model.CourseName}' has been updated successfully!";
                return RedirectToAction(nameof(Courses));
            }

            ViewBag.CourseId = id;
            return View(model);
        }

        // POST: Course/DeleteCourse
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCourse(int id)
        {
            var course = await _context.Courses
                .Include(c => c.CourseAssignments)
                .Include(c => c.Enrollments)
                .FirstOrDefaultAsync(c => c.CourseId == id);

            if (course == null)
            {
                TempData["error"] = "Course not found.";
                return RedirectToAction(nameof(Courses));
            }

            // Check if course has assignments
            if (course.CourseAssignments.Any())
            {
                TempData["error"] = $"Cannot delete course '{course.CourseCode}' because it has {course.CourseAssignments.Count} course assignment(s).";
                return RedirectToAction(nameof(Courses));
            }

            // Check if course has enrollments
            if (course.Enrollments.Any())
            {
                TempData["error"] = $"Cannot delete course '{course.CourseCode}' because it has {course.Enrollments.Count} student enrollment(s).";
                return RedirectToAction(nameof(Courses));
            }

            _context.Courses.Remove(course);
            await _context.SaveChangesAsync();

            TempData["success"] = $"Course '{course.CourseCode} - {course.CourseName}' has been deleted successfully!";
            return RedirectToAction(nameof(Courses));
        }
    }
}
