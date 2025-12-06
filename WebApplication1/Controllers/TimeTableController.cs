
using AMS.Models.Entities;
using AMS.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ClosedXML.Excel;
using iTextSharp.text;
using iTextSharp.text.pdf;
using AMS.Models;

namespace AMS.Controllers
{
    public class TimetableController : Controller
    {
        private readonly ApplicationDbContext _context;

        public TimetableController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Timetable
        public async Task<IActionResult> Index(TimetableFilterViewModel filter)
        {
            var query = _context.Timetables
                .Include(t => t.Batch)
                .Include(t => t.Semester)
                .AsQueryable();

            if (filter.SemesterId.HasValue)
            {
                query = query.Where(t => t.SemesterId == filter.SemesterId);
            }

            if (filter.BatchId.HasValue)
            {
                query = query.Where(t => t.BatchId == filter.BatchId);
            }

            if (!string.IsNullOrEmpty(filter.Status))
            {
                bool isActive = filter.Status == "Active";
                query = query.Where(t => t.IsActive == isActive);
            }

            filter.Timetables = await query.ToListAsync();

            // Get semesters with formatted display name (SemesterName Year)
            var semesters = await _context.Semesters
                .Select(s => new { s.SemesterId, DisplayName = s.SemesterName + " " + s.Year })
                .ToListAsync();
            ViewBag.Semesters = new SelectList(semesters, "SemesterId", "DisplayName");

            // Get batches with formatted display name (BatchName (Year))
            var batches = await _context.Batches
                .Select(b => new { b.BatchId, DisplayName = b.BatchName + " (" + b.Year + ")" })
                .ToListAsync();
            ViewBag.Batches = new SelectList(batches, "BatchId", "DisplayName");

            return View(filter);
        }

        // GET: Timetable/Create
        public async Task<IActionResult> Create()
        {
            var model = new AddTimetableViewModel();

            // Get batches with formatted display name (BatchName (Year))
            var batches = await _context.Batches
                .Select(b => new { b.BatchId, DisplayName = b.BatchName + " (" + b.Year + ")" })
                .ToListAsync();
            model.Batches = new SelectList(batches, "BatchId", "DisplayName");

            // Get semesters with formatted display name (SemesterName Year)
            var semesters = await _context.Semesters
                .Select(s => new { s.SemesterId, DisplayName = s.SemesterName + " " + s.Year })
                .ToListAsync();
            model.Semesters = new SelectList(semesters, "SemesterId", "DisplayName");

            return View(model);
        }

        // POST: Timetable/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AddTimetableViewModel model)
        {
            if (ModelState.IsValid)
            {
                // Check if timetable already exists for this batch and semester
                var existingTimetable = await _context.Timetables
                    .AnyAsync(t => t.BatchId == model.BatchId && t.SemesterId == model.SemesterId);

                if (existingTimetable)
                {
                    ModelState.AddModelError("", "A timetable already exists for this batch and semester. Please edit the existing one.");

                    var batchList = await _context.Batches
                        .Select(b => new { b.BatchId, DisplayName = b.BatchName + " (" + b.Year + ")" })
                        .ToListAsync();
                    model.Batches = new SelectList(batchList, "BatchId", "DisplayName");

                    var semList = await _context.Semesters
                        .Select(s => new { s.SemesterId, DisplayName = s.SemesterName + " " + s.Year })
                        .ToListAsync();
                    model.Semesters = new SelectList(semList, "SemesterId", "DisplayName");
                    return View(model);
                }

                var timetable = new Timetable
                {
                    BatchId = model.BatchId,
                    SemesterId = model.SemesterId,
                    IsActive = model.IsActive
                };

                _context.Add(timetable);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Edit), new { id = timetable.TimetableId });
            }

            // Re-populate dropdowns for validation failure
            var batchesList = await _context.Batches
                .Select(b => new { b.BatchId, DisplayName = b.BatchName + " (" + b.Year + ")" })
                .ToListAsync();
            model.Batches = new SelectList(batchesList, "BatchId", "DisplayName");

