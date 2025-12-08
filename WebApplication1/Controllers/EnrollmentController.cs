
using AMS.Helpers;
using AMS.Models;
using AMS.Models.Entities;
using AMS.Models.ViewModels;
using AMS.Services;
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
        private readonly IInstitutionService _institutionService;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public EnrollmentController(ApplicationDbContext context, IInstitutionService institutionService, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _institutionService = institutionService;
            _webHostEnvironment = webHostEnvironment;
        }

        // GET: Enrollment/Enrollments
        [HttpGet]
        public async Task<IActionResult> Enrollments(EnrollmentFilterViewModel filter)
        {
            var query = _context.Enrollments
                .Include(e => e.Student)
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

            if (filter.BatchId.HasValue && filter.BatchId.Value > 0)
            {
                query = query.Where(e => e.Student.BatchId == filter.BatchId.Value);
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
                    Text = $"{s.SemesterName} {s.Year}"
                })
                .ToListAsync();

            filter.Batches = await _context.Batches
                .Where(b => b.IsActive == true)
                .Select(b => new SelectListItem
                {
                    Value = b.BatchId.ToString(),
                    Text = $"{b.BatchName} ({b.Year})"
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
                    BatchId = model.BatchId,
                    Status = model.Status
                };

                _context.Enrollments.Add(enrollment);
                await _context.SaveChangesAsync();

                // Get names for success message
                var student = await _context.Students.FirstOrDefaultAsync(s => s.StudentId == model.StudentId);
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
                .Include(e => e.Student)
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
                BatchId = enrollment.BatchId,
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
                    enrollment.BatchId = model.BatchId;
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
                .Include(e => e.Student)
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
                    Text = $"{s.SemesterName} {s.Year}"
                })
                .ToListAsync();

            model.Batches = await _context.Batches
                .Where(b => b.IsActive == true)
                .Select(b => new SelectListItem
                {
                    Value = b.BatchId.ToString(),
                    Text = $"{b.BatchName} ({b.Year})"
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
                    text = s.SemesterName + " " + s.Year
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

        // DTO for Quick Enroll request
        public class QuickEnrollRequest
        {
            public int StudentId { get; set; }
            public int CourseId { get; set; }
            public int SemesterId { get; set; }
        }

        // GET: Enrollment/BulkEnroll
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
            model.Batches = new SelectList(await _context.Batches
                .Select(b => new { b.BatchId, Name = b.BatchName + " (" + b.Year + ")" })
                .ToListAsync(), "BatchId", "Name");

            model.Semesters = new SelectList(await _context.Semesters
                .Where(s => s.IsActive == true)
                .Select(s => new { s.SemesterId, Name = s.SemesterName + " " + s.Year })
                .ToListAsync(), "SemesterId", "Name");

            model.Courses = new SelectList(await _context.Courses
                .Where(c => c.IsActive == true)
                .Select(c => new { c.CourseId, Name = c.CourseCode + " - " + c.CourseName })
                .ToListAsync(), "CourseId", "Name");
        }

        // GET: Enrollment/ExportToExcel
        [HttpGet]
        public async Task<IActionResult> ExportToExcel(int? studentId, int? courseId, int? semesterId, int? batchId, string? status)
        {
            var query = _context.Enrollments
                .Include(e => e.Student).ThenInclude(s => s.Batch)
                .Include(e => e.Course)
                .Include(e => e.Semester)
                .AsQueryable();

            // Apply same filters as the list view
            if (studentId.HasValue && studentId.Value > 0)
                query = query.Where(e => e.StudentId == studentId.Value);
            if (courseId.HasValue && courseId.Value > 0)
                query = query.Where(e => e.CourseId == courseId.Value);
            if (semesterId.HasValue && semesterId.Value > 0)
                query = query.Where(e => e.SemesterId == semesterId.Value);
            if (batchId.HasValue && batchId.Value > 0)
                query = query.Where(e => e.Student.BatchId == batchId.Value);
            if (!string.IsNullOrEmpty(status))
                query = query.Where(e => e.Status == status);

            var enrollments = await query.OrderBy(e => e.Student.FirstName).ToListAsync();

            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Enrollments");

            // Header row
            worksheet.Cell(1, 1).Value = "Student Name";
            worksheet.Cell(1, 2).Value = "Roll Number";
            worksheet.Cell(1, 3).Value = "Batch";
            worksheet.Cell(1, 4).Value = "Course Code";
            worksheet.Cell(1, 5).Value = "Course Name";
            worksheet.Cell(1, 6).Value = "Semester";
            worksheet.Cell(1, 7).Value = "Status";

            // Style header
            var headerRange = worksheet.Range(1, 1, 1, 7);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#4F46E5");
            headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;

            // Data rows
            int row = 2;
            foreach (var e in enrollments)
            {
                worksheet.Cell(row, 1).Value = $"{e.Student?.FirstName} {e.Student?.LastName}";
                worksheet.Cell(row, 2).Value = e.Student?.RollNumber ?? "";
                worksheet.Cell(row, 3).Value = $"{e.Student?.Batch?.BatchName} ({e.Student?.Batch?.Year})";
                worksheet.Cell(row, 4).Value = e.Course?.CourseCode ?? "";
                worksheet.Cell(row, 5).Value = e.Course?.CourseName ?? "";
                worksheet.Cell(row, 6).Value = $"{e.Semester?.SemesterName} {e.Semester?.Year}";
                worksheet.Cell(row, 7).Value = e.Status ?? "";
                row++;
            }

            // Auto-fit columns
            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var content = stream.ToArray();

            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Enrollments_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        }

        // GET: Enrollment/ExportToPdf
        [HttpGet]
        public async Task<IActionResult> ExportToPdf(int? studentId, int? courseId, int? semesterId, int? batchId, string? status)
        {
            var query = _context.Enrollments
                .Include(e => e.Student).ThenInclude(s => s.Batch)
                .Include(e => e.Course)
                .Include(e => e.Semester)
                .AsQueryable();

            // Apply same filters as the list view
            if (studentId.HasValue && studentId.Value > 0)
                query = query.Where(e => e.StudentId == studentId.Value);
            if (courseId.HasValue && courseId.Value > 0)
                query = query.Where(e => e.CourseId == courseId.Value);
            if (semesterId.HasValue && semesterId.Value > 0)
                query = query.Where(e => e.SemesterId == semesterId.Value);
            if (batchId.HasValue && batchId.Value > 0)
                query = query.Where(e => e.Student.BatchId == batchId.Value);
            if (!string.IsNullOrEmpty(status))
                query = query.Where(e => e.Status == status);

            var enrollments = await query.OrderBy(e => e.Student.FirstName).ToListAsync();
            var institution = await _institutionService.GetInstitutionInfoAsync();

            using var stream = new MemoryStream();
            var document = new iTextSharp.text.Document(iTextSharp.text.PageSize.A4.Rotate(), 40, 40, 70, 60);
            var writer = iTextSharp.text.pdf.PdfWriter.GetInstance(document, stream);
            writer.PageEvent = new PdfHeaderFooter(institution, _webHostEnvironment);
            document.Open();

            // Title
            var titleFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA_BOLD, 18);
            document.Add(new iTextSharp.text.Paragraph("Enrollments Report", titleFont));
            var smallFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA, 10);
            document.Add(new iTextSharp.text.Paragraph($"Generated on: {DateTime.Now:MMMM dd, yyyy HH:mm}", smallFont));
            document.Add(new iTextSharp.text.Paragraph($"Total Records: {enrollments.Count}", smallFont));
            document.Add(new iTextSharp.text.Paragraph(" "));

            // Table
            var table = new iTextSharp.text.pdf.PdfPTable(7);
            table.WidthPercentage = 100;
            table.SetWidths(new float[] { 15f, 10f, 12f, 10f, 18f, 12f, 8f });

            // Header
            var headerFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA_BOLD, 10, iTextSharp.text.BaseColor.WHITE);
            var headerBgColor = new iTextSharp.text.BaseColor(79, 70, 229);
            string[] headers = { "Student Name", "Roll Number", "Batch", "Course Code", "Course Name", "Semester", "Status" };
            foreach (var header in headers)
            {
                var cell = new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase(header, headerFont));
                cell.BackgroundColor = headerBgColor;
                cell.Padding = 5;
                table.AddCell(cell);
            }

            // Data
            var dataFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA, 9);
            foreach (var e in enrollments)
            {
                table.AddCell(new iTextSharp.text.Phrase($"{e.Student?.FirstName} {e.Student?.LastName}", dataFont));
                table.AddCell(new iTextSharp.text.Phrase(e.Student?.RollNumber ?? "", dataFont));
                table.AddCell(new iTextSharp.text.Phrase($"{e.Student?.Batch?.BatchName} ({e.Student?.Batch?.Year})", dataFont));
                table.AddCell(new iTextSharp.text.Phrase(e.Course?.CourseCode ?? "", dataFont));
                table.AddCell(new iTextSharp.text.Phrase(e.Course?.CourseName ?? "", dataFont));
                table.AddCell(new iTextSharp.text.Phrase($"{e.Semester?.SemesterName} {e.Semester?.Year}", dataFont));
                table.AddCell(new iTextSharp.text.Phrase(e.Status ?? "", dataFont));
            }

            document.Add(table);
            document.Close();

            return File(stream.ToArray(), "application/pdf", $"Enrollments_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
        }
    }
}
