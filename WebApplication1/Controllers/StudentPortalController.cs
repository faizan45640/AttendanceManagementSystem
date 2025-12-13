using AMS.Models;
using AMS.Helpers;
using AMS.Models.Entities;
using AMS.Models.ViewModels;
using AMS.Services;
using ClosedXML.Excel;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AMS.Controllers
{
    [Authorize(Roles = "Student")]
    public class StudentPortalController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IInstitutionService _institutionService;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public StudentPortalController(ApplicationDbContext context, IInstitutionService institutionService, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _institutionService = institutionService;
            _webHostEnvironment = webHostEnvironment;
        }

        private async Task<Student?> GetCurrentStudentAsync()
        {
            var userIdStr = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userIdStr)) return null;

            if (!int.TryParse(userIdStr, out int userId)) return null;

            return await _context.Students
                .Include(s => s.User)
                .Include(s => s.Batch)
                .FirstOrDefaultAsync(s => s.UserId == userId);
        }

        public async Task<IActionResult> Dashboard()
        {
            var student = await GetCurrentStudentAsync();
            if (student == null) return RedirectToAction("Login", "Auth");

            var model = new StudentDashboardViewModel
            {
                StudentName = $"{student.FirstName} {student.LastName}",
                RollNumber = student.RollNumber,
                BatchName = student.Batch?.BatchName ?? "N/A"
            };

            // 1. Calculate Overall Stats
            // Get all enrollments to find relevant courses
            var enrollments = await _context.Enrollments
                .Where(e => e.StudentId == student.StudentId && e.Status == "Active")
                .ToListAsync();

            var courseIds = enrollments.Select(e => e.CourseId).ToList();

            // Get all attendance records for this student
            var attendances = await _context.Attendances
                .Include(a => a.Session)
                .ThenInclude(s => s.CourseAssignment)
                .ThenInclude(ca => ca.Course)
                .Where(a => a.StudentId == student.StudentId && a.Session.CourseAssignment.Course != null)
                .ToListAsync();

            if (attendances.Any())
            {
                model.TotalClasses = attendances.Count;
                model.PresentClasses = attendances.Count(a => a.Status == "Present");
                model.AbsentClasses = attendances.Count(a => a.Status == "Absent");
                model.OverallAttendancePercentage = (double)model.PresentClasses / model.TotalClasses * 100;
            }

            // 2. Get Today's Schedule
            var today = DateOnly.FromDateTime(DateTime.Now);
            var dayOfWeek = (int)today.DayOfWeek;

            // Find active timetable for student's batch (or enrolled courses)
            // Logic similar to AttendanceController.StudentReport

            // First, try to find timetable for current semester
            var currentSemester = await _context.Semesters
                .FirstOrDefaultAsync(s => s.StartDate <= today && s.EndDate >= today && s.IsActive == true);

            if (currentSemester != null)
            {
                // Get slots for today
                // We need to check slots for the student's batch AND any cross-batch enrollments

                // A. Student's Batch Timetable
                var batchSlots = await _context.TimetableSlots
                    .Include(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Course)
                    .Include(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Teacher)
                    .Where(ts => ts.Timetable.BatchId == student.BatchId &&
                                 ts.Timetable.SemesterId == currentSemester.SemesterId &&
                                 ts.Timetable.IsActive == true &&
                                 ts.DayOfWeek == dayOfWeek)
                    .ToListAsync();

                // B. Cross-Batch Enrollments
                // If student is enrolled in a course with a specific BatchId, we should look for THAT batch's timetable
                var crossBatchEnrollments = enrollments.Where(e => e.BatchId.HasValue && e.BatchId != student.BatchId).ToList();

                foreach (var enrollment in crossBatchEnrollments)
                {
                    var crossSlots = await _context.TimetableSlots
                        .Include(ts => ts.CourseAssignment)
                        .ThenInclude(ca => ca.Course)
                        .Include(ts => ts.CourseAssignment)
                        .ThenInclude(ca => ca.Teacher)
                        .Where(ts => ts.Timetable.BatchId == enrollment.BatchId &&
                                     ts.Timetable.SemesterId == currentSemester.SemesterId &&
                                     ts.Timetable.IsActive == true &&
                                     ts.DayOfWeek == dayOfWeek &&
                                     ts.CourseAssignment.CourseId == enrollment.CourseId) // Only for the specific course
                        .ToListAsync();

                    batchSlots.AddRange(crossSlots);
                }

                // Filter out slots for courses the student is NOT enrolled in (if we want to be strict)
                // But usually batch timetable applies to all unless dropped.
                // Let's keep it simple: Show all slots for the batch + cross batch slots.

                // Sort by time
                batchSlots = batchSlots.OrderBy(s => s.StartTime).ToList();

                foreach (var slot in batchSlots)
                {
                    // Check if attendance is already marked for today
                    var attendance = attendances.FirstOrDefault(a =>
                        a.Session.SessionDate == today &&
                        a.Session.CourseAssignmentId == slot.CourseAssignmentId &&
                        a.Session.StartTime == slot.StartTime);

                    var status = "Scheduled";
                    var color = "text-blue-600 bg-blue-50 dark:text-blue-400 dark:bg-blue-900/20";

                    if (attendance != null)
                    {
                        status = attendance.Status;
                        if (status == "Present") color = "text-green-600 bg-green-50 dark:text-green-400 dark:bg-green-900/20";
                        else if (status == "Absent") color = "text-red-600 bg-red-50 dark:text-red-400 dark:bg-red-900/20";
                    }

                    model.TodayClasses.Add(new ClassSessionViewModel
                    {
                        CourseName = slot.CourseAssignment?.Course?.CourseName ?? "Unknown",
                        TeacherName = $"{slot.CourseAssignment?.Teacher?.FirstName} {slot.CourseAssignment?.Teacher?.LastName}",
                        StartTime = slot.StartTime ?? default,
                        EndTime = slot.EndTime ?? default,
                        Status = status,
                        StatusColor = color
                    });
                }
            }

            // 3. Generate Alerts
            // Group attendance by course
            var courseAttendance = attendances
                .GroupBy(a => a.Session.CourseAssignment.CourseId)
                .Select(g => new
                {
                    CourseId = g.Key,
                    CourseName = g.First().Session.CourseAssignment.Course.CourseName,
                    Total = g.Count(),
                    Present = g.Count(a => a.Status == "Present"),
                    Percentage = (double)g.Count(a => a.Status == "Present") / g.Count() * 100
                })
                .ToList();

            foreach (var ca in courseAttendance)
            {
                if (ca.Percentage < 75)
                {
                    model.Alerts.Add($"Warning: Your attendance in {ca.CourseName} is {ca.Percentage:F1}% (Below 75%)");
                }
            }

            return View(model);
        }

        public async Task<IActionResult> MyAttendance(int? semesterId)
        {
            var student = await GetCurrentStudentAsync();
            if (student == null) return RedirectToAction("Login", "Auth");

            // Reuse the logic from AttendanceController.StudentReport but restricted to current student
            // We can redirect to that controller action if we want, but better to have a dedicated view for students
            // Or we can instantiate the AttendanceController? No, that's bad practice.
            // Let's copy the relevant logic or refactor into a service. For now, copy-paste-modify is safer to avoid breaking existing code.

            var model = new StudentAttendanceReportViewModel
            {
                SelectedStudentId = student.StudentId,
                StudentName = $"{student.FirstName} {student.LastName}",
                RollNumber = student.RollNumber,
                BatchName = student.Batch?.BatchName,
                SelectedSemesterId = semesterId
            };

            // Load Semesters for dropdown
            model.SemesterList = await _context.Semesters
                .Select(s => new SelectListItem { Value = s.SemesterId.ToString(), Text = s.SemesterName + " (" + s.Year + ")", Selected = s.SemesterId == semesterId })
                .ToListAsync();

            // Get Attendance
            var attendanceQuery = _context.Attendances
                .Include(a => a.Session)
                .ThenInclude(s => s.CourseAssignment)
                .ThenInclude(ca => ca.Course)
                .Include(a => a.Session)
                .ThenInclude(s => s.CourseAssignment)
                .ThenInclude(ca => ca.Semester)
                .Include(a => a.Session)
                .ThenInclude(s => s.CourseAssignment)
                .ThenInclude(ca => ca.Teacher)
                .Where(a => a.StudentId == student.StudentId && a.Session.CourseAssignment.Course != null);

            if (semesterId.HasValue)
            {
                attendanceQuery = attendanceQuery.Where(a => a.Session.CourseAssignment.SemesterId == semesterId);
            }

            var attendances = await attendanceQuery
                .OrderByDescending(a => a.Session.SessionDate)
                .ToListAsync();

            // Group by Course with AssignmentId
            model.Courses = attendances.GroupBy(a => new
            {
                a.Session.CourseAssignmentId,
                a.Session.CourseAssignment.CourseId,
                a.Session.CourseAssignment.Course.CourseName,
                a.Session.CourseAssignment.Semester.SemesterName,
                a.Session.CourseAssignment.Semester.Year,
                a.Session.CourseAssignment.Teacher.FirstName,
                a.Session.CourseAssignment.Teacher.LastName
            })
            .Select(g => new StudentCourseAttendanceViewModel
            {
                CourseId = g.Key.CourseId ?? 0,
                AssignmentId = g.Key.CourseAssignmentId ?? 0,
                CourseName = g.Key.CourseName,
                SemesterName = $"{g.Key.SemesterName} ({g.Key.Year})",
                TeacherName = $"{g.Key.FirstName} {g.Key.LastName}",
                TotalSessions = g.Count(),
                PresentSessions = g.Count(a => a.Status == "Present"),
                LateSessions = g.Count(a => a.Status == "Late"),
                ExcusedSessions = g.Count(a => a.Status == "Excused"),
                AbsentSessions = g.Count(a => a.Status == "Absent"),
                // Percentage: Present + Late + Excused count as attended
                Percentage = g.Count() > 0 ? (double)(g.Count(a => a.Status == "Present") + g.Count(a => a.Status == "Late") + g.Count(a => a.Status == "Excused")) / g.Count() * 100 : 0,
                History = g.Select(a => new AttendanceRecordViewModel
                {
                    Date = a.Session.SessionDate,
                    Status = a.Status,
                    StartTime = a.Session.StartTime,
                    EndTime = a.Session.EndTime
                }).OrderByDescending(h => h.Date).ToList()
            }).ToList();

            return View(model);
        }

        public async Task<IActionResult> MyTimetable()
        {
            var student = await GetCurrentStudentAsync();
            if (student == null) return RedirectToAction("Login", "Auth");

            // Get Active Timetable
            // Logic: Get Student's Batch Timetable + Any Cross-Batch Course Slots

            var today = DateOnly.FromDateTime(DateTime.Now);
            var currentSemester = await _context.Semesters
                .FirstOrDefaultAsync(s => s.StartDate <= today && s.EndDate >= today && s.IsActive == true);

            var batch = await _context.Batches.FindAsync(student.BatchId);
            ViewBag.BatchName = batch?.BatchName ?? "N/A";
            ViewBag.SemesterName = currentSemester != null ? $"{currentSemester.SemesterName} ({currentSemester.Year})" : "N/A";

            if (currentSemester == null)
            {
                return View(new List<TimetableSlot>()); // Empty view
            }

            var slots = await _context.TimetableSlots
                .Include(ts => ts.CourseAssignment)
                .ThenInclude(ca => ca.Course)
                .Include(ts => ts.CourseAssignment)
                .ThenInclude(ca => ca.Teacher)
                .Where(ts => ts.Timetable.BatchId == student.BatchId &&
                             ts.Timetable.SemesterId == currentSemester.SemesterId &&
                             ts.Timetable.IsActive == true)
                .ToListAsync();

            // Add Cross-Batch Slots
            var enrollments = await _context.Enrollments
                .Where(e => e.StudentId == student.StudentId && e.Status == "Active" && e.BatchId.HasValue && e.BatchId != student.BatchId)
                .ToListAsync();

            foreach (var enrollment in enrollments)
            {
                var crossSlots = await _context.TimetableSlots
                    .Include(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Course)
                    .Include(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Teacher)
                    .Where(ts => ts.Timetable.BatchId == enrollment.BatchId &&
                                 ts.Timetable.SemesterId == currentSemester.SemesterId &&
                                 ts.Timetable.IsActive == true &&
                                 ts.CourseAssignment.CourseId == enrollment.CourseId)
                    .ToListAsync();

                slots.AddRange(crossSlots);
            }

            return View(slots.OrderBy(s => s.DayOfWeek).ThenBy(s => s.StartTime).ToList());
        }

        // NOTE: MyCourses is implemented in the Student Self-Enrollment section below.

        // ============== PDF EXPORT METHODS ==============

        public async Task<IActionResult> ExportMyTimetableToPdf()
        {
            var student = await GetCurrentStudentAsync();
            if (student == null) return RedirectToAction("Login", "Auth");

            var today = DateOnly.FromDateTime(DateTime.Now);
            var currentSemester = await _context.Semesters
                .FirstOrDefaultAsync(s => s.StartDate <= today && s.EndDate >= today && s.IsActive == true);

            var batch = await _context.Batches.FindAsync(student.BatchId);
            string batchName = batch?.BatchName ?? "N/A";
            string semesterName = currentSemester != null ? $"{currentSemester.SemesterName} ({currentSemester.Year})" : "N/A";

            // Get slots same as MyTimetable
            var slots = new List<TimetableSlot>();
            if (currentSemester != null)
            {
                var enrollments = await _context.Enrollments
                    .Include(e => e.Course)
                    .Where(e => e.StudentId == student.StudentId && e.Status == "Active")
                    .ToListAsync();

                foreach (var enrollment in enrollments)
                {
                    var targetBatchId = enrollment.BatchId ?? student.BatchId;
                    var crossSlots = await _context.TimetableSlots
                        .Include(ts => ts.CourseAssignment)
                        .ThenInclude(ca => ca.Course)
                        .Include(ts => ts.CourseAssignment)
                        .ThenInclude(ca => ca.Teacher)
                        .Where(ts => ts.Timetable.BatchId == targetBatchId &&
                                     ts.Timetable.SemesterId == currentSemester.SemesterId &&
                                     ts.Timetable.IsActive == true &&
                                     ts.CourseAssignment.CourseId == enrollment.CourseId)
                        .ToListAsync();
                    slots.AddRange(crossSlots);
                }
            }

            var orderedSlots = slots.OrderBy(s => s.DayOfWeek).ThenBy(s => s.StartTime).ToList();

            using var memoryStream = new MemoryStream();
            var document = new Document(PageSize.A4.Rotate(), 36, 36, 100, 50);
            var writer = PdfWriter.GetInstance(document, memoryStream);

            var institutionInfo = await _institutionService.GetInstitutionInfoAsync();

            writer.PageEvent = new PdfHeaderFooter(institutionInfo, _webHostEnvironment);

            document.Open();

            // Title
            var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16, BaseColor.DARK_GRAY);
            var title = new Paragraph($"Weekly Timetable - {student.FirstName} {student.LastName}", titleFont)
            {
                Alignment = Element.ALIGN_CENTER,
                SpacingAfter = 5
            };
            document.Add(title);

            // Subtitle
            var subtitleFont = FontFactory.GetFont(FontFactory.HELVETICA, 11, BaseColor.GRAY);
            var subtitle = new Paragraph($"Batch: {batchName} | Semester: {semesterName}", subtitleFont)
            {
                Alignment = Element.ALIGN_CENTER,
                SpacingAfter = 15
            };
            document.Add(subtitle);

            // Create table for timetable grid
            var table = new PdfPTable(7) { WidthPercentage = 100 };
            table.SetWidths(new float[] { 1, 1, 1, 1, 1, 1, 1 });

            var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10, BaseColor.WHITE);
            var cellFont = FontFactory.GetFont(FontFactory.HELVETICA, 9, BaseColor.DARK_GRAY);
            var smallFont = FontFactory.GetFont(FontFactory.HELVETICA, 7, BaseColor.GRAY);

            string[] days = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
            var headerColor = new BaseColor(37, 99, 235); // Blue

            // Header row
            foreach (var day in days)
            {
                var cell = new PdfPCell(new Phrase(day, headerFont))
                {
                    BackgroundColor = headerColor,
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    Padding = 8,
                    BorderWidth = 0.5f
                };
                table.AddCell(cell);
            }

            // Group slots by day
            var slotsByDay = orderedSlots.GroupBy(s => s.DayOfWeek).ToDictionary(g => g.Key, g => g.ToList());

            // Find max slots in any day for row count
            int maxSlotsPerDay = slotsByDay.Any() ? slotsByDay.Max(d => d.Value.Count) : 1;

            // Add rows
            for (int row = 0; row < Math.Max(maxSlotsPerDay, 1); row++)
            {
                for (int day = 0; day < 7; day++)
                {
                    var daySlots = slotsByDay.ContainsKey(day) ? slotsByDay[day] : new List<TimetableSlot>();
                    if (row < daySlots.Count)
                    {
                        var slot = daySlots[row];
                        var cellContent = new Phrase();
                        cellContent.Add(new Chunk($"{slot.CourseAssignment.Course.CourseCode}\n", cellFont));
                        cellContent.Add(new Chunk($"{slot.StartTime:hh\\:mm} - {slot.EndTime:hh\\:mm}", smallFont));

                        var cell = new PdfPCell(cellContent)
                        {
                            HorizontalAlignment = Element.ALIGN_CENTER,
                            VerticalAlignment = Element.ALIGN_MIDDLE,
                            Padding = 6,
                            MinimumHeight = 50,
                            BorderWidth = 0.5f,
                            BackgroundColor = new BaseColor(248, 250, 252)
                        };
                        table.AddCell(cell);
                    }
                    else
                    {
                        var cell = new PdfPCell(new Phrase("-", smallFont))
                        {
                            HorizontalAlignment = Element.ALIGN_CENTER,
                            VerticalAlignment = Element.ALIGN_MIDDLE,
                            Padding = 6,
                            MinimumHeight = 50,
                            BorderWidth = 0.5f
                        };
                        table.AddCell(cell);
                    }
                }
            }

            document.Add(table);

            // Course Legend
            document.Add(new Paragraph("\n"));
            var legendTitle = new Paragraph("Course Legend:", FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10, BaseColor.DARK_GRAY))
            {
                SpacingAfter = 5
            };
            document.Add(legendTitle);

            var courses = orderedSlots.Select(s => s.CourseAssignment)
                .GroupBy(ca => ca.CourseId)
                .Select(g => g.First())
                .ToList();

            foreach (var ca in courses)
            {
                var legendItem = new Paragraph($"• {ca.Course.CourseCode} - {ca.Course.CourseName} ({ca.Teacher?.FirstName} {ca.Teacher?.LastName})", smallFont);
                document.Add(legendItem);
            }

            // Generated date
            document.Add(new Paragraph("\n"));
            var dateFont = FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 8, BaseColor.GRAY);
            var datePara = new Paragraph($"Generated on: {DateTime.Now:MMMM dd, yyyy hh:mm tt}", dateFont)
            {
                Alignment = Element.ALIGN_RIGHT
            };
            document.Add(datePara);

            document.Close();

            return File(memoryStream.ToArray(), "application/pdf", $"Timetable_{student.FirstName}_{student.LastName}.pdf");
        }

        public async Task<IActionResult> ExportMyAttendanceToPdf(int? semesterId)
        {
            var student = await GetCurrentStudentAsync();
            if (student == null) return RedirectToAction("Login", "Auth");

            var today = DateOnly.FromDateTime(DateTime.Now);
            Semester currentSemester;

            if (semesterId.HasValue)
            {
                currentSemester = await _context.Semesters.FindAsync(semesterId.Value);
            }
            else
            {
                currentSemester = await _context.Semesters
                    .FirstOrDefaultAsync(s => s.StartDate <= today && s.EndDate >= today && s.IsActive == true);
            }

            string semesterName = currentSemester != null ? $"{currentSemester.SemesterName} ({currentSemester.Year})" : "N/A";
            var batch = await _context.Batches.FindAsync(student.BatchId);
            string batchName = batch?.BatchName ?? "N/A";

            // Get attendance data same as MyAttendance
            var attendanceData = new List<dynamic>();

            if (currentSemester != null)
            {
                var enrollments = await _context.Enrollments
                    .Include(e => e.Course)
                    .Where(e => e.StudentId == student.StudentId && e.Status == "Active")
                    .ToListAsync();

                foreach (var enrollment in enrollments)
                {
                    var targetBatchId = enrollment.BatchId ?? student.BatchId;
                    var assignment = await _context.CourseAssignments
                        .Include(ca => ca.Teacher)
                        .FirstOrDefaultAsync(ca => ca.CourseId == enrollment.CourseId &&
                                                   ca.BatchId == targetBatchId &&
                                                   ca.SemesterId == currentSemester.SemesterId);

                    if (assignment != null)
                    {
                        var attendances = await _context.Attendances
                            .Include(a => a.Session)
                            .Where(a => a.StudentId == student.StudentId &&
                                        a.Session.CourseAssignmentId == assignment.AssignmentId)
                            .OrderByDescending(a => a.Session.SessionDate)
                            .ThenByDescending(a => a.Session.StartTime)
                            .ToListAsync();

                        var total = attendances.Count;
                        var present = attendances.Count(a => a.Status == "Present");
                        var late = attendances.Count(a => a.Status == "Late");
                        var excused = attendances.Count(a => a.Status == "Excused");
                        var absent = attendances.Count(a => a.Status == "Absent");
                        // For percentage calculation, Present + Late + Excused count as attendance
                        var effectivePresent = present + late + excused;
                        var percentage = total > 0 ? (double)effectivePresent / total * 100 : 0;

                        attendanceData.Add(new
                        {
                            CourseId = enrollment.CourseId,
                            CourseName = enrollment.Course.CourseName,
                            CourseCode = enrollment.Course.CourseCode,
                            TeacherName = assignment.Teacher != null ? $"{assignment.Teacher.FirstName} {assignment.Teacher.LastName}" : "N/A",
                            AssignmentId = assignment.AssignmentId,
                            Total = total,
                            Present = present,
                            Late = late,
                            Excused = excused,
                            Absent = absent,
                            EffectivePresent = effectivePresent,
                            Percentage = percentage,
                            History = attendances.Take(10).Select(a => new
                            {
                                Date = a.Session.SessionDate.ToString("MMM dd"),
                                Status = a.Status
                            }).ToList()
                        });
                    }
                }
            }

            // Calculate overall stats
            int overallTotal = attendanceData.Sum(d => (int)d.Total);
            int overallPresent = attendanceData.Sum(d => (int)d.Present);
            int overallLate = attendanceData.Sum(d => (int)d.Late);
            int overallExcused = attendanceData.Sum(d => (int)d.Excused);
            int overallAbsent = attendanceData.Sum(d => (int)d.Absent);
            int overallEffectivePresent = attendanceData.Sum(d => (int)d.EffectivePresent);
            double overallPercentage = overallTotal > 0 ? (double)overallEffectivePresent / overallTotal * 100 : 0;

            using var memoryStream = new MemoryStream();
            var document = new Document(PageSize.A4, 36, 36, 100, 50);
            var writer = PdfWriter.GetInstance(document, memoryStream);

            var institutionInfo = await _institutionService.GetInstitutionInfoAsync();

            writer.PageEvent = new PdfHeaderFooter(institutionInfo, _webHostEnvironment);

            document.Open();

            // Title
            var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16, BaseColor.DARK_GRAY);
            var title = new Paragraph($"Attendance Report - {student.FirstName} {student.LastName}", titleFont)
            {
                Alignment = Element.ALIGN_CENTER,
                SpacingAfter = 5
            };
            document.Add(title);

            // Subtitle
            var subtitleFont = FontFactory.GetFont(FontFactory.HELVETICA, 11, BaseColor.GRAY);
            var subtitle = new Paragraph($"Roll No: {student.RollNumber} | Batch: {batchName} | Semester: {semesterName}", subtitleFont)
            {
                Alignment = Element.ALIGN_CENTER,
                SpacingAfter = 15
            };
            document.Add(subtitle);

            // Overall Summary Box - 6 columns for all statuses
            var summaryTable = new PdfPTable(6) { WidthPercentage = 100 };
            summaryTable.SetWidths(new float[] { 1, 1, 1, 1, 1, 1 });

            var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10, BaseColor.WHITE);
            var valueFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14, BaseColor.DARK_GRAY);
            var labelFont = FontFactory.GetFont(FontFactory.HELVETICA, 8, BaseColor.GRAY);

            BaseColor percentColor = overallPercentage >= 75 ? new BaseColor(34, 197, 94) : new BaseColor(239, 68, 68);

            // Summary cells
            AddSummaryCell(summaryTable, $"{overallPercentage:F1}%", "Overall Attendance", percentColor);
            AddSummaryCell(summaryTable, overallTotal.ToString(), "Total Classes", new BaseColor(59, 130, 246));
            AddSummaryCell(summaryTable, overallPresent.ToString(), "Present", new BaseColor(34, 197, 94));
            AddSummaryCell(summaryTable, overallLate.ToString(), "Late", new BaseColor(251, 191, 36));
            AddSummaryCell(summaryTable, overallExcused.ToString(), "Excused", new BaseColor(139, 92, 246));
            AddSummaryCell(summaryTable, overallAbsent.ToString(), "Absent", new BaseColor(239, 68, 68));

            document.Add(summaryTable);
            document.Add(new Paragraph("\n"));

            // Course-wise Details Table - 8 columns
            var detailsTitle = new Paragraph("Course-wise Attendance Details", FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12, BaseColor.DARK_GRAY))
            {
                SpacingAfter = 10
            };
            document.Add(detailsTitle);

            var detailsTable = new PdfPTable(8) { WidthPercentage = 100 };
            detailsTable.SetWidths(new float[] { 2f, 0.8f, 0.8f, 0.8f, 0.8f, 0.8f, 0.8f, 1.2f });

            // Header
            var tableHeaderFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, BaseColor.WHITE);
            var tableHeaderColor = new BaseColor(55, 65, 81);
            string[] headers = { "Course", "Total", "Present", "Late", "Excused", "Absent", "%", "Status" };
            foreach (var header in headers)
            {
                var cell = new PdfPCell(new Phrase(header, tableHeaderFont))
                {
                    BackgroundColor = tableHeaderColor,
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    Padding = 6,
                    BorderWidth = 0
                };
                detailsTable.AddCell(cell);
            }

            var cellFont = FontFactory.GetFont(FontFactory.HELVETICA, 8, BaseColor.DARK_GRAY);
            var rowAlt = false;

            foreach (var data in attendanceData)
            {
                BaseColor rowColor = rowAlt ? new BaseColor(249, 250, 251) : BaseColor.WHITE;
                rowAlt = !rowAlt;

                // Course name
                var courseCell = new PdfPCell(new Phrase($"{data.CourseCode}\n{data.CourseName}", cellFont))
                {
                    BackgroundColor = rowColor,
                    Padding = 5,
                    BorderWidth = 0.5f,
                    BorderColor = new BaseColor(229, 231, 235)
                };
                detailsTable.AddCell(courseCell);

                // Total
                AddDataCell(detailsTable, data.Total.ToString(), rowColor, cellFont);

                // Present
                var presentFont = FontFactory.GetFont(FontFactory.HELVETICA, 8, new BaseColor(34, 197, 94));
                AddDataCell(detailsTable, data.Present.ToString(), rowColor, presentFont);

                // Late
                var lateFont = FontFactory.GetFont(FontFactory.HELVETICA, 8, new BaseColor(251, 191, 36));
                AddDataCell(detailsTable, data.Late.ToString(), rowColor, lateFont);

                // Excused
                var excusedFont = FontFactory.GetFont(FontFactory.HELVETICA, 8, new BaseColor(139, 92, 246));
                AddDataCell(detailsTable, data.Excused.ToString(), rowColor, excusedFont);

                // Absent
                var absentFont = FontFactory.GetFont(FontFactory.HELVETICA, 8, new BaseColor(239, 68, 68));
                AddDataCell(detailsTable, data.Absent.ToString(), rowColor, absentFont);

                // Percentage
                double pct = data.Percentage;
                var pctFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 8, pct >= 75 ? new BaseColor(34, 197, 94) : new BaseColor(239, 68, 68));
                AddDataCell(detailsTable, $"{pct:F1}%", rowColor, pctFont);

                // Status
                string status = pct >= 75 ? "Good" : "Warning";
                var statusFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 7, pct >= 75 ? new BaseColor(34, 197, 94) : new BaseColor(239, 68, 68));
                AddDataCell(detailsTable, status, rowColor, statusFont);
            }

            document.Add(detailsTable);

            // Warning note if below 75%
            var lowCourses = attendanceData.Where(d => (double)d.Percentage < 75).ToList();
            if (lowCourses.Any())
            {
                document.Add(new Paragraph("\n"));
                var warningFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, new BaseColor(180, 83, 9));
                var warning = new Paragraph($"⚠ Warning: You have {lowCourses.Count} course(s) below 75% attendance requirement.", warningFont)
                {
                    SpacingBefore = 10
                };
                document.Add(warning);
            }

            // Generated date
            document.Add(new Paragraph("\n"));
            var dateFont = FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 8, BaseColor.GRAY);
            var datePara = new Paragraph($"Generated on: {DateTime.Now:MMMM dd, yyyy hh:mm tt}", dateFont)
            {
                Alignment = Element.ALIGN_RIGHT
            };
            document.Add(datePara);

            document.Close();

            return File(memoryStream.ToArray(), "application/pdf", $"Attendance_Report_{student.FirstName}_{student.LastName}.pdf");
        }

        // Course-wise detailed PDF with day-wise attendance
        public async Task<IActionResult> ExportCourseAttendanceToPdf(int assignmentId)
        {
            var student = await GetCurrentStudentAsync();
            if (student == null) return RedirectToAction("Login", "Auth");

            // Get course assignment details
            var assignment = await _context.CourseAssignments
                .Include(ca => ca.Course)
                .Include(ca => ca.Teacher)
                .Include(ca => ca.Semester)
                .Include(ca => ca.Batch)
                .FirstOrDefaultAsync(ca => ca.AssignmentId == assignmentId);

            if (assignment == null) return NotFound();

            // Get all attendance records for this student in this course
            var attendances = await _context.Attendances
                .Include(a => a.Session)
                .Where(a => a.StudentId == student.StudentId &&
                            a.Session.CourseAssignmentId == assignmentId)
                .OrderBy(a => a.Session.SessionDate)
                .ThenBy(a => a.Session.StartTime)
                .ToListAsync();

            // Calculate stats
            var total = attendances.Count;
            var present = attendances.Count(a => a.Status == "Present");
            var late = attendances.Count(a => a.Status == "Late");
            var excused = attendances.Count(a => a.Status == "Excused");
            var absent = attendances.Count(a => a.Status == "Absent");
            var effectivePresent = present + late + excused;
            var percentage = total > 0 ? (double)effectivePresent / total * 100 : 0;

            using var memoryStream = new MemoryStream();
            var document = new Document(PageSize.A4, 36, 36, 100, 50);
            var writer = PdfWriter.GetInstance(document, memoryStream);

            var institutionInfo = await _institutionService.GetInstitutionInfoAsync();
            writer.PageEvent = new PdfHeaderFooter(institutionInfo, _webHostEnvironment);

            document.Open();

            // Title
            var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16, BaseColor.DARK_GRAY);
            var title = new Paragraph($"Course Attendance Report", titleFont)
            {
                Alignment = Element.ALIGN_CENTER,
                SpacingAfter = 5
            };
            document.Add(title);

            // Student Info
            var subtitleFont = FontFactory.GetFont(FontFactory.HELVETICA, 11, BaseColor.GRAY);
            var subtitle = new Paragraph($"{student.FirstName} {student.LastName} | Roll No: {student.RollNumber}", subtitleFont)
            {
                Alignment = Element.ALIGN_CENTER,
                SpacingAfter = 10
            };
            document.Add(subtitle);

            // Course Info Box
            var infoTable = new PdfPTable(2) { WidthPercentage = 100 };
            infoTable.SetWidths(new float[] { 1, 1 });

            var infoFont = FontFactory.GetFont(FontFactory.HELVETICA, 10, BaseColor.DARK_GRAY);
            var infoLabelFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10, BaseColor.DARK_GRAY);

            AddInfoRow(infoTable, "Course:", $"{assignment.Course.CourseCode} - {assignment.Course.CourseName}", infoLabelFont, infoFont);
            AddInfoRow(infoTable, "Teacher:", assignment.Teacher != null ? $"{assignment.Teacher.FirstName} {assignment.Teacher.LastName}" : "N/A", infoLabelFont, infoFont);
            AddInfoRow(infoTable, "Semester:", $"{assignment.Semester.SemesterName} ({assignment.Semester.Year})", infoLabelFont, infoFont);
            AddInfoRow(infoTable, "Batch:", assignment.Batch?.BatchName ?? "N/A", infoLabelFont, infoFont);

            document.Add(infoTable);
            document.Add(new Paragraph("\n"));

            // Summary Stats
            var summaryTable = new PdfPTable(6) { WidthPercentage = 100 };
            summaryTable.SetWidths(new float[] { 1, 1, 1, 1, 1, 1 });

            BaseColor percentColor = percentage >= 75 ? new BaseColor(34, 197, 94) : new BaseColor(239, 68, 68);

            AddSummaryCell(summaryTable, $"{percentage:F1}%", "Attendance", percentColor);
            AddSummaryCell(summaryTable, total.ToString(), "Total", new BaseColor(59, 130, 246));
            AddSummaryCell(summaryTable, present.ToString(), "Present", new BaseColor(34, 197, 94));
            AddSummaryCell(summaryTable, late.ToString(), "Late", new BaseColor(251, 191, 36));
            AddSummaryCell(summaryTable, excused.ToString(), "Excused", new BaseColor(139, 92, 246));
            AddSummaryCell(summaryTable, absent.ToString(), "Absent", new BaseColor(239, 68, 68));

            document.Add(summaryTable);
            document.Add(new Paragraph("\n"));

            // Day-wise Attendance Table
            var detailsTitle = new Paragraph("Day-wise Attendance Record", FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12, BaseColor.DARK_GRAY))
            {
                SpacingAfter = 10
            };
            document.Add(detailsTitle);

            var detailsTable = new PdfPTable(5) { WidthPercentage = 100 };
            detailsTable.SetWidths(new float[] { 0.5f, 1.5f, 1f, 1f, 1f });

            // Header
            var tableHeaderFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, BaseColor.WHITE);
            var tableHeaderColor = new BaseColor(55, 65, 81);
            string[] headers = { "#", "Date", "Day", "Time", "Status" };
            foreach (var header in headers)
            {
                var cell = new PdfPCell(new Phrase(header, tableHeaderFont))
                {
                    BackgroundColor = tableHeaderColor,
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    Padding = 8,
                    BorderWidth = 0
                };
                detailsTable.AddCell(cell);
            }

            var cellFont = FontFactory.GetFont(FontFactory.HELVETICA, 9, BaseColor.DARK_GRAY);
            var rowAlt = false;
            int rowNum = 1;

            foreach (var att in attendances)
            {
                BaseColor rowColor = rowAlt ? new BaseColor(249, 250, 251) : BaseColor.WHITE;
                rowAlt = !rowAlt;

                // Row number
                AddDataCell(detailsTable, rowNum.ToString(), rowColor, cellFont);

                // Date
                AddDataCell(detailsTable, att.Session.SessionDate.ToString("MMM dd, yyyy"), rowColor, cellFont);

                // Day of week
                var dayName = att.Session.SessionDate.DayOfWeek.ToString();
                AddDataCell(detailsTable, dayName, rowColor, cellFont);

                // Time
                AddDataCell(detailsTable, att.Session.StartTime.ToString(@"hh\:mm tt"), rowColor, cellFont);

                // Status with color
                BaseColor statusColor = att.Status switch
                {
                    "Present" => new BaseColor(34, 197, 94),
                    "Late" => new BaseColor(251, 191, 36),
                    "Excused" => new BaseColor(139, 92, 246),
                    "Absent" => new BaseColor(239, 68, 68),
                    _ => BaseColor.DARK_GRAY
                };
                var statusFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, statusColor);
                AddDataCell(detailsTable, att.Status, rowColor, statusFont);

                rowNum++;
            }

            document.Add(detailsTable);

            // Warning if below 75%
            if (percentage < 75)
            {
                document.Add(new Paragraph("\n"));
                var warningFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10, new BaseColor(180, 83, 9));
                var warning = new Paragraph($"⚠ Warning: Your attendance is below 75% requirement!", warningFont)
                {
                    SpacingBefore = 10
                };
                document.Add(warning);
            }

            // Generated date
            document.Add(new Paragraph("\n"));
            var dateFont = FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 8, BaseColor.GRAY);
            var datePara = new Paragraph($"Generated on: {DateTime.Now:MMMM dd, yyyy hh:mm tt}", dateFont)
            {
                Alignment = Element.ALIGN_RIGHT
            };
            document.Add(datePara);

            document.Close();

            return File(memoryStream.ToArray(), "application/pdf", $"Course_Attendance_{assignment.Course.CourseCode}_{student.RollNumber}.pdf");
        }

        private void AddInfoRow(PdfPTable table, string label, string value, Font labelFont, Font valueFont)
        {
            var labelCell = new PdfPCell(new Phrase(label, labelFont))
            {
                Border = 0,
                Padding = 4
            };
            table.AddCell(labelCell);

            var valueCell = new PdfPCell(new Phrase(value, valueFont))
            {
                Border = 0,
                Padding = 4
            };
            table.AddCell(valueCell);
        }

        private void AddSummaryCell(PdfPTable table, string value, string label, BaseColor accentColor)
        {
            var valueFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14, accentColor);
            var labelFont = FontFactory.GetFont(FontFactory.HELVETICA, 8, BaseColor.GRAY);

            var content = new Phrase();
            content.Add(new Chunk(value + "\n", valueFont));
            content.Add(new Chunk(label, labelFont));

            var cell = new PdfPCell(content)
            {
                HorizontalAlignment = Element.ALIGN_CENTER,
                VerticalAlignment = Element.ALIGN_MIDDLE,
                Padding = 10,
                BorderWidth = 0.5f,
                BorderColor = new BaseColor(229, 231, 235),
                BackgroundColor = new BaseColor(249, 250, 251)
            };
            table.AddCell(cell);
        }

        private void AddDataCell(PdfPTable table, string text, BaseColor bgColor, Font font)
        {
            var cell = new PdfPCell(new Phrase(text, font))
            {
                BackgroundColor = bgColor,
                HorizontalAlignment = Element.ALIGN_CENTER,
                VerticalAlignment = Element.ALIGN_MIDDLE,
                Padding = 6,
                BorderWidth = 0.5f,
                BorderColor = new BaseColor(229, 231, 235)
            };
            table.AddCell(cell);
        }

        // ============== JSON API ENDPOINTS ==============

        [HttpGet]
        public async Task<IActionResult> GetMyTimetableJson()
        {
            var student = await GetCurrentStudentAsync();
            if (student == null) return Unauthorized();

            var today = DateOnly.FromDateTime(DateTime.Now);
            var currentSemester = await _context.Semesters
                .FirstOrDefaultAsync(s => s.StartDate <= today && s.EndDate >= today && s.IsActive == true);

            var batch = await _context.Batches.FindAsync(student.BatchId);

            var slots = new List<object>();
            if (currentSemester != null)
            {
                var enrollments = await _context.Enrollments
                    .Include(e => e.Course)
                    .Where(e => e.StudentId == student.StudentId && e.Status == "Active")
                    .ToListAsync();

                foreach (var enrollment in enrollments)
                {
                    var targetBatchId = enrollment.BatchId ?? student.BatchId;
                    var crossSlots = await _context.TimetableSlots
                        .Include(ts => ts.CourseAssignment)
                        .ThenInclude(ca => ca.Course)
                        .Include(ts => ts.CourseAssignment)
                        .ThenInclude(ca => ca.Teacher)
                        .Where(ts => ts.Timetable.BatchId == targetBatchId &&
                                     ts.Timetable.SemesterId == currentSemester.SemesterId &&
                                     ts.Timetable.IsActive == true &&
                                     ts.CourseAssignment.CourseId == enrollment.CourseId)
                        .Select(ts => new
                        {
                            ts.SlotId,
                            ts.DayOfWeek,
                            DayName = GetDayName(ts.DayOfWeek ?? 0),
                            StartTime = ts.StartTime.Value.ToString(@"hh\:mm"),
                            EndTime = ts.EndTime.Value.ToString(@"hh\:mm"),
                            CourseCode = ts.CourseAssignment.Course.CourseCode,
                            CourseName = ts.CourseAssignment.Course.CourseName,
                            TeacherName = ts.CourseAssignment.Teacher != null
                                ? ts.CourseAssignment.Teacher.FirstName + " " + ts.CourseAssignment.Teacher.LastName
                                : "N/A"
                        })
                        .ToListAsync();
                    slots.AddRange(crossSlots);
                }
            }

            return Json(new
            {
                success = true,
                studentName = $"{student.FirstName} {student.LastName}",
                batchName = batch?.BatchName ?? "N/A",
                semesterName = currentSemester != null ? $"{currentSemester.SemesterName} ({currentSemester.Year})" : "N/A",
                slots = slots.OrderBy(s => ((dynamic)s).DayOfWeek).ThenBy(s => ((dynamic)s).StartTime).ToList()
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetMyAttendanceJson(int? semesterId)
        {
            var student = await GetCurrentStudentAsync();
            if (student == null) return Unauthorized();

            var today = DateOnly.FromDateTime(DateTime.Now);
            Semester currentSemester;

            if (semesterId.HasValue)
            {
                currentSemester = await _context.Semesters.FindAsync(semesterId.Value);
            }
            else
            {
                currentSemester = await _context.Semesters
                    .FirstOrDefaultAsync(s => s.StartDate <= today && s.EndDate >= today && s.IsActive == true);
            }

            var batch = await _context.Batches.FindAsync(student.BatchId);
            var courses = new List<object>();

            if (currentSemester != null)
            {
                var enrollments = await _context.Enrollments
                    .Include(e => e.Course)
                    .Where(e => e.StudentId == student.StudentId && e.Status == "Active")
                    .ToListAsync();

                foreach (var enrollment in enrollments)
                {
                    var targetBatchId = enrollment.BatchId ?? student.BatchId;
                    var assignment = await _context.CourseAssignments
                        .Include(ca => ca.Teacher)
                        .FirstOrDefaultAsync(ca => ca.CourseId == enrollment.CourseId &&
                                                   ca.BatchId == targetBatchId &&
                                                   ca.SemesterId == currentSemester.SemesterId);

                    if (assignment != null)
                    {
                        var attendances = await _context.Attendances
                            .Include(a => a.Session)
                            .Where(a => a.StudentId == student.StudentId &&
                                        a.Session.CourseAssignmentId == assignment.AssignmentId)
                            .OrderByDescending(a => a.Session.SessionDate)
                            .ThenByDescending(a => a.Session.StartTime)
                            .ToListAsync();

                        var total = attendances.Count;
                        var present = attendances.Count(a => a.Status == "Present");
                        var late = attendances.Count(a => a.Status == "Late");
                        var excused = attendances.Count(a => a.Status == "Excused");
                        var absent = attendances.Count(a => a.Status == "Absent");
                        var effectivePresent = present + late + excused;
                        var percentage = total > 0 ? (double)effectivePresent / total * 100 : 0;

                        courses.Add(new
                        {
                            courseId = enrollment.CourseId,
                            assignmentId = assignment.AssignmentId,
                            courseCode = enrollment.Course.CourseCode,
                            courseName = enrollment.Course.CourseName,
                            teacherName = assignment.Teacher != null
                                ? $"{assignment.Teacher.FirstName} {assignment.Teacher.LastName}"
                                : "N/A",
                            total,
                            present,
                            late,
                            excused,
                            absent,
                            effectivePresent,
                            percentage = Math.Round(percentage, 1),
                            isLow = percentage < 75,
                            history = attendances.Take(10).Select(a => new
                            {
                                date = a.Session.SessionDate.ToString("MMM dd, yyyy"),
                                time = a.Session.StartTime.ToString(@"hh\:mm"),
                                status = a.Status
                            }).ToList()
                        });
                    }
                }
            }

            // Calculate overall stats
            int overallTotal = courses.Sum(c => (int)((dynamic)c).total);
            int overallPresent = courses.Sum(c => (int)((dynamic)c).present);
            int overallLate = courses.Sum(c => (int)((dynamic)c).late);
            int overallExcused = courses.Sum(c => (int)((dynamic)c).excused);
            int overallAbsent = courses.Sum(c => (int)((dynamic)c).absent);
            int overallEffectivePresent = courses.Sum(c => (int)((dynamic)c).effectivePresent);
            double overallPercentage = overallTotal > 0 ? (double)overallEffectivePresent / overallTotal * 100 : 0;

            // Get semesters for dropdown with year
            var semesters = await _context.Semesters
                .Where(s => s.IsActive == true)
                .OrderByDescending(s => s.StartDate)
                .Select(s => new { s.SemesterId, SemesterName = s.SemesterName + " (" + s.Year + ")" })
                .ToListAsync();

            return Json(new
            {
                success = true,
                studentName = $"{student.FirstName} {student.LastName}",
                rollNumber = student.RollNumber,
                batchName = batch?.BatchName ?? "N/A",
                currentSemesterId = currentSemester?.SemesterId,
                semesterName = currentSemester != null ? $"{currentSemester.SemesterName} ({currentSemester.Year})" : "N/A",
                overall = new
                {
                    total = overallTotal,
                    present = overallPresent,
                    late = overallLate,
                    excused = overallExcused,
                    absent = overallAbsent,
                    effectivePresent = overallEffectivePresent,
                    percentage = Math.Round(overallPercentage, 1),
                    isLow = overallPercentage < 75
                },
                courses,
                semesters
            });
        }

        private static string GetDayName(int dayOfWeek)
        {
            return dayOfWeek switch
            {
                0 => "Sunday",
                1 => "Monday",
                2 => "Tuesday",
                3 => "Wednesday",
                4 => "Thursday",
                5 => "Friday",
                6 => "Saturday",
                _ => ""
            };
        }

        #region Student Self-Enrollment

        /// <summary>
        /// Display My Courses page with enrolled and available courses
        /// </summary>
        [HttpGet]
        public IActionResult MyCourses()
        {
            // Client-side page; data loads from GetMyCoursesJson
            return View();
        }

        /// <summary>
        /// Enroll student in a course
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnrollInCourse(int courseId, int semesterId, int? batchId)
        {
            var student = await GetCurrentStudentAsync();
            if (student == null)
                return Json(new { success = false, message = "Student not found" });

            var today = DateOnly.FromDateTime(DateTime.Now);

            // Validate semester is active and current
            var semester = await _context.Semesters.FindAsync(semesterId);
            if (semester == null || semester.IsActive != true)
                return Json(new { success = false, message = "Invalid semester" });

            if (semester.EndDate < today)
                return Json(new { success = false, message = "Cannot enroll: Semester has ended" });

            // Check enrollment deadline (halfway through semester)
            if (semester.StartDate != default && semester.EndDate != default)
            {
                var totalDays = semester.EndDate.DayNumber - semester.StartDate.DayNumber;
                if (totalDays >= 0)
                {
                    var enrollmentDeadline = semester.StartDate.AddDays(totalDays / 2);
                    if (today > enrollmentDeadline)
                        return Json(new { success = false, message = "Enrollment period has ended. Contact admin for late enrollment." });
                }
            }

            // Check if already enrolled
            var existingEnrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.StudentId == student.StudentId &&
                                         e.CourseId == courseId &&
                                         e.SemesterId == semesterId);

            if (existingEnrollment != null)
                return Json(new { success = false, message = "You are already enrolled in this course" });

            // Check for timetable conflicts
            var courseAssignment = await _context.CourseAssignments
                .Include(ca => ca.TimetableSlots)
                .FirstOrDefaultAsync(ca => ca.CourseId == courseId &&
                                          ca.SemesterId == semesterId &&
                                          ca.IsActive == true);

            if (courseAssignment == null)
                return Json(new { success = false, message = "Course is not available this semester" });

            // Get student's current schedule
            var studentEnrollments = await _context.Enrollments
                .Where(e => e.StudentId == student.StudentId && e.SemesterId == semesterId && e.Status == "Active")
                .Select(e => e.CourseId)
                .ToListAsync();

            var studentSlots = await _context.TimetableSlots
                .Include(ts => ts.CourseAssignment)
                .Where(ts => studentEnrollments.Contains(ts.CourseAssignment.CourseId) &&
                            ts.CourseAssignment.SemesterId == semesterId &&
                            ts.CourseAssignment.IsActive == true)
                .ToListAsync();

            // Check for conflicts
            foreach (var newSlot in courseAssignment.TimetableSlots)
            {
                if (!newSlot.DayOfWeek.HasValue || !newSlot.StartTime.HasValue || !newSlot.EndTime.HasValue)
                    continue;

                foreach (var existingSlot in studentSlots)
                {
                    if (!existingSlot.DayOfWeek.HasValue || !existingSlot.StartTime.HasValue || !existingSlot.EndTime.HasValue)
                        continue;

                    if (existingSlot.DayOfWeek.Value == newSlot.DayOfWeek.Value)
                    {
                        // Check time overlap
                        if (newSlot.StartTime.Value < existingSlot.EndTime.Value &&
                            newSlot.EndTime.Value > existingSlot.StartTime.Value)
                        {
                            var conflictCourse = await _context.Courses.FindAsync(existingSlot.CourseAssignment?.CourseId);
                            return Json(new
                            {
                                success = false,
                                message = $"Schedule conflict with {conflictCourse?.CourseName ?? "another course"} on {GetDayName(newSlot.DayOfWeek.Value)}"
                            });
                        }
                    }
                }
            }

            // Create enrollment
            var enrollment = new Enrollment
            {
                StudentId = student.StudentId,
                CourseId = courseId,
                SemesterId = semesterId,
                BatchId = batchId ?? courseAssignment.BatchId,
                Status = "Active"
            };

            _context.Enrollments.Add(enrollment);
            await _context.SaveChangesAsync();

            var course = await _context.Courses.FindAsync(courseId);
            return Json(new { success = true, message = $"Successfully enrolled in {course?.CourseName}" });
        }

        /// <summary>
        /// Unenroll student from a course
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnenrollFromCourse(int enrollmentId)
        {
            var student = await GetCurrentStudentAsync();
            if (student == null)
                return Json(new { success = false, message = "Student not found" });

            var enrollment = await _context.Enrollments
                .Include(e => e.Course)
                .Include(e => e.Semester)
                .FirstOrDefaultAsync(e => e.EnrollmentId == enrollmentId && e.StudentId == student.StudentId);

            if (enrollment == null)
                return Json(new { success = false, message = "Enrollment not found" });

            var today = DateOnly.FromDateTime(DateTime.Now);

            // Check if semester has ended
            if (enrollment.Semester?.EndDate < today)
                return Json(new { success = false, message = "Cannot unenroll: Semester has ended" });

            // Check if any attendance has been marked
            var hasAttendance = await _context.Attendances
                .Include(a => a.Session)
                .ThenInclude(s => s.CourseAssignment)
                .AnyAsync(a => a.StudentId == student.StudentId &&
                              a.Session.CourseAssignment.CourseId == enrollment.CourseId &&
                              a.Session.CourseAssignment.SemesterId == enrollment.SemesterId);

            if (hasAttendance)
                return Json(new { success = false, message = "Cannot unenroll: Attendance records exist for this course" });

            // Remove enrollment
            _context.Enrollments.Remove(enrollment);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"Successfully unenrolled from {enrollment.Course?.CourseName}" });
        }

        /// <summary>
        /// Get course details for enrollment modal (JSON API)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetCourseDetails(int assignmentId)
        {
            var assignment = await _context.CourseAssignments
                .Include(ca => ca.Course)
                .Include(ca => ca.Teacher)
                .Include(ca => ca.Batch)
                .Include(ca => ca.Semester)
                .Include(ca => ca.TimetableSlots)
                .FirstOrDefaultAsync(ca => ca.AssignmentId == assignmentId);

            if (assignment == null)
                return Json(new { success = false, message = "Course not found" });

            return Json(new
            {
                success = true,
                course = new
                {
                    assignmentId = assignment.AssignmentId,
                    courseId = assignment.CourseId,
                    courseCode = assignment.Course?.CourseCode,
                    courseName = assignment.Course?.CourseName,
                    creditHours = assignment.Course?.CreditHours,
                    teacherName = assignment.Teacher != null ? $"{assignment.Teacher.FirstName} {assignment.Teacher.LastName}" : "TBA",
                    batchName = assignment.Batch?.BatchName,
                    batchId = assignment.BatchId,
                    semesterId = assignment.SemesterId,
                    semesterName = assignment.Semester != null ? $"{assignment.Semester.SemesterName} {assignment.Semester.Year}" : "",
                    schedule = assignment.TimetableSlots?.Select(ts => new
                    {
                        dayOfWeek = ts.DayOfWeek,
                        dayName = GetDayName(ts.DayOfWeek ?? 0),
                        startTime = ts.StartTime?.ToString("h:mm tt"),
                        endTime = ts.EndTime?.ToString("h:mm tt")
                    }).OrderBy(s => s.dayOfWeek).ToList()
                }
            });
        }

        /// <summary>
        /// Get all enrollment data as JSON for client-side rendering
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetMyCoursesJson(int? semesterId)
        {
            var student = await GetCurrentStudentAsync();
            if (student == null)
                return Json(new { success = false, message = "Student not found" });

            var batch = await _context.Batches.FindAsync(student.BatchId);
            var today = DateOnly.FromDateTime(DateTime.Now);

            // Get current semester
            var currentSemester = await _context.Semesters
                .FirstOrDefaultAsync(s => s.StartDate <= today && s.EndDate >= today && s.IsActive == true);

            var selectedSemesterId = semesterId ?? currentSemester?.SemesterId;

            // Get semesters list
            var semesters = await _context.Semesters
                .Where(s => s.IsActive == true)
                .OrderByDescending(s => s.StartDate)
                .Select(s => new { s.SemesterId, semesterName = $"{s.SemesterName} {s.Year}" })
                .ToListAsync();

            // Get enrolled courses
            var enrollments = await _context.Enrollments
                .Include(e => e.Course)
                .Include(e => e.Semester)
                .Where(e => e.StudentId == student.StudentId &&
                           (selectedSemesterId == null || e.SemesterId == selectedSemesterId))
                .ToListAsync();

            var enrolledCourses = new List<object>();
            foreach (var enrollment in enrollments)
            {
                var targetBatchId = enrollment.BatchId ?? student.BatchId;
                var assignment = await _context.CourseAssignments
                    .Include(ca => ca.Teacher)
                    .Include(ca => ca.Batch)
                    .Include(ca => ca.TimetableSlots)
                    .FirstOrDefaultAsync(ca => ca.CourseId == enrollment.CourseId &&
                                              ca.SemesterId == enrollment.SemesterId &&
                                              ca.BatchId == targetBatchId &&
                                              ca.IsActive == true);

                var attendanceQuery = await _context.Attendances
                    .Include(a => a.Session)
                    .ThenInclude(s => s.CourseAssignment)
                    .Where(a => a.StudentId == student.StudentId &&
                               a.Session.CourseAssignment.CourseId == enrollment.CourseId &&
                               a.Session.CourseAssignment.SemesterId == enrollment.SemesterId)
                    .ToListAsync();

                var totalSessions = attendanceQuery.Count;
                var presentSessions = attendanceQuery.Count(a => a.Status == "Present" || a.Status == "Late" || a.Status == "Excused");

                var semester = await _context.Semesters.FindAsync(enrollment.SemesterId);
                bool canUnenroll = totalSessions == 0 && (semester == null || semester.EndDate >= today);
                string unenrollBlockedReason = totalSessions > 0
                    ? $"Cannot unenroll: {totalSessions} attendance record(s) exist"
                    : (semester?.EndDate < today ? "Cannot unenroll: Semester has ended" : "");

                enrolledCourses.Add(new
                {
                    enrollmentId = enrollment.EnrollmentId,
                    courseId = enrollment.CourseId ?? 0,
                    courseCode = enrollment.Course?.CourseCode ?? "",
                    courseName = enrollment.Course?.CourseName ?? "",
                    creditHours = enrollment.Course?.CreditHours ?? 0,
                    teacherName = assignment?.Teacher != null ? $"{assignment.Teacher.FirstName} {assignment.Teacher.LastName}" : "TBA",
                    batchId = assignment?.BatchId ?? (targetBatchId ?? 0),
                    batchName = assignment?.Batch?.BatchName ?? "",
                    batchYear = assignment?.Batch?.Year,
                    semesterName = semester != null ? $"{semester.SemesterName} {semester.Year}" : "",
                    semesterId = enrollment.SemesterId ?? 0,
                    status = enrollment.Status ?? "Active",
                    totalSessions,
                    presentSessions,
                    attendancePercentage = totalSessions > 0 ? Math.Round((double)presentSessions / totalSessions * 100, 0) : 0,
                    canUnenroll,
                    unenrollBlockedReason,
                    schedule = assignment?.TimetableSlots == null
                        ? new List<object>()
                        : assignment.TimetableSlots
                            .Select(ts => new
                            {
                                dayOfWeek = ts.DayOfWeek ?? 0,
                                dayName = GetDayName(ts.DayOfWeek ?? 0),
                                startTime = ts.StartTime?.ToString("h:mm tt") ?? "",
                                endTime = ts.EndTime?.ToString("h:mm tt") ?? ""
                            })
                            .OrderBy(s => s.dayOfWeek)
                            .Select(s => (object)s)
                            .ToList()
                });
            }

            // Get available courses (only for current semester)
            var availableCourses = new List<object>();
            if (currentSemester != null && selectedSemesterId == currentSemester.SemesterId)
            {
                var enrolledCourseIds = enrollments
                    .Where(e => e.SemesterId == currentSemester.SemesterId)
                    .Select(e => e.CourseId)
                    .ToList();

                // Get student's schedule for conflict detection
                var studentSlots = await _context.TimetableSlots
                    .Include(ts => ts.CourseAssignment)
                    .Where(ts => enrolledCourseIds.Contains(ts.CourseAssignment.CourseId) &&
                                ts.CourseAssignment.SemesterId == currentSemester.SemesterId &&
                                ts.CourseAssignment.IsActive == true)
                    .ToListAsync();

                var availableAssignments = await _context.CourseAssignments
                    .Include(ca => ca.Course)
                    .Include(ca => ca.Teacher)
                    .Include(ca => ca.Batch)
                    .Include(ca => ca.TimetableSlots)
                    .Where(ca => ca.SemesterId == currentSemester.SemesterId && ca.IsActive == true)
                    .ToListAsync();

                // Check enrollment deadline (halfway through semester)
                bool enrollmentOpen = true;
                string deadlineMessage = "";
                if (currentSemester.StartDate != default && currentSemester.EndDate != default)
                {
                    var totalDays = currentSemester.EndDate.DayNumber - currentSemester.StartDate.DayNumber;
                    if (totalDays >= 0)
                    {
                        var enrollmentDeadline = currentSemester.StartDate.AddDays(totalDays / 2);
                        if (today > enrollmentDeadline)
                        {
                            enrollmentOpen = false;
                            deadlineMessage = "Enrollment period has ended";
                        }
                    }
                }

                foreach (var assignment in availableAssignments)
                {
                    if (enrolledCourseIds.Contains(assignment.CourseId))
                        continue;

                    bool canEnroll = enrollmentOpen;
                    string blockReason = deadlineMessage;

                    // Check for schedule conflicts
                    if (canEnroll)
                    {
                        foreach (var slot in assignment.TimetableSlots)
                        {
                            if (!slot.DayOfWeek.HasValue || !slot.StartTime.HasValue || !slot.EndTime.HasValue)
                                continue;

                            foreach (var existing in studentSlots)
                            {
                                if (!existing.DayOfWeek.HasValue || !existing.StartTime.HasValue || !existing.EndTime.HasValue)
                                    continue;

                                if (existing.DayOfWeek.Value == slot.DayOfWeek.Value)
                                {
                                    if (slot.StartTime.Value < existing.EndTime.Value && slot.EndTime.Value > existing.StartTime.Value)
                                    {
                                        canEnroll = false;
                                        blockReason = "Schedule conflict with existing course";
                                        break;
                                    }
                                }
                            }
                            if (!canEnroll) break;
                        }
                    }

                    availableCourses.Add(new
                    {
                        courseAssignmentId = assignment.AssignmentId,
                        courseId = assignment.CourseId ?? 0,
                        courseCode = assignment.Course?.CourseCode ?? "",
                        courseName = assignment.Course?.CourseName ?? "",
                        creditHours = assignment.Course?.CreditHours ?? 0,
                        teacherName = assignment.Teacher != null ? $"{assignment.Teacher.FirstName} {assignment.Teacher.LastName}" : "TBA",
                        batchName = assignment.Batch?.BatchName ?? "",
                        batchYear = assignment.Batch?.Year,
                        batchId = assignment.BatchId ?? 0,
                        semesterId = currentSemester.SemesterId,
                        semesterName = $"{currentSemester.SemesterName} {currentSemester.Year}",
                        isOwnBatch = assignment.BatchId == student.BatchId,
                        canEnroll,
                        enrollBlockedReason = blockReason,
                        schedule = assignment.TimetableSlots == null
                            ? new List<object>()
                            : assignment.TimetableSlots
                                .Select(ts => new
                                {
                                    dayOfWeek = ts.DayOfWeek ?? 0,
                                    dayName = GetDayName(ts.DayOfWeek ?? 0),
                                    startTime = ts.StartTime?.ToString("h:mm tt") ?? "",
                                    endTime = ts.EndTime?.ToString("h:mm tt") ?? ""
                                })
                                .OrderBy(s => s.dayOfWeek)
                                .Select(s => (object)s)
                                .ToList()
                    });
                }

                // Sort: own batch first
                availableCourses = availableCourses
                    .OrderByDescending(c => ((dynamic)c).isOwnBatch)
                    .ThenBy(c => ((dynamic)c).courseName)
                    .ToList();
            }

            return Json(new
            {
                success = true,
                studentName = $"{student.FirstName} {student.LastName}",
                rollNumber = student.RollNumber,
                batchName = batch?.BatchName ?? "N/A",
                batchId = student.BatchId,
                currentSemesterId = currentSemester?.SemesterId,
                currentSemesterName = currentSemester != null ? $"{currentSemester.SemesterName} {currentSemester.Year}" : "N/A",
                selectedSemesterId,
                semesters,
                enrolledCourses,
                availableCourses,
                stats = new
                {
                    enrolledCount = enrolledCourses.Count,
                    totalCredits = enrolledCourses.Sum(c => (int)((dynamic)c).creditHours),
                    availableCount = availableCourses.Count
                }
            });
        }

        #endregion
    }
}