            var semestersList = await _context.Semesters
                .Select(s => new { s.SemesterId, DisplayName = s.SemesterName + " " + s.Year })
                .ToListAsync();
            model.Semesters = new SelectList(semestersList, "SemesterId", "DisplayName");
            return View(model);
        }

        // GET: Timetable/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var timetable = await _context.Timetables
                .Include(t => t.Batch)
                .Include(t => t.Semester)
                .Include(t => t.TimetableSlots)
                .ThenInclude(ts => ts.CourseAssignment)
                .ThenInclude(ca => ca.Course)
                .Include(t => t.TimetableSlots)
                .ThenInclude(ts => ts.CourseAssignment)
                .ThenInclude(ca => ca.Teacher)
                .FirstOrDefaultAsync(m => m.TimetableId == id);

            if (timetable == null)
            {
                return NotFound();
            }

            var model = new EditTimetableViewModel
            {
                TimetableId = timetable.TimetableId,
                BatchName = timetable.Batch?.BatchName,
                BatchYear = timetable.Batch?.Year,
                SemesterName = timetable.Semester?.SemesterName ?? timetable.Semester?.Year.ToString(),
                SemesterYear = timetable.Semester?.Year,
                IsActive = timetable.IsActive ?? false,
                Slots = timetable.TimetableSlots.Select(s => new TimetableSlotViewModel
                {
                    SlotId = s.SlotId,
                    CourseAssignmentId = s.CourseAssignmentId,
                    DayOfWeek = s.DayOfWeek ?? 0,
                    DayName = ((DayOfWeek)(s.DayOfWeek ?? 0)).ToString(),
                    StartTime = s.StartTime ?? default,
                    EndTime = s.EndTime ?? default,
                    CourseName = s.CourseAssignment?.Course?.CourseName ?? "Unknown",
                    TeacherName = s.CourseAssignment?.Teacher?.FirstName + " " + s.CourseAssignment?.Teacher?.LastName // Assuming Teacher properties
                }).ToList()
            };

            // Load CourseAssignments for the dropdown
            // We should only show assignments relevant to the semester/batch if possible, 
            // but usually CourseAssignment is linked to Semester.
            var assignments = await _context.CourseAssignments
                .Include(ca => ca.Course)
                .Include(ca => ca.Teacher)
                .Where(ca => ca.SemesterId == timetable.SemesterId && ca.BatchId == timetable.BatchId) // Filter by semester and batch
                .Select(ca => new
                {
                    ca.AssignmentId,
                    Name = ca.Course.CourseName + " - " + ca.Teacher.FirstName + " " + ca.Teacher.LastName
                })
                .ToListAsync();

            model.CourseAssignments = new SelectList(assignments, "AssignmentId", "Name");
            model.NewSlot.TimetableId = timetable.TimetableId;

            return View(model);
        }

        // POST: Timetable/AddSlot
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSlot(AddTimetableSlotViewModel newSlot)
        {
            if (ModelState.IsValid)
            {
                // Validate StartTime < EndTime
                if (newSlot.StartTime >= newSlot.EndTime)
                {
                    TempData["error"] = "Start time must be before end time.";
                    return RedirectToAction(nameof(Edit), new { id = newSlot.TimetableId });
                }

                // Check for overlapping slots
                var overlappingSlot = await _context.TimetableSlots
                    .Where(s => s.TimetableId == newSlot.TimetableId && s.DayOfWeek == newSlot.DayOfWeek)
                    .Where(s => s.StartTime < newSlot.EndTime && newSlot.StartTime < s.EndTime)
                    .FirstOrDefaultAsync();

                if (overlappingSlot != null)
                {
                    TempData["error"] = $"Time slot overlaps with an existing class ({overlappingSlot.StartTime} - {overlappingSlot.EndTime}).";
                    return RedirectToAction(nameof(Edit), new { id = newSlot.TimetableId });
                }

                var slot = new TimetableSlot
                {
                    TimetableId = newSlot.TimetableId,
                    CourseAssignmentId = newSlot.CourseAssignmentId,
                    DayOfWeek = newSlot.DayOfWeek,
                    StartTime = newSlot.StartTime,
                    EndTime = newSlot.EndTime
                };

                _context.Add(slot);
                await _context.SaveChangesAsync();
                TempData["success"] = "Slot added successfully.";
            }
            return RedirectToAction(nameof(Edit), new { id = newSlot.TimetableId });
        }

        // POST: Timetable/DeleteSlot/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSlot(int id)
        {
            var slot = await _context.TimetableSlots.FindAsync(id);
            if (slot != null)
            {
                int timetableId = slot.TimetableId ?? 0;
                _context.TimetableSlots.Remove(slot);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Edit), new { id = timetableId });
            }
            return RedirectToAction(nameof(Index));
        }

        // GET: Timetable/MyTimetable
        public async Task<IActionResult> MyTimetable(int? batchId, int? teacherId)
        {
            // Security: If Teacher, force teacherId to be their own
            if (User.IsInRole("Teacher"))
            {
                var userId = User.FindFirstValue("UserId");
                var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == int.Parse(userId));
                if (teacher == null) return RedirectToAction("Login", "Auth");

                teacherId = teacher.TeacherId;
                batchId = null; // Teachers shouldn't view by batch here, or we can allow it if we filter by their courses
            }

            if (batchId.HasValue)
            {
                var timetable = await _context.Timetables
                    .Include(t => t.Batch)
                    .Include(t => t.Semester)
                    .Include(t => t.TimetableSlots)
                    .ThenInclude(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Course)
                    .Include(t => t.TimetableSlots)
                    .ThenInclude(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Teacher)
                    .Where(t => t.BatchId == batchId && t.IsActive == true)
                    .FirstOrDefaultAsync();

                if (timetable != null)
                {
                    var model = new EditTimetableViewModel
                    {
                        TimetableId = timetable.TimetableId,
                        BatchName = timetable.Batch?.BatchName,
                        SemesterName = timetable.Semester?.Year.ToString(),
                        IsActive = timetable.IsActive ?? false,
                        Slots = timetable.TimetableSlots.Select(s => new TimetableSlotViewModel
                        {
                            SlotId = s.SlotId,
                            CourseAssignmentId = s.CourseAssignmentId,
                            DayOfWeek = s.DayOfWeek ?? 0,
                            DayName = ((DayOfWeek)(s.DayOfWeek ?? 0)).ToString(),
                            StartTime = s.StartTime ?? default,
                            EndTime = s.EndTime ?? default,
                            CourseName = s.CourseAssignment?.Course?.CourseName ?? "Unknown",
                            TeacherName = s.CourseAssignment?.Teacher?.FirstName + " " + s.CourseAssignment?.Teacher?.LastName
                        }).ToList()
                    };
                    return View("TimetableView", model);
                }
            }
            else if (teacherId.HasValue)
            {
                // For teacher, we need to find all slots across all active timetables
                var slots = await _context.TimetableSlots
                    .Include(ts => ts.Timetable)
                    .ThenInclude(t => t.Batch)
                    .Include(ts => ts.Timetable)
                    .ThenInclude(t => t.Semester)
                    .Include(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Course)
                    .Include(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Teacher)
                    .Where(ts => ts.CourseAssignment.TeacherId == teacherId && ts.Timetable.IsActive == true)
                    .ToListAsync();

                // Calculate attendance status for each slot
                var today = DateOnly.FromDateTime(DateTime.Now);
                var slotViewModels = new List<TimetableSlotViewModel>();

                foreach (var s in slots)
                {
                    var vm = new TimetableSlotViewModel
                    {
                        SlotId = s.SlotId,
                        CourseAssignmentId = s.CourseAssignmentId,
                        DayOfWeek = s.DayOfWeek ?? 0,
                        DayName = ((DayOfWeek)(s.DayOfWeek ?? 0)).ToString(),
                        StartTime = s.StartTime ?? default,
                        EndTime = s.EndTime ?? default,
                        CourseName = $"{s.CourseAssignment?.Course?.CourseName} ({s.Timetable?.Batch?.BatchName})",
                        TeacherName = "You"
                    };

                    // Determine relevant date (today or most recent past occurrence)
                    var targetDate = today;
                    int daysDiff = (int)today.DayOfWeek - (s.DayOfWeek ?? 0);
                    if (daysDiff < 0) daysDiff += 7;

                    // If today is the day, check today. If today is NOT the day, check last occurrence.
                    // Actually, if today is Mon and slot is Tue, last occurrence was 6 days ago.
                    // If today is Wed and slot is Tue, last occurrence was yesterday.
                    // Let's just check the most recent occurrence <= today.
                    targetDate = today.AddDays(-daysDiff);

                    // Check if session exists
                    var sessionExists = await _context.Sessions.AnyAsync(sess =>
                        sess.CourseAssignmentId == s.CourseAssignmentId &&
                        sess.SessionDate == targetDate &&
                        sess.StartTime == s.StartTime);

                    if (sessionExists)
                    {
                        vm.AttendanceStatus = "Marked";
                    }
                    else
                    {
                        // If the calculated date is today, it's Pending.
                        // If it's in the past, it's Pending (Overdue).
                        // But wait, if today is Mon and slot is Tue, targetDate is last Tue.
                        // We should probably check if the *next* occurrence is today?
                        // No, attendance is usually marked for the current week.
                        // Let's keep it simple: if session exists for the *latest possible date*, it's marked.
                        // Otherwise Pending.
                        vm.AttendanceStatus = "Pending";
                    }

                    slotViewModels.Add(vm);
                }

                var model = new EditTimetableViewModel
                {
                    BatchName = "Teacher Schedule", // Generic title
                    SemesterName = "All Active Semesters",
                    IsActive = true,
                    CanMarkAttendance = true,
                    Slots = slotViewModels
                };
                return View("TimetableView", model);
            }

            ViewBag.Batches = new SelectList(await _context.Batches.ToListAsync(), "BatchId", "BatchName");
            // Assuming we have a Teachers DbSet or Users with role Teacher. 
            // I'll assume Users for now or skip if I can't find it.
            // Let's check if we can find teachers. CourseAssignment has TeacherId (User).
            // I'll try to load users who are teachers.
            // Since I don't know the Role logic perfectly, I'll just load all users or skip.
            // Better to just stick to Batch for now to avoid errors, or try to load Users.
            // I'll comment out the Teacher selection part in the View if I can't load them easily.
            // But I can try to load from CourseAssignments unique teachers.
            var teacherIds = await _context.CourseAssignments.Select(ca => ca.TeacherId).Distinct().ToListAsync();
            var teachers = await _context.Teachers.Where(t => teacherIds.Contains(t.TeacherId)).Select(t => new { t.TeacherId, Name = t.FirstName + " " + t.LastName }).ToListAsync();
            ViewBag.Teachers = new SelectList(teachers, "TeacherId", "Name");

            return View("SelectTimetable");
        }

        // GET: Timetable/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var timetable = await _context.Timetables
                .Include(t => t.Batch)
                .Include(t => t.Semester)
                .Include(t => t.TimetableSlots)
                .ThenInclude(ts => ts.CourseAssignment)
                .ThenInclude(ca => ca.Course)
                .Include(t => t.TimetableSlots)
                .ThenInclude(ts => ts.CourseAssignment)
                .ThenInclude(ca => ca.Teacher)
                .FirstOrDefaultAsync(m => m.TimetableId == id);

            if (timetable == null)
            {
                return NotFound();
            }

            var model = new EditTimetableViewModel
            {
                TimetableId = timetable.TimetableId,
                BatchName = timetable.Batch?.BatchName,
                SemesterName = timetable.Semester?.SemesterName ?? timetable.Semester?.Year.ToString(),
                IsActive = timetable.IsActive ?? false,
                Slots = timetable.TimetableSlots.Select(s => new TimetableSlotViewModel
                {
                    SlotId = s.SlotId,
                    CourseAssignmentId = s.CourseAssignmentId,
                    DayOfWeek = s.DayOfWeek ?? 0,
                    DayName = ((DayOfWeek)(s.DayOfWeek ?? 0)).ToString(),
                    StartTime = s.StartTime ?? default,
                    EndTime = s.EndTime ?? default,
                    CourseName = s.CourseAssignment?.Course?.CourseName ?? "Unknown",
                    TeacherName = s.CourseAssignment?.Teacher?.FirstName + " " + s.CourseAssignment?.Teacher?.LastName
                }).ToList()
            };

            return View(model);
        }

        // POST: Timetable/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var timetable = await _context.Timetables.FindAsync(id);
            if (timetable != null)
            {
                _context.Timetables.Remove(timetable);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        // GET: Timetable/ExportToExcel - List export
        [HttpGet]
        public async Task<IActionResult> ExportToExcel(int? semesterId, int? batchId, string? status)
        {
            var query = _context.Timetables
                .Include(t => t.Batch)
                .Include(t => t.Semester)
                .Include(t => t.TimetableSlots)
                .AsQueryable();

            if (semesterId.HasValue && semesterId.Value > 0)
                query = query.Where(t => t.SemesterId == semesterId.Value);
            if (batchId.HasValue && batchId.Value > 0)
                query = query.Where(t => t.BatchId == batchId.Value);
            if (!string.IsNullOrEmpty(status))
            {
                bool isActive = status == "Active";
                query = query.Where(t => t.IsActive == isActive);
            }

            var timetables = await query.ToListAsync();

            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Timetables");

            worksheet.Cell(1, 1).Value = "Batch";
            worksheet.Cell(1, 2).Value = "Semester";
            worksheet.Cell(1, 3).Value = "Total Slots";
            worksheet.Cell(1, 4).Value = "Status";

            var headerRange = worksheet.Range(1, 1, 1, 4);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#4F46E5");
            headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;

            int row = 2;
            foreach (var t in timetables)
            {
                worksheet.Cell(row, 1).Value = $"{t.Batch?.BatchName} ({t.Batch?.Year})";
                worksheet.Cell(row, 2).Value = $"{t.Semester?.SemesterName} {t.Semester?.Year}";
                worksheet.Cell(row, 3).Value = t.TimetableSlots?.Count ?? 0;
                worksheet.Cell(row, 4).Value = t.IsActive == true ? "Active" : "Inactive";
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Timetables_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        }

        // GET: Timetable/ExportToPdf - List export
        [HttpGet]
        public async Task<IActionResult> ExportToPdf(int? semesterId, int? batchId, string? status)
        {
            var query = _context.Timetables
                .Include(t => t.Batch)
                .Include(t => t.Semester)
                .Include(t => t.TimetableSlots)
                .AsQueryable();

            if (semesterId.HasValue && semesterId.Value > 0)
                query = query.Where(t => t.SemesterId == semesterId.Value);
            if (batchId.HasValue && batchId.Value > 0)
                query = query.Where(t => t.BatchId == batchId.Value);
            if (!string.IsNullOrEmpty(status))
            {
                bool isActive = status == "Active";
                query = query.Where(t => t.IsActive == isActive);
            }

            var timetables = await query.ToListAsync();

            using var stream = new MemoryStream();
            var document = new iTextSharp.text.Document(iTextSharp.text.PageSize.A4);
            iTextSharp.text.pdf.PdfWriter.GetInstance(document, stream);
            document.Open();

            var titleFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA_BOLD, 18);
            document.Add(new iTextSharp.text.Paragraph("Timetables Report", titleFont));
            var smallFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA, 10);
            document.Add(new iTextSharp.text.Paragraph($"Generated on: {DateTime.Now:MMMM dd, yyyy HH:mm}", smallFont));
            document.Add(new iTextSharp.text.Paragraph($"Total Records: {timetables.Count}", smallFont));
            document.Add(new iTextSharp.text.Paragraph(" "));

            var table = new iTextSharp.text.pdf.PdfPTable(4);
            table.WidthPercentage = 100;
            table.SetWidths(new float[] { 30f, 30f, 20f, 20f });

            var headerFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA_BOLD, 10, iTextSharp.text.BaseColor.WHITE);
            var headerBgColor = new iTextSharp.text.BaseColor(79, 70, 229);
            string[] headers = { "Batch", "Semester", "Total Slots", "Status" };
            foreach (var header in headers)
            {
                var cell = new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase(header, headerFont));
                cell.BackgroundColor = headerBgColor;
                cell.Padding = 5;
                table.AddCell(cell);
            }

            var dataFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA, 9);
            foreach (var t in timetables)
            {
                table.AddCell(new iTextSharp.text.Phrase($"{t.Batch?.BatchName} ({t.Batch?.Year})", dataFont));
                table.AddCell(new iTextSharp.text.Phrase($"{t.Semester?.SemesterName} {t.Semester?.Year}", dataFont));
                table.AddCell(new iTextSharp.text.Phrase((t.TimetableSlots?.Count ?? 0).ToString(), dataFont));
                table.AddCell(new iTextSharp.text.Phrase(t.IsActive == true ? "Active" : "Inactive", dataFont));
            }

            document.Add(table);
            document.Close();

            return File(stream.ToArray(), "application/pdf", $"Timetables_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
        }

        // GET: Timetable/ExportTimetableToExcel/5 - Individual timetable export (actual schedule)
        [HttpGet]
        public async Task<IActionResult> ExportTimetableToExcel(int id)
        {
            var timetable = await _context.Timetables
                .Include(t => t.Batch)
                .Include(t => t.Semester)
                .Include(t => t.TimetableSlots)
                .ThenInclude(ts => ts.CourseAssignment)
                .ThenInclude(ca => ca.Course)
                .Include(t => t.TimetableSlots)
                .ThenInclude(ts => ts.CourseAssignment)
                .ThenInclude(ca => ca.Teacher)
                .FirstOrDefaultAsync(t => t.TimetableId == id);

            if (timetable == null) return NotFound();

            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Timetable");

            // Title
            worksheet.Cell(1, 1).Value = $"Class Timetable - {timetable.Batch?.BatchName} ({timetable.Batch?.Year})";
            worksheet.Range(1, 1, 1, 7).Merge();
            worksheet.Cell(1, 1).Style.Font.Bold = true;
            worksheet.Cell(1, 1).Style.Font.FontSize = 16;
            worksheet.Cell(1, 1).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

            worksheet.Cell(2, 1).Value = $"Semester: {timetable.Semester?.SemesterName} {timetable.Semester?.Year}";
            worksheet.Range(2, 1, 2, 7).Merge();
            worksheet.Cell(2, 1).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

            // Days header (Monday to Sunday)
            var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
            int col = 1;
            foreach (var day in days)
            {
                worksheet.Cell(4, col).Value = day.ToString();
                worksheet.Cell(4, col).Style.Font.Bold = true;
                worksheet.Cell(4, col).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#4F46E5");
                worksheet.Cell(4, col).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                worksheet.Cell(4, col).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                col++;
            }

            // Find max slots in any day to determine row count
            int maxSlots = days.Max(d => timetable.TimetableSlots.Count(s => s.DayOfWeek == (int)d));
            if (maxSlots == 0) maxSlots = 1;

            // Fill in slots
            for (int slotIndex = 0; slotIndex < maxSlots; slotIndex++)
            {
                int dataRow = 5 + slotIndex;
                col = 1;
                foreach (var day in days)
                {
                    var daySlots = timetable.TimetableSlots
                        .Where(s => s.DayOfWeek == (int)day)
                        .OrderBy(s => s.StartTime)
                        .ToList();

                    if (slotIndex < daySlots.Count)
                    {
                        var slot = daySlots[slotIndex];
                        var slotText = $"{slot.StartTime?.ToString("hh:mm tt")} - {slot.EndTime?.ToString("hh:mm tt")}\n" +
                                       $"{slot.CourseAssignment?.Course?.CourseName}\n" +
                                       $"{slot.CourseAssignment?.Teacher?.FirstName} {slot.CourseAssignment?.Teacher?.LastName}";
                        worksheet.Cell(dataRow, col).Value = slotText;
                        worksheet.Cell(dataRow, col).Style.Alignment.WrapText = true;
                        worksheet.Cell(dataRow, col).Style.Alignment.Vertical = ClosedXML.Excel.XLAlignmentVerticalValues.Top;
                    }
                    col++;
                }
                worksheet.Row(dataRow).Height = 60;
            }

            // Set column widths
            for (int i = 1; i <= 7; i++)
            {
                worksheet.Column(i).Width = 20;
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var fileName = $"Timetable_{timetable.Batch?.BatchName}_{timetable.Semester?.SemesterName}_{DateTime.Now:yyyyMMdd}.xlsx";
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        // GET: Timetable/ExportTimetableToPdf/5 - Individual timetable export (actual schedule)
        [HttpGet]
        public async Task<IActionResult> ExportTimetableToPdf(int id)
        {
            var timetable = await _context.Timetables
                .Include(t => t.Batch)
                .Include(t => t.Semester)
                .Include(t => t.TimetableSlots)
                .ThenInclude(ts => ts.CourseAssignment)
                .ThenInclude(ca => ca.Course)
                .Include(t => t.TimetableSlots)
                .ThenInclude(ts => ts.CourseAssignment)
                .ThenInclude(ca => ca.Teacher)
                .FirstOrDefaultAsync(t => t.TimetableId == id);

            if (timetable == null) return NotFound();

            using var stream = new MemoryStream();
            var document = new iTextSharp.text.Document(iTextSharp.text.PageSize.A4.Rotate(), 20, 20, 30, 30);
            iTextSharp.text.pdf.PdfWriter.GetInstance(document, stream);
            document.Open();

            // Title
            var titleFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA_BOLD, 18);
            var title = new iTextSharp.text.Paragraph("Class Timetable", titleFont);
            title.Alignment = iTextSharp.text.Element.ALIGN_CENTER;
            document.Add(title);

            var subtitleFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA, 12);
            var subtitle = new iTextSharp.text.Paragraph($"{timetable.Batch?.BatchName} ({timetable.Batch?.Year}) - {timetable.Semester?.SemesterName} {timetable.Semester?.Year}", subtitleFont);
            subtitle.Alignment = iTextSharp.text.Element.ALIGN_CENTER;
            document.Add(subtitle);

            document.Add(new iTextSharp.text.Paragraph(" "));

            // Table with 7 columns for days
            var table = new iTextSharp.text.pdf.PdfPTable(7);
            table.WidthPercentage = 100;
            table.SetWidths(new float[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f });

            // Header
            var headerFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA_BOLD, 10, iTextSharp.text.BaseColor.WHITE);
            var headerBgColor = new iTextSharp.text.BaseColor(79, 70, 229);
            var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };

            foreach (var day in days)
            {
                var cell = new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase(day.ToString(), headerFont));
                cell.BackgroundColor = headerBgColor;
                cell.HorizontalAlignment = iTextSharp.text.Element.ALIGN_CENTER;
                cell.Padding = 8;
                table.AddCell(cell);
            }

            // Find max slots
            int maxSlots = days.Max(d => timetable.TimetableSlots.Count(s => s.DayOfWeek == (int)d));
            if (maxSlots == 0) maxSlots = 1;

            var dataFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA, 8);
            var timeFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA_BOLD, 8, new iTextSharp.text.BaseColor(79, 70, 229));

            // Data rows
            for (int slotIndex = 0; slotIndex < maxSlots; slotIndex++)
            {
                foreach (var day in days)
                {
                    var daySlots = timetable.TimetableSlots
                        .Where(s => s.DayOfWeek == (int)day)
                        .OrderBy(s => s.StartTime)
                        .ToList();

                    var cell = new iTextSharp.text.pdf.PdfPCell();
                    cell.MinimumHeight = 60;
                    cell.Padding = 5;

                    if (slotIndex < daySlots.Count)
                    {
                        var slot = daySlots[slotIndex];

                        var timeText = new iTextSharp.text.Phrase($"{slot.StartTime?.ToString("hh:mm tt")} - {slot.EndTime?.ToString("hh:mm tt")}\n", timeFont);
                        var courseText = new iTextSharp.text.Phrase($"{slot.CourseAssignment?.Course?.CourseName}\n", dataFont);
                        var teacherText = new iTextSharp.text.Phrase($"{slot.CourseAssignment?.Teacher?.FirstName} {slot.CourseAssignment?.Teacher?.LastName}", dataFont);

                        var paragraph = new iTextSharp.text.Paragraph();
                        paragraph.Add(timeText);
                        paragraph.Add(courseText);
                        paragraph.Add(teacherText);
                        cell.AddElement(paragraph);

                        cell.BackgroundColor = new iTextSharp.text.BaseColor(249, 250, 251);
                    }
                    else
                    {
                        cell.AddElement(new iTextSharp.text.Paragraph("", dataFont));
                    }

                    table.AddCell(cell);
                }
            }

            document.Add(table);

            // Footer
            document.Add(new iTextSharp.text.Paragraph(" "));
            var footerFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA, 8, iTextSharp.text.BaseColor.GRAY);
            var footer = new iTextSharp.text.Paragraph($"Generated on {DateTime.Now:MMMM dd, yyyy} at {DateTime.Now:hh:mm tt}", footerFont);
            footer.Alignment = iTextSharp.text.Element.ALIGN_CENTER;
            document.Add(footer);

            document.Close();

            var fileName = $"Timetable_{timetable.Batch?.BatchName}_{timetable.Semester?.SemesterName}_{DateTime.Now:yyyyMMdd}.pdf";
            return File(stream.ToArray(), "application/pdf", fileName);
        }
    }
}
