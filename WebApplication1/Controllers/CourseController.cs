using AMS.Data;
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

        public CourseController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Course/Courses
        public async Task<IActionResult> Courses(CourseFilterViewModel filter)
        {
            filter.Page = filter.Page < 1 ? 1 : filter.Page;
            filter.PageSize = filter.PageSize <= 0 ? 20 : filter.PageSize;
            filter.PageSize = Math.Clamp(filter.PageSize, 10, 100);

            var query = _context.Courses
                .AsNoTracking()
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

            filter.TotalCount = await query.CountAsync();
            if (filter.TotalPages > 0 && filter.Page > filter.TotalPages)
            {
                filter.Page = filter.TotalPages;
            }

            filter.Courses = await query
                .OrderBy(c => c.CourseCode)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
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
            if (!ModelState.IsValid)
            {
                if (IsAjaxRequest())
                {
                    var errors = GetModelStateErrors();
                    return BadRequest(new { success = false, message = "Invalid data submitted.", errors });
                }

                return View(model);
            }

            // Check if course code already exists
            var existingCourse = await _context.Courses
                .FirstOrDefaultAsync(c => c.CourseCode == model.CourseCode);

            if (existingCourse != null)
            {
                if (IsAjaxRequest())
                {
                    return Conflict(new { success = false, message = "A course with this code already exists." });
                }

                ModelState.AddModelError(nameof(AddCourseViewModel.CourseCode), "A course with this code already exists.");
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

            var successMessage = $"Course '{model.CourseCode} - {model.CourseName}' has been added successfully!";
            if (IsAjaxRequest())
            {
                return Ok(new { success = true, message = successMessage });
            }

            TempData["success"] = successMessage;
            return RedirectToAction(nameof(Courses));
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
            if (!ModelState.IsValid)
            {
                if (IsAjaxRequest())
                {
                    var errors = GetModelStateErrors();
                    return BadRequest(new { success = false, message = "Invalid data submitted.", errors });
                }

                ViewBag.CourseId = id;
                return View(model);
            }

            var course = await _context.Courses.FindAsync(id);
            if (course == null)
            {
                if (IsAjaxRequest())
                {
                    return NotFound(new { success = false, message = "Course not found." });
                }

                TempData["error"] = "Course not found.";
                return RedirectToAction(nameof(Courses));
            }

            // Check if course code already exists for another course
            var existingCourse = await _context.Courses
                .FirstOrDefaultAsync(c => c.CourseCode == model.CourseCode && c.CourseId != id);

            if (existingCourse != null)
            {
                if (IsAjaxRequest())
                {
                    return Conflict(new { success = false, message = "A course with this code already exists." });
                }

                ModelState.AddModelError(nameof(AddCourseViewModel.CourseCode), "A course with this code already exists.");
                ViewBag.CourseId = id;
                return View(model);
            }

            course.CourseCode = model.CourseCode;
            course.CourseName = model.CourseName;
            course.CreditHours = model.CreditHours;
            course.IsActive = model.IsActive;

            _context.Update(course);
            await _context.SaveChangesAsync();

            var successMessage = $"Course '{model.CourseCode} - {model.CourseName}' has been updated successfully!";
            if (IsAjaxRequest())
            {
                return Ok(new { success = true, message = successMessage });
            }

            TempData["success"] = successMessage;
            return RedirectToAction(nameof(Courses));
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
                if (IsAjaxRequest())
                {
                    return NotFound(new { success = false, message = "Course not found." });
                }

                TempData["error"] = "Course not found.";
                return RedirectToAction(nameof(Courses));
            }

            // Check if course has assignments
            if (course.CourseAssignments.Any())
            {
                var message = $"Cannot delete course '{course.CourseCode}' because it has {course.CourseAssignments.Count} course assignment(s).";
                if (IsAjaxRequest())
                {
                    return BadRequest(new { success = false, message });
                }

                TempData["error"] = message;
                return RedirectToAction(nameof(Courses));
            }

            // Check if course has enrollments
            if (course.Enrollments.Any())
            {
                var message = $"Cannot delete course '{course.CourseCode}' because it has {course.Enrollments.Count} student enrollment(s).";
                if (IsAjaxRequest())
                {
                    return BadRequest(new { success = false, message });
                }

                TempData["error"] = message;
                return RedirectToAction(nameof(Courses));
            }

            var courseCode = course.CourseCode;
            var courseName = course.CourseName;
            _context.Courses.Remove(course);
            await _context.SaveChangesAsync();

            var successMessage = $"Course '{courseCode} - {courseName}' has been deleted successfully!";
            if (IsAjaxRequest())
            {
                return Ok(new { success = true, message = successMessage });
            }

            TempData["success"] = successMessage;
            return RedirectToAction(nameof(Courses));
        }

        // GET: Course/ExportToExcel
        [HttpGet]
        public async Task<IActionResult> ExportToExcel(string? courseCode, string? courseName, string? status)
        {
            var query = _context.Courses.AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(courseCode))
                query = query.Where(c => c.CourseCode.Contains(courseCode));
            if (!string.IsNullOrEmpty(courseName))
                query = query.Where(c => c.CourseName.Contains(courseName));
            if (!string.IsNullOrEmpty(status))
            {
                bool isActive = status == "Active";
                query = query.Where(c => c.IsActive == isActive);
            }

            var courses = await query.OrderBy(c => c.CourseCode).ToListAsync();

            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Courses");

            // Header row
            worksheet.Cell(1, 1).Value = "Course Code";
            worksheet.Cell(1, 2).Value = "Course Name";
            worksheet.Cell(1, 3).Value = "Credit Hours";
            worksheet.Cell(1, 4).Value = "Status";

            // Style header
            var headerRange = worksheet.Range(1, 1, 1, 4);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#4F46E5");
            headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;

            // Data rows
            int row = 2;
            foreach (var c in courses)
            {
                worksheet.Cell(row, 1).Value = c.CourseCode ?? "";
                worksheet.Cell(row, 2).Value = c.CourseName ?? "";
                worksheet.Cell(row, 3).Value = c.CreditHours;
                worksheet.Cell(row, 4).Value = c.IsActive == true ? "Active" : "Inactive";
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Courses_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        }

        // GET: Course/ExportToPdf
        [HttpGet]
        public async Task<IActionResult> ExportToPdf(string? courseCode, string? courseName, string? status)
        {
            var query = _context.Courses.AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(courseCode))
                query = query.Where(c => c.CourseCode.Contains(courseCode));
            if (!string.IsNullOrEmpty(courseName))
                query = query.Where(c => c.CourseName.Contains(courseName));
            if (!string.IsNullOrEmpty(status))
            {
                bool isActive = status == "Active";
                query = query.Where(c => c.IsActive == isActive);
            }

            var courses = await query.OrderBy(c => c.CourseCode).ToListAsync();

            using var stream = new MemoryStream();
            var document = new iTextSharp.text.Document(iTextSharp.text.PageSize.A4);
            iTextSharp.text.pdf.PdfWriter.GetInstance(document, stream);
            document.Open();

            var titleFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA_BOLD, 18);
            document.Add(new iTextSharp.text.Paragraph("Courses Report", titleFont));
            var smallFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA, 10);
            document.Add(new iTextSharp.text.Paragraph($"Generated on: {DateTime.Now:MMMM dd, yyyy HH:mm}", smallFont));
            document.Add(new iTextSharp.text.Paragraph(" "));

            var table = new iTextSharp.text.pdf.PdfPTable(4);
            table.WidthPercentage = 100;
            table.SetWidths(new float[] { 20f, 40f, 20f, 20f });

            var headerFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA_BOLD, 10, iTextSharp.text.BaseColor.WHITE);
            var headerBgColor = new iTextSharp.text.BaseColor(79, 70, 229);
            string[] headers = { "Course Code", "Course Name", "Credit Hours", "Status" };
            foreach (var header in headers)
            {
                var cell = new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase(header, headerFont));
                cell.BackgroundColor = headerBgColor;
                cell.Padding = 5;
                table.AddCell(cell);
            }

            var dataFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA, 9);
            foreach (var c in courses)
            {
                table.AddCell(new iTextSharp.text.Phrase(c.CourseCode ?? "", dataFont));
                table.AddCell(new iTextSharp.text.Phrase(c.CourseName ?? "", dataFont));
                table.AddCell(new iTextSharp.text.Phrase(c.CreditHours?.ToString() ?? "0", dataFont));
                table.AddCell(new iTextSharp.text.Phrase(c.IsActive == true ? "Active" : "Inactive", dataFont));
            }

            document.Add(table);
            document.Close();

            return File(stream.ToArray(), "application/pdf", $"Courses_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
        }
    }
}
