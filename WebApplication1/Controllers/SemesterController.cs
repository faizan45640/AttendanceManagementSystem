using AMS.Data;
using AMS.Models;
using AMS.Models.Entities;
using AMS.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AMS.Controllers
{
    public class SemesterController : Controller
    {
        private readonly ApplicationDbContext _context;

        private bool IsAjaxRequest()
        {
            return string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        }

        private object GetModelStateErrors()
        {
            return ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray() ?? Array.Empty<string>()
                );
        }

        public SemesterController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Semester/Semesters
        public async Task<IActionResult> Semesters(SemesterFilterViewModel filter)
        {
            filter.Page = filter.Page < 1 ? 1 : filter.Page;
            filter.PageSize = filter.PageSize <= 0 ? 20 : filter.PageSize;
            filter.PageSize = Math.Clamp(filter.PageSize, 10, 100);

            var query = _context.Semesters
                .AsNoTracking()
                .AsQueryable();

            // Apply filters
            if (filter.Year.HasValue)
            {
                query = query.Where(s => s.Year == filter.Year.Value);
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

            filter.Semesters = await query
                .OrderByDescending(s => s.Year)
                .ThenByDescending(s => s.StartDate)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();

            // Load counts separately to avoid circular references
            foreach (var semester in filter.Semesters)
            {
                semester.CourseAssignments = await _context.CourseAssignments
                    .Where(ca => ca.SemesterId == semester.SemesterId)
                    .Select(ca => new CourseAssignment { AssignmentId = ca.AssignmentId })
                    .ToListAsync();

                semester.Enrollments = await _context.Enrollments
                    .Where(e => e.SemesterId == semester.SemesterId)
                    .Select(e => new Enrollment { EnrollmentId = e.EnrollmentId })
                    .ToListAsync();
            }

            return View(filter);
        }

        // GET: Semester/AddSemester
        public IActionResult AddSemester()
        {
            var model = new AddSemesterViewModel
            {
                Year = DateTime.Now.Year,
                StartDate = DateOnly.FromDateTime(DateTime.Now),
                EndDate = DateOnly.FromDateTime(DateTime.Now.AddMonths(4)),
                IsActive = true
            };
            return View(model);
        }

        // POST: Semester/AddSemester
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSemester(AddSemesterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                if (IsAjaxRequest())
                {
                    var errors = GetModelStateErrors();
                    return BadRequest(new { success = false, message = "Invalid data submitted.", errors });
                }

                return View(model);
            }

            // Validate end date is after start date
            if (model.EndDate <= model.StartDate)
            {
                if (IsAjaxRequest())
                {
                    return BadRequest(new { success = false, message = "End date must be after start date." });
                }

                ModelState.AddModelError(nameof(AddSemesterViewModel.EndDate), "End date must be after start date.");
                return View(model);
            }

            // Check if semester name already exists for the same year
            var existingSemester = await _context.Semesters
                .FirstOrDefaultAsync(s => s.SemesterName == model.SemesterName && s.Year == model.Year);

            if (existingSemester != null)
            {
                if (IsAjaxRequest())
                {
                    return Conflict(new { success = false, message = "A semester with this name already exists for the selected year." });
                }

                ModelState.AddModelError(nameof(AddSemesterViewModel.SemesterName), "A semester with this name already exists for the selected year.");
                return View(model);
            }

            var semester = new Semester
            {
                SemesterName = model.SemesterName,
                Year = model.Year,
                StartDate = model.StartDate,
                EndDate = model.EndDate,
                IsActive = model.IsActive
            };

            // If this semester is active, deactivate all others
            if (model.IsActive)
            {
                var activeSemesters = await _context.Semesters.Where(s => s.IsActive == true).ToListAsync();
                foreach (var s in activeSemesters)
                {
                    s.IsActive = false;
                }
            }

            _context.Semesters.Add(semester);
            await _context.SaveChangesAsync();

            var successMessage = $"Semester '{model.SemesterName}' has been added successfully!";
            if (IsAjaxRequest())
            {
                return Ok(new { success = true, message = successMessage });
            }

            TempData["success"] = successMessage;
            return RedirectToAction(nameof(Semesters));
        }

        // GET: Semester/EditSemester/5
        public async Task<IActionResult> EditSemester(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var semester = await _context.Semesters.FindAsync(id);
            if (semester == null)
            {
                return NotFound();
            }

            var model = new AddSemesterViewModel
            {
                SemesterName = semester.SemesterName,
                Year = semester.Year,
                StartDate = semester.StartDate,
                EndDate = semester.EndDate,
                IsActive = semester.IsActive
            };

            ViewBag.SemesterId = id;
            return View(model);
        }

        // POST: Semester/EditSemester/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditSemester(int id, AddSemesterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                if (IsAjaxRequest())
                {
                    var errors = GetModelStateErrors();
                    return BadRequest(new { success = false, message = "Invalid data submitted.", errors });
                }

                ViewBag.SemesterId = id;
                return View(model);
            }

            // Validate end date is after start date
            if (model.EndDate <= model.StartDate)
            {
                if (IsAjaxRequest())
                {
                    return BadRequest(new { success = false, message = "End date must be after start date." });
                }

                ModelState.AddModelError(nameof(AddSemesterViewModel.EndDate), "End date must be after start date.");
                ViewBag.SemesterId = id;
                return View(model);
            }

            var semester = await _context.Semesters.FindAsync(id);
            if (semester == null)
            {
                if (IsAjaxRequest())
                {
                    return NotFound(new { success = false, message = "Semester not found." });
                }

                TempData["error"] = "Semester not found.";
                return RedirectToAction(nameof(Semesters));
            }

            // Check if semester name already exists for another semester with the same year
            var existingSemester = await _context.Semesters
                .FirstOrDefaultAsync(s => s.SemesterName == model.SemesterName &&
                                        s.Year == model.Year &&
                                        s.SemesterId != id);

            if (existingSemester != null)
            {
                if (IsAjaxRequest())
                {
                    return Conflict(new { success = false, message = "A semester with this name already exists for the selected year." });
                }

                ModelState.AddModelError(nameof(AddSemesterViewModel.SemesterName), "A semester with this name already exists for the selected year.");
                ViewBag.SemesterId = id;
                return View(model);
            }

            semester.SemesterName = model.SemesterName;
            semester.Year = model.Year;
            semester.StartDate = model.StartDate;
            semester.EndDate = model.EndDate;
            semester.IsActive = model.IsActive;

            // If this semester is active, deactivate all others
            if (model.IsActive)
            {
                var activeSemesters = await _context.Semesters
                    .Where(s => s.IsActive == true && s.SemesterId != id)
                    .ToListAsync();

                foreach (var s in activeSemesters)
                {
                    s.IsActive = false;
                }
            }

            _context.Update(semester);
            await _context.SaveChangesAsync();

            var successMessage = $"Semester '{model.SemesterName}' has been updated successfully!";
            if (IsAjaxRequest())
            {
                return Ok(new { success = true, message = successMessage });
            }

            TempData["success"] = successMessage;
            return RedirectToAction(nameof(Semesters));
        }

        // POST: Semester/DeleteSemester
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSemester(int id)
        {
            var semester = await _context.Semesters
                .Include(s => s.CourseAssignments)
                .Include(s => s.Enrollments)
                .FirstOrDefaultAsync(s => s.SemesterId == id);

            if (semester == null)
            {
                if (IsAjaxRequest())
                {
                    return NotFound(new { success = false, message = "Semester not found." });
                }

                TempData["error"] = "Semester not found.";
                return RedirectToAction(nameof(Semesters));
            }

            // Check if semester has course assignments
            if (semester.CourseAssignments.Any())
            {
                var message = $"Cannot delete semester '{semester.SemesterName}' because it has {semester.CourseAssignments.Count} course assignment(s).";
                if (IsAjaxRequest())
                {
                    return BadRequest(new { success = false, message });
                }

                TempData["error"] = message;
                return RedirectToAction(nameof(Semesters));
            }

            // Check if semester has enrollments
            if (semester.Enrollments.Any())
            {
                var message = $"Cannot delete semester '{semester.SemesterName}' because it has {semester.Enrollments.Count} student enrollment(s).";
                if (IsAjaxRequest())
                {
                    return BadRequest(new { success = false, message });
                }

                TempData["error"] = message;
                return RedirectToAction(nameof(Semesters));
            }

            var semesterName = semester.SemesterName;
            _context.Semesters.Remove(semester);
            await _context.SaveChangesAsync();

            var successMessage = $"Semester '{semesterName}' has been deleted successfully!";
            if (IsAjaxRequest())
            {
                return Ok(new { success = true, message = successMessage });
            }

            TempData["success"] = successMessage;
            return RedirectToAction(nameof(Semesters));
        }
    }
}
