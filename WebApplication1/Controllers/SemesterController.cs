
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

        public SemesterController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Semester/Semesters
        public async Task<IActionResult> Semesters(SemesterFilterViewModel filter)
        {
            var query = _context.Semesters
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

            filter.Semesters = await query
                .OrderByDescending(s => s.Year)
                .ThenByDescending(s => s.StartDate)
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
            if (ModelState.IsValid)
            {
                // Validate end date is after start date
                if (model.EndDate <= model.StartDate)
                {
                    ModelState.AddModelError("EndDate", "End date must be after start date.");
                    return View(model);
                }

                // Check if semester name already exists for the same year
                var existingSemester = await _context.Semesters
                    .FirstOrDefaultAsync(s => s.SemesterName == model.SemesterName && s.Year == model.Year);

                if (existingSemester != null)
                {
                    ModelState.AddModelError("SemesterName", "A semester with this name already exists for the selected year.");
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

                _context.Semesters.Add(semester);
                await _context.SaveChangesAsync();

                TempData["success"] = $"Semester '{model.SemesterName}' has been added successfully!";
                return RedirectToAction(nameof(Semesters));
            }

            return View(model);
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
            if (ModelState.IsValid)
            {
                // Validate end date is after start date
                if (model.EndDate <= model.StartDate)
                {
                    ModelState.AddModelError("EndDate", "End date must be after start date.");
                    ViewBag.SemesterId = id;
                    return View(model);
                }

                var semester = await _context.Semesters.FindAsync(id);
                if (semester == null)
                {
                    return NotFound();
                }

                // Check if semester name already exists for another semester with the same year
                var existingSemester = await _context.Semesters
                    .FirstOrDefaultAsync(s => s.SemesterName == model.SemesterName &&
                                            s.Year == model.Year &&
                                            s.SemesterId != id);

                if (existingSemester != null)
                {
                    ModelState.AddModelError("SemesterName", "A semester with this name already exists for the selected year.");
                    ViewBag.SemesterId = id;
                    return View(model);
                }

                semester.SemesterName = model.SemesterName;
                semester.Year = model.Year;
                semester.StartDate = model.StartDate;
                semester.EndDate = model.EndDate;
                semester.IsActive = model.IsActive;

                _context.Update(semester);
                await _context.SaveChangesAsync();

                TempData["success"] = $"Semester '{model.SemesterName}' has been updated successfully!";
                return RedirectToAction(nameof(Semesters));
            }

            return View(model);
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
                TempData["error"] = "Semester not found.";
                return RedirectToAction(nameof(Semesters));
            }

            // Check if semester has course assignments
            if (semester.CourseAssignments.Any())
            {
                TempData["error"] = $"Cannot delete semester '{semester.SemesterName}' because it has {semester.CourseAssignments.Count} course assignment(s).";
                return RedirectToAction(nameof(Semesters));
            }

            // Check if semester has enrollments
            if (semester.Enrollments.Any())
            {
                TempData["error"] = $"Cannot delete semester '{semester.SemesterName}' because it has {semester.Enrollments.Count} student enrollment(s).";
                return RedirectToAction(nameof(Semesters));
            }

            _context.Semesters.Remove(semester);
            await _context.SaveChangesAsync();

            TempData["success"] = $"Semester '{semester.SemesterName}' has been deleted successfully!";
            return RedirectToAction(nameof(Semesters));
        }
    }
}
