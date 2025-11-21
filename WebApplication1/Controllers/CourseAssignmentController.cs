
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
    public class CourseAssignmentController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CourseAssignmentController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: CourseAssignment/CourseAssignments
        [HttpGet]
        public async Task<IActionResult> CourseAssignments(CourseAssignmentFilterViewModel filter)
        {
            var query = _context.CourseAssignments
                .Include(ca => ca.Teacher).ThenInclude(t => t.User)
                .Include(ca => ca.Course)
                .Include(ca => ca.Batch)
                .Include(ca => ca.Semester)
                .Include(ca => ca.Sessions)
                .Include(ca => ca.TimetableSlots)
                .AsQueryable();

            // Apply filters
            if (filter.TeacherId.HasValue && filter.TeacherId.Value > 0)
            {
                query = query.Where(ca => ca.TeacherId == filter.TeacherId.Value);
            }

            if (filter.CourseId.HasValue && filter.CourseId.Value > 0)
            {
                query = query.Where(ca => ca.CourseId == filter.CourseId.Value);
            }

            if (filter.BatchId.HasValue && filter.BatchId.Value > 0)
            {
                query = query.Where(ca => ca.BatchId == filter.BatchId.Value);
            }

            if (filter.SemesterId.HasValue && filter.SemesterId.Value > 0)
            {
                query = query.Where(ca => ca.SemesterId == filter.SemesterId.Value);
            }

            if (!string.IsNullOrEmpty(filter.Status))
            {
                bool isActive = filter.Status == "Active";
                query = query.Where(ca => ca.IsActive == isActive);
            }

            filter.CourseAssignments = await query
                .OrderByDescending(ca => ca.IsActive)
                .ThenBy(ca => ca.Teacher.FirstName)
                .ThenBy(ca => ca.Course.CourseName)
                .ToListAsync();

            // Load dropdowns
            filter.Teachers = await _context.Teachers
                .Include(t => t.User)
                .Where(t => t.IsActive == true)
                .Select(t => new SelectListItem
                {
                    Value = t.TeacherId.ToString(),
                    Text = $"{t.User} {t.LastName}"
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

            filter.Batches = await _context.Batches
                .Where(b => b.IsActive == true)
                .Select(b => new SelectListItem
                {
                    Value = b.BatchId.ToString(),
                    Text = $"{b.BatchName} - {b.Year}"
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

        // GET: CourseAssignment/AddCourseAssignment
        public async Task<IActionResult> AddCourseAssignment(int? teacherId = null, int? courseId = null)
        {
            var model = new AddCourseAssignmentViewModel
            {
                TeacherId = teacherId,
                CourseId = courseId
            };

            await LoadDropdowns(model);
            return View(model);
        }

        // POST: CourseAssignment/AddCourseAssignment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddCourseAssignment(AddCourseAssignmentViewModel model)
        {
            if (ModelState.IsValid)
            {
                // Check for duplicate assignment
                var existingAssignment = await _context.CourseAssignments
                    .FirstOrDefaultAsync(ca =>
                        ca.TeacherId == model.TeacherId &&
                        ca.CourseId == model.CourseId &&
                        ca.BatchId == model.BatchId &&
                        ca.SemesterId == model.SemesterId);

                if (existingAssignment != null)
                {
                    ModelState.AddModelError("", "This course assignment already exists.");
                    await LoadDropdowns(model);
                    return View(model);
                }

                var courseAssignment = new CourseAssignment
                {
                    TeacherId = model.TeacherId,
                    CourseId = model.CourseId,
                    BatchId = model.BatchId,
                    SemesterId = model.SemesterId,
                    IsActive = model.IsActive
                };

                _context.CourseAssignments.Add(courseAssignment);
                await _context.SaveChangesAsync();

                // Get names for success message
                var teacher = await _context.Teachers.Include(t => t.User).FirstOrDefaultAsync(t => t.TeacherId == model.TeacherId);
                var course = await _context.Courses.FirstOrDefaultAsync(c => c.CourseId == model.CourseId);

                TempData["success"] = $"Course '{course?.CourseName}' has been assigned to '{teacher?.FirstName} {teacher?.LastName}' successfully!";
                return RedirectToAction(nameof(CourseAssignments));
            }

            await LoadDropdowns(model);
            return View(model);
        }

        // GET: CourseAssignment/EditCourseAssignment/5
        public async Task<IActionResult> EditCourseAssignment(int? AssignmentId)
        {
            if (AssignmentId == null)
            {
                return NotFound();
            }

            var assignment = await _context.CourseAssignments
                .Include(ca => ca.Teacher).ThenInclude(t => t.User)
                .Include(ca => ca.Course)
                .Include(ca => ca.Batch)
                .Include(ca => ca.Semester)
                .FirstOrDefaultAsync(ca => ca.AssignmentId == AssignmentId);

            if (assignment == null)
            {
                return NotFound();
            }

            var model = new AddCourseAssignmentViewModel
            {
                TeacherId = assignment.TeacherId,
                CourseId = assignment.CourseId,
                BatchId = assignment.BatchId,
                SemesterId = assignment.SemesterId,
                IsActive = assignment.IsActive ?? true
            };

            await LoadDropdowns(model);
            ViewBag.AssignmentId = AssignmentId;
            return View(model);
        }

        // POST: CourseAssignment/EditCourseAssignment/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCourseAssignment(int AssignmentId, AddCourseAssignmentViewModel model)
        {
            var assignment = await _context.CourseAssignments.FindAsync(AssignmentId);
            if (assignment == null)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // Check for duplicate assignment (excluding current)
                    var existingAssignment = await _context.CourseAssignments
                        .FirstOrDefaultAsync(ca =>
                            ca.AssignmentId != AssignmentId &&
                            ca.TeacherId == model.TeacherId &&
                            ca.CourseId == model.CourseId &&
                            ca.BatchId == model.BatchId &&
                            ca.SemesterId == model.SemesterId);

                    if (existingAssignment != null)
                    {
                        ModelState.AddModelError("", "This course assignment already exists.");
                        await LoadDropdowns(model);
                        ViewBag.AssignmentId = AssignmentId;
                        return View(model);
                    }

                    assignment.TeacherId = model.TeacherId;
                    assignment.CourseId = model.CourseId;
                    assignment.BatchId = model.BatchId;
                    assignment.SemesterId = model.SemesterId;
                    assignment.IsActive = model.IsActive;

                    _context.Update(assignment);
                    await _context.SaveChangesAsync();

                    TempData["success"] = "Course assignment has been updated successfully!";
                    return RedirectToAction(nameof(CourseAssignments));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!CourseAssignmentExists(AssignmentId))
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
            ViewBag.AssignmentId = AssignmentId;
            return View(model);
        }

        // POST: CourseAssignment/DeleteCourseAssignment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCourseAssignment(int AssignmentId)
        {
            var assignment = await _context.CourseAssignments
                .Include(ca => ca.Sessions)
                .Include(ca => ca.TimetableSlots)
                .Include(ca => ca.Teacher).ThenInclude(t => t.User)
                .Include(ca => ca.Course)
                .FirstOrDefaultAsync(ca => ca.AssignmentId == AssignmentId);

            if (assignment == null)
            {
                TempData["error"] = "Course assignment not found.";
                return RedirectToAction(nameof(CourseAssignments));
            }

            // Check if assignment has sessions
            if (assignment.Sessions.Any())
            {
                TempData["error"] = $"Cannot delete this assignment because it has {assignment.Sessions.Count} session(s) associated with it.";
                return RedirectToAction(nameof(CourseAssignments));
            }

            // Check if assignment has timetable slots
            if (assignment.TimetableSlots.Any())
            {
                TempData["error"] = $"Cannot delete this assignment because it has {assignment.TimetableSlots.Count} timetable slot(s) associated with it.";
                return RedirectToAction(nameof(CourseAssignments));
            }

            _context.CourseAssignments.Remove(assignment);
            await _context.SaveChangesAsync();

            TempData["success"] = $"Course assignment for '{assignment.Course?.CourseName}' has been deleted successfully!";
            return RedirectToAction(nameof(CourseAssignments));
        }

        // Helper method to load dropdowns
        private async Task LoadDropdowns(AddCourseAssignmentViewModel model)
        {
            model.Teachers = await _context.Teachers
                .Include(t => t.User)
                .Where(t => t.IsActive == true)
                .Select(t => new SelectListItem
                {
                    Value = t.TeacherId.ToString(),
                    Text = $"{t.FirstName} {t.LastName}"
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

            model.Batches = await _context.Batches
                .Where(b => b.IsActive == true)
                .Select(b => new SelectListItem
                {
                    Value = b.BatchId.ToString(),
                    Text = $"{b.BatchName} - {b.Year}"
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

        private bool CourseAssignmentExists(int id)
        {
            return _context.CourseAssignments.Any(e => e.AssignmentId == id);
        }
        // GET: CourseAssignment/GetDropdownData (for AJAX)
        [HttpGet]
        public async Task<IActionResult> GetDropdownData()
        {
            var data = new
            {
                teachers = await _context.Teachers
                    .Include(t => t.User)
                    .Where(t => t.IsActive == true)
                    .Select(t => new { value = t.TeacherId.ToString(), text = $"{t.FirstName} {t.LastName}" })
                    .ToListAsync(),

                courses = await _context.Courses
                    .Where(c => c.IsActive == true)
                    .Select(c => new { value = c.CourseId.ToString(), text = $"{c.CourseCode} - {c.CourseName}" })
                    .ToListAsync(),

                batches = await _context.Batches
                    .Where(b => b.IsActive == true)
                    .Select(b => new { value = b.BatchId.ToString(), text = $"{b.BatchName} - {b.Year}" })
                    .ToListAsync(),

                semesters = await _context.Semesters
                    .Where(s => s.IsActive == true)
                    .Select(s => new { value = s.SemesterId.ToString(), text = $"{s.SemesterName} - {s.Year}" })
                    .ToListAsync()
            };

            return Json(data);
        }

        // POST: CourseAssignment/QuickAssign (AJAX endpoint)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickAssign([FromBody] QuickAssignRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return Json(new { success = false, message = "Invalid data provided." });
                }

                // Check for duplicate assignment
                var existingAssignment = await _context.CourseAssignments
                    .FirstOrDefaultAsync(ca =>
                        ca.TeacherId == request.TeacherId &&
                        ca.CourseId == request.CourseId &&
                        ca.BatchId == request.BatchId &&
                        ca.SemesterId == request.SemesterId);

                if (existingAssignment != null)
                {
                    return Json(new { success = false, message = "This course assignment already exists." });
                }

                var courseAssignment = new CourseAssignment
                {
                    TeacherId = request.TeacherId,
                    CourseId = request.CourseId,
                    BatchId = request.BatchId,
                    SemesterId = request.SemesterId,
                    IsActive = true
                };

                _context.CourseAssignments.Add(courseAssignment);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Course assigned successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "An error occurred while assigning the course." });
            }
        }

        // Request model for QuickAssign
        public class QuickAssignRequest
        {
            public int TeacherId { get; set; }
            public int CourseId { get; set; }
            public int BatchId { get; set; }
            public int SemesterId { get; set; }
        }
    }
}
