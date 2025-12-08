using AMS.Models;
using AMS.Helpers;
using AMS.Models.Entities;
using AMS.Models.ViewModels;
using AMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Linq;
using ClosedXML.Excel;
using iTextSharp.text;
using iTextSharp.text.pdf;

namespace AMS.Controllers
{
    [Authorize]
    public class AttendanceController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IInstitutionService _institutionService;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public AttendanceController(ApplicationDbContext context, IInstitutionService institutionService, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _institutionService = institutionService;
            _webHostEnvironment = webHostEnvironment;
        }

        // GET: Attendance/MyTeacherReport
        [Authorize(Roles = "Teacher")]
        public async Task<IActionResult> MyTeacherReport()
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Auth");

            var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == int.Parse(userId));
            if (teacher == null) return RedirectToAction("Login", "Auth");

            return await TeacherReport(teacher.TeacherId);
        }

        // GET: Attendance/Mark
        // Called when clicking a slot on the timetable
        public async Task<IActionResult> Mark(int slotId, DateOnly? date)
        {
            var slot = await _context.TimetableSlots
                .Include(ts => ts.Timetable)
                .Include(ts => ts.CourseAssignment)
                .ThenInclude(ca => ca.Course)
                .Include(ts => ts.CourseAssignment)
                .ThenInclude(ca => ca.Batch)
                .Include(ts => ts.CourseAssignment)
                .ThenInclude(ca => ca.Semester)
                .FirstOrDefaultAsync(ts => ts.SlotId == slotId);

            if (slot == null) return NotFound("Slot not found.");
            if (slot.Timetable?.IsActive != true) return BadRequest("Cannot mark attendance for inactive timetable.");

            // Security Check: If user is a Teacher, ensure they own this slot
            if (User.IsInRole("Teacher"))
            {
                var userId = User.FindFirstValue("UserId");
                var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == int.Parse(userId));
                if (teacher == null || slot.CourseAssignment.TeacherId != teacher.TeacherId)
                {
                    return Forbid();
                }
            }

            // Calculate the date based on the dayOfWeek
            var today = DateOnly.FromDateTime(DateTime.Now);
            var targetDate = date ?? today;

            if (!date.HasValue)
            {
                int daysDiff = (int)today.DayOfWeek - (slot.DayOfWeek ?? 0);
                if (daysDiff < 0) daysDiff += 7;
                targetDate = today.AddDays(-daysDiff);
            }
            else
            {
                // Validate that the selected date matches the slot's day of week
                if ((int)targetDate.DayOfWeek != slot.DayOfWeek)
                {
                    TempData["error"] = $"Invalid date. This class is scheduled for {((DayOfWeek)(slot.DayOfWeek ?? 0))}.";
                    // Fallback to the most recent valid date
                    int daysDiff = (int)today.DayOfWeek - (slot.DayOfWeek ?? 0);
                    if (daysDiff < 0) daysDiff += 7;
                    targetDate = today.AddDays(-daysDiff);
                }
            }

            // Validate Semester Start/End
            var semester = slot.CourseAssignment?.Semester;
            var validDates = new List<DateOnly>();

            if (semester != null)
            {
                // Generate all valid dates for this slot in the semester
                var d = semester.StartDate;
                // Advance to first occurrence
                while ((int)d.DayOfWeek != slot.DayOfWeek)
                {
                    d = d.AddDays(1);
                }

                while (d <= semester.EndDate)
                {
                    validDates.Add(d);
                    d = d.AddDays(7);
                }

                if (targetDate < semester.StartDate)
                {
                    if (date.HasValue) TempData["error"] = $"Date is before semester start ({semester.StartDate.ToShortDateString()}).";
                    targetDate = validDates.FirstOrDefault();
                }
                else if (targetDate > semester.EndDate)
                {
                    if (date.HasValue) TempData["error"] = $"Date is after semester end ({semester.EndDate.ToShortDateString()}).";
                    targetDate = validDates.LastOrDefault();
                }
            }

            var viewResult = await LoadAttendanceView(slot, targetDate);
            if (viewResult is ViewResult v)
            {
                var m = v.Model as MarkAttendanceViewModel;
                if (m != null)
                {
                    m.ValidDates = validDates;
                }
            }
            return viewResult;
        }

        [HttpPost]
        public async Task<IActionResult> LoadByDate(int slotId, DateOnly date)
        {
            return await Mark(slotId, date);
        }

        private async Task<IActionResult> LoadAttendanceView(TimetableSlot slot, DateOnly date)
        {
            var assignment = slot.CourseAssignment;
            if (assignment == null) return NotFound("Course assignment not found.");

            // Check if session exists
            var session = await _context.Sessions
                .Include(s => s.Attendances)
                .FirstOrDefaultAsync(s => s.CourseAssignmentId == assignment.AssignmentId && s.SessionDate == date && s.StartTime == slot.StartTime);

            var model = new MarkAttendanceViewModel
            {
                SlotId = slot.SlotId,
                CourseAssignmentId = assignment.AssignmentId,
                CourseName = assignment.Course?.CourseName ?? "Unknown",
                BatchName = assignment.Batch?.BatchName ?? "Unknown", // Fixed property name
                Date = date,
                StartTime = slot.StartTime ?? default,
                EndTime = slot.EndTime ?? default,
                Students = new List<StudentAttendanceViewModel>()
            };

            // Get students enrolled in this course/semester
            var enrollments = await _context.Enrollments
                .Include(e => e.Student)
                .Where(e => e.CourseId == assignment.CourseId && e.SemesterId == assignment.SemesterId && e.Status == "Active")
                .ToListAsync();

            // If CourseAssignment has a specific BatchId, filter by it
            // Also filter by Timetable's BatchId to be sure we are marking for the correct batch
            // FIX: Allow students from other batches if they are explicitly enrolled in this course/semester
            // We only filter by batch if the enrollment logic requires it, but here we assume Enrollment is the source of truth.
            // However, if the same course is taught to multiple batches separately, we might need to distinguish.
            // But if a student is enrolled in "Math 101" for "Semester 1", and there are two sections (Batch A, Batch B),
            // the Enrollment table doesn't specify Section/Batch.
            // If we remove the filter, the student appears in BOTH sections.
            // Given the user's request ("I enrolled a student from another batch... teacher can't see that student"),
            // we MUST include them.
            // To avoid duplicates in multiple sections, we could check if the student belongs to the batch OR is enrolled.
            // But since we don't have SectionId in Enrollment, we'll show them in all sections of that course.
            // This is the intended behavior for "Out of Batch" enrollments in this system context.

            /* 
            if (slot.Timetable?.BatchId != null)
            {
                enrollments = enrollments.Where(e => e.Student.BatchId == slot.Timetable.BatchId).ToList();
            }
            else if (assignment.BatchId.HasValue)
            {
                enrollments = enrollments.Where(e => e.Student.BatchId == assignment.BatchId).ToList();
            }
            */

            // ROBUST LOGIC: Use Enrollment.BatchId to determine class allocation.
            // 1. If Enrollment has a specific BatchId, the student attends THAT batch's class.
            // 2. If Enrollment.BatchId is null, the student attends their Home Batch's class (Student.BatchId).

            var slotBatchId = slot.Timetable?.BatchId ?? assignment.BatchId;

            // Filter enrollments based on the target batch
            var validEnrollments = enrollments.Where(e =>
                // Case A: Explicitly assigned to this batch for this course
                (e.BatchId.HasValue && e.BatchId == slotBatchId) ||
                // Case B: Not explicitly assigned, but their home batch matches (Standard case)
                (!e.BatchId.HasValue && e.Student.BatchId == slotBatchId)
            ).ToList();

            foreach (var enrollment in validEnrollments)
            {
                var student = enrollment.Student;
                if (student == null) continue;

                var attendance = session?.Attendances.FirstOrDefault(a => a.StudentId == student.StudentId);

                model.Students.Add(new StudentAttendanceViewModel
                {
                    StudentId = student.StudentId,
                    StudentName = student.FirstName + " " + student.LastName,
                    RollNumber = student.RollNumber ?? "",
                    Status = attendance?.Status ?? "Present", // Default to Present if not marked
                    IsMarked = attendance != null
                });
            }

            model.Students = model.Students.OrderBy(s => s.RollNumber).ToList();

            return View("Mark", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(MarkAttendanceViewModel model)
        {
            // Security Check: If user is a Teacher, ensure they own this course assignment
            if (User.IsInRole("Teacher"))
            {
                var userId = User.FindFirstValue("UserId");
                var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == int.Parse(userId));

                // We need to check the CourseAssignment ownership
                var assignment = await _context.CourseAssignments.FindAsync(model.CourseAssignmentId);

                if (teacher == null || assignment == null || assignment.TeacherId != teacher.TeacherId)
                {
                    return Forbid();
                }
            }

            // Find or create session
            var session = await _context.Sessions
                .Include(s => s.Attendances)
                .FirstOrDefaultAsync(s => s.CourseAssignmentId == model.CourseAssignmentId && s.SessionDate == model.Date && s.StartTime == model.StartTime);

            if (session == null)
            {
                session = new Session
                {
                    CourseAssignmentId = model.CourseAssignmentId,
                    SessionDate = model.Date,
                    StartTime = model.StartTime,
                    EndTime = model.EndTime,
                    // CreatedBy = User.GetUserId() 
                };
                _context.Sessions.Add(session);
                await _context.SaveChangesAsync();
            }

            foreach (var studentModel in model.Students)
            {
                var attendance = session.Attendances.FirstOrDefault(a => a.StudentId == studentModel.StudentId);
                if (attendance == null)
                {
                    attendance = new Attendance
                    {
                        SessionId = session.SessionId,
                        StudentId = studentModel.StudentId,
                        Status = studentModel.Status,
                        // MarkedBy = User.GetUserId()
                    };
                    _context.Attendances.Add(attendance);
                }
                else
                {
                    attendance.Status = studentModel.Status;
                    // attendance.MarkedBy = User.GetUserId();
                }
            }

            await _context.SaveChangesAsync();

            TempData["success"] = "Attendance marked successfully.";
            return RedirectToAction("Mark", new { slotId = model.SlotId, date = model.Date });
        }

        // GET: Attendance/StudentReport
        public async Task<IActionResult> StudentReport(int? studentId, string rollNumber, int? batchId, int? semesterId, int? courseId, string duration = "week")
        {
            var model = new StudentAttendanceReportViewModel
            {
                SelectedBatchId = batchId,
                SelectedSemesterId = semesterId,
                SelectedCourseId = courseId,
                SelectedStudentId = studentId,
                SearchRollNumber = rollNumber,
                Duration = duration
            };

            // Security: Check if Teacher
            int? currentTeacherId = null;
            if (User.IsInRole("Teacher"))
            {
                var userId = User.FindFirstValue("UserId");
                var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == int.Parse(userId));
                if (teacher != null) currentTeacherId = teacher.TeacherId;
            }

            // Populate Dropdowns
            if (currentTeacherId.HasValue)
            {
                // Teacher: Only show assigned batches and courses
                var teacherAssignments = await _context.CourseAssignments
                    .Where(ca => ca.TeacherId == currentTeacherId && ca.IsActive == true)
                    .Select(ca => new { ca.BatchId, ca.Batch.BatchName, ca.CourseId, ca.Course.CourseName })
                    .ToListAsync();

                var batchIds = teacherAssignments.Select(a => a.BatchId).Distinct().ToList();
                var courseIds = teacherAssignments.Select(a => a.CourseId).Distinct().ToList();

                model.BatchList = await _context.Batches
                    .Where(b => batchIds.Contains(b.BatchId))
                    .Select(b => new SelectListItem { Value = b.BatchId.ToString(), Text = b.BatchName, Selected = b.BatchId == batchId })
                    .ToListAsync();

                model.CourseList = await _context.Courses
                    .Where(c => courseIds.Contains(c.CourseId))
                    .Select(c => new SelectListItem { Value = c.CourseId.ToString(), Text = c.CourseName, Selected = c.CourseId == courseId })
                    .ToListAsync();
            }
            else
            {
                // Admin: Show all
                model.BatchList = await _context.Batches
                    .Select(b => new SelectListItem { Value = b.BatchId.ToString(), Text = b.BatchName, Selected = b.BatchId == batchId })
                    .ToListAsync();

                model.CourseList = await _context.Courses
                    .Select(c => new SelectListItem { Value = c.CourseId.ToString(), Text = c.CourseName, Selected = c.CourseId == courseId })
                    .ToListAsync();
            }

            model.SemesterList = await _context.Semesters
                .Select(s => new SelectListItem { Value = s.SemesterId.ToString(), Text = s.SemesterName, Selected = s.SemesterId == semesterId })
                .ToListAsync();

            // Find Student
            Student student = null;
            if (studentId.HasValue)
            {
                student = await _context.Students.Include(s => s.Batch).FirstOrDefaultAsync(s => s.StudentId == studentId);
            }
            else if (!string.IsNullOrEmpty(rollNumber))
            {
                student = await _context.Students.Include(s => s.Batch).FirstOrDefaultAsync(s => s.RollNumber == rollNumber);
            }

            // Security: Verify Teacher access to student
            if (currentTeacherId.HasValue && student != null)
            {
                // Check if student is enrolled in any course taught by this teacher
                // We check if the teacher has an assignment for the course the student is enrolled in.
                // We do NOT restrict by student's batch, as they might be an out-of-batch enrollment.
                var isEnrolled = await _context.Enrollments
                    .AnyAsync(e => e.StudentId == student.StudentId &&
                                   e.Status == "Active" &&
                                   _context.CourseAssignments.Any(ca => ca.TeacherId == currentTeacherId && ca.CourseId == e.CourseId));

                if (!isEnrolled)
                {
                    // If not enrolled, maybe check if they are just in the batch (for initial lookup)
                    // But the requirement says "restricted to his students".
                    // If strict, we deny.
                    // Let's return a view with error or null student.
                    TempData["error"] = "You do not have permission to view this student's report.";
                    student = null;
                    model.SelectedStudentId = null;
                    model.RollNumber = null;
                    model.SearchRollNumber = null;
                }
            }

            // Populate Student List if Batch is selected
            if (batchId.HasValue)
            {
                // Start with students belonging to the batch
                var query = _context.Students.Where(s => s.BatchId == batchId);

                // Also include students from OTHER batches who are enrolled in courses assigned to this batch (and teacher)
                if (currentTeacherId.HasValue)
                {
                    // 1. Get courses taught by this teacher to this batch
                    var teacherCourseIds = await _context.CourseAssignments
                        .Where(ca => ca.TeacherId == currentTeacherId && ca.BatchId == batchId)
                        .Select(ca => ca.CourseId)
                        .ToListAsync();

                    // 2. Get ALL students enrolled in these courses (regardless of their batch)
                    // Updated Logic: Only include students who are explicitly assigned to this batch via Enrollment
                    // OR students who are in this batch and have no specific override.
                    var enrolledStudentIds = await _context.Enrollments
                        .Where(e => teacherCourseIds.Contains(e.CourseId) && e.Status == "Active" && e.StudentId != null)
                        .Where(e => (e.BatchId == batchId) || (e.BatchId == null && e.Student.BatchId == batchId))
                        .Select(e => (int)e.StudentId)
                        .Distinct()
                        .ToListAsync();

                    // 3. Select students who are EITHER in the batch OR enrolled in the courses
                    // Since we filtered enrolledStudentIds to only include those relevant to THIS batch,
                    // we can just use that list.
                    // However, the original 'query' gets all students in the batch.
                    // We should combine them carefully.

                    // Actually, with the new logic, 'enrolledStudentIds' covers everyone who SHOULD be in this batch's report for these courses.
                    // But 'query' covers students who are physically in the batch (for general reporting).

                    var batchStudentIds = await query.Select(s => s.StudentId).ToListAsync();
                    var allStudentIds = batchStudentIds.Concat(enrolledStudentIds).Distinct().ToList();

                    model.StudentList = await _context.Students
                        .Where(s => allStudentIds.Contains(s.StudentId))
                        .Select(s => new SelectListItem { Value = s.StudentId.ToString(), Text = $"{s.FirstName} {s.LastName} ({s.RollNumber})", Selected = s.StudentId == (student != null ? student.StudentId : null) })
                        .ToListAsync();
                }
                else
                {
                    // Admin view: Just show batch students (default behavior) or expand if needed.
                    // For now, keep default for Admin to avoid clutter, unless requested.
                    model.StudentList = await query
                        .Select(s => new SelectListItem { Value = s.StudentId.ToString(), Text = $"{s.FirstName} {s.LastName} ({s.RollNumber})", Selected = s.StudentId == (student != null ? student.StudentId : null) })
                        .ToListAsync();
                }
            }

            if (student == null)
            {
                return View(model);
            }

            model.SelectedStudentId = student.StudentId;
            model.StudentName = student.FirstName + " " + student.LastName;
            model.RollNumber = student.RollNumber;
            model.BatchName = student.Batch?.BatchName;
            model.SearchRollNumber = student.RollNumber;

            // Get all attendance records for this student
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
                .Where(a => a.StudentId == student.StudentId);

            // Security: Filter attendance for Teacher
            if (currentTeacherId.HasValue)
            {
                attendanceQuery = attendanceQuery.Where(a => a.Session.CourseAssignment.TeacherId == currentTeacherId);
            }

            // Apply Filters
            if (semesterId.HasValue)
            {
                attendanceQuery = attendanceQuery.Where(a => a.Session.CourseAssignment.SemesterId == semesterId);
            }
            if (courseId.HasValue)
            {
                attendanceQuery = attendanceQuery.Where(a => a.Session.CourseAssignment.CourseId == courseId);
            }

            var attendances = await attendanceQuery
                .OrderByDescending(a => a.Session.SessionDate)
                .ToListAsync();

            // Group by Course/Semester
            var grouped = attendances.GroupBy(a => new
            {
                a.Session.CourseAssignment.CourseId,
                a.Session.CourseAssignment.SemesterId,
                a.Session.CourseAssignment.Course.CourseName,
                a.Session.CourseAssignment.Semester.SemesterName,
                a.Session.CourseAssignment.Teacher.FirstName,
                a.Session.CourseAssignment.Teacher.LastName
            })
            .Select(g => new StudentCourseAttendanceViewModel
            {
                CourseId = g.Key.CourseId ?? 0,
                SemesterId = g.Key.SemesterId ?? 0,
                CourseName = g.Key.CourseName,
                SemesterName = g.Key.SemesterName,
                TeacherName = g.Key.FirstName + " " + g.Key.LastName,
                TotalSessions = g.Count(),
                PresentSessions = g.Count(a => a.Status == "Present" || a.Status == "Late"),
                AbsentSessions = g.Count(a => a.Status == "Absent"),
                LateSessions = g.Count(a => a.Status == "Late"),
                Percentage = g.Count() > 0 ? (double)g.Count(a => a.Status == "Present" || a.Status == "Late") / g.Count() * 100 : 0,
                History = g.Select(a => new AttendanceRecordViewModel
                {
                    Date = a.Session.SessionDate,
                    Status = a.Status,
                    StartTime = a.Session.StartTime,
                    EndTime = a.Session.EndTime
                }).ToList()
            }).ToList();

            // Generate Weekly Report
            var weeks = new List<WeeklyAttendanceViewModel>();

            Timetable activeTimetable = null;

            // 1. If Semester is selected, try to find timetable for that semester
            if (semesterId.HasValue)
            {
                activeTimetable = await _context.Timetables
                    .Include(t => t.Semester)
                    .Include(t => t.TimetableSlots)
                    .ThenInclude(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Course)
                    .Include(t => t.TimetableSlots)
                    .ThenInclude(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Teacher)
                    .FirstOrDefaultAsync(t => t.BatchId == student.BatchId && t.SemesterId == semesterId);
            }
            else
            {
                // 2. If no semester selected, try to find the Active timetable
                activeTimetable = await _context.Timetables
                    .Include(t => t.Semester)
                    .Include(t => t.TimetableSlots)
                    .ThenInclude(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Course)
                    .Include(t => t.TimetableSlots)
                    .ThenInclude(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Teacher)
                    .FirstOrDefaultAsync(t => t.BatchId == student.BatchId && t.IsActive == true);

                // 3. Fallback: Find ANY timetable for the current semester (based on today)
                if (activeTimetable == null)
                {
                    var today = DateOnly.FromDateTime(DateTime.Now);
                    var currentSemester = await _context.Semesters
                       .FirstOrDefaultAsync(s => s.StartDate <= today && s.EndDate >= today);

                    if (currentSemester != null)
                    {
                        activeTimetable = await _context.Timetables
                           .Include(t => t.Semester)
                           .Include(t => t.TimetableSlots)
                           .ThenInclude(ts => ts.CourseAssignment)
                           .ThenInclude(ca => ca.Course)
                           .Include(t => t.TimetableSlots)
                           .ThenInclude(ts => ts.CourseAssignment)
                           .ThenInclude(ca => ca.Teacher)
                           .FirstOrDefaultAsync(t => t.BatchId == student.BatchId && t.SemesterId == currentSemester.SemesterId);
                    }
                }

                // Fallback 2: Use semester from last attendance or just the latest timetable found
                if (activeTimetable == null)
                {
                    if (attendances.Any())
                    {
                        var lastAttendance = attendances.First();
                        var semId = lastAttendance.Session.CourseAssignment.SemesterId;
                        activeTimetable = await _context.Timetables
                            .Include(t => t.Semester)
                            .Include(t => t.TimetableSlots)
                            .ThenInclude(ts => ts.CourseAssignment)
                            .ThenInclude(ca => ca.Course)
                            .Include(t => t.TimetableSlots)
                            .ThenInclude(ts => ts.CourseAssignment)
                            .ThenInclude(ca => ca.Teacher)
                            .FirstOrDefaultAsync(t => t.BatchId == student.BatchId && t.SemesterId == semId);
                    }
                    else
                    {
                        // Just get the latest timetable for this batch
                        activeTimetable = await _context.Timetables
                            .Include(t => t.Semester)
                            .Include(t => t.TimetableSlots)
                            .ThenInclude(ts => ts.CourseAssignment)
                            .ThenInclude(ca => ca.Course)
                            .Include(t => t.TimetableSlots)
                            .ThenInclude(ts => ts.CourseAssignment)
                            .ThenInclude(ca => ca.Teacher)
                            .OrderByDescending(t => t.TimetableId)
                            .FirstOrDefaultAsync(t => t.BatchId == student.BatchId);
                    }
                }
            }

            // Determine Date Range based on Duration
            DateOnly startDate = default;
            DateOnly endDate = default;
            var todayDate = DateOnly.FromDateTime(DateTime.Now);

            // Base range (Semester or All Time)
            DateOnly semesterStart = todayDate;
            DateOnly semesterEnd = todayDate;

            if (activeTimetable != null && activeTimetable.Semester != null)
            {
                semesterStart = activeTimetable.Semester.StartDate;
                semesterEnd = activeTimetable.Semester.EndDate;
            }
            else if (semesterId.HasValue)
            {
                // If no timetable but semester selected, get dates from semester
                var sem = await _context.Semesters.FindAsync(semesterId);
                if (sem != null)
                {
                    semesterStart = sem.StartDate;
                    semesterEnd = sem.EndDate;
                }
            }
            else if (attendances.Any())
            {
                semesterStart = attendances.Min(a => a.Session.SessionDate);
                semesterEnd = attendances.Max(a => a.Session.SessionDate);
            }
            else
            {
                semesterStart = todayDate.AddDays(-7);
                semesterEnd = todayDate.AddDays(7);
            }

            if (duration == "month")
            {
                startDate = new DateOnly(todayDate.Year, todayDate.Month, 1);
                endDate = startDate.AddMonths(1).AddDays(-1);
            }
            else if (duration == "all")
            {
                startDate = semesterStart;
                endDate = semesterEnd;
            }
            else // "week"
            {
                startDate = todayDate.AddDays(-(int)todayDate.DayOfWeek + 1);
                if (todayDate.DayOfWeek == DayOfWeek.Sunday) startDate = todayDate.AddDays(-6);
                endDate = startDate.AddDays(6);
            }

            // Ensure valid dates
            if (startDate == default) startDate = todayDate.AddMonths(-1);
            if (endDate == default) endDate = todayDate.AddMonths(1);

            // Limit end date to today if semester is ongoing/future (optional)
            var reportEndDate = endDate;
            // if (reportEndDate > todayDate) reportEndDate = todayDate; // Allow seeing future schedule
            if (startDate > reportEndDate) reportEndDate = startDate;

            // Start from the beginning of the week of StartDate
            var currentWeekStart = startDate.AddDays(-(int)startDate.DayOfWeek + 1); // Monday
            if (startDate.DayOfWeek == DayOfWeek.Sunday) currentWeekStart = startDate.AddDays(-6); // If starts on Sunday, go back to Monday

            // Safety break to prevent infinite loops
            int safetyCounter = 0;
            while (currentWeekStart <= reportEndDate && safetyCounter < 52)
            {
                safetyCounter++;
                var weekEnd = currentWeekStart.AddDays(6);
                var weekViewModel = new WeeklyAttendanceViewModel
                {
                    WeekRange = $"{currentWeekStart:MMM dd} - {weekEnd:MMM dd}",
                    Days = new List<DailyAttendanceViewModel>()
                };

                for (int i = 0; i < 7; i++)
                {
                    var currentDate = currentWeekStart.AddDays(i);
                    // if (currentDate > endDate && currentDate > todayDate) break; // Allow full week
                    if (currentDate < startDate) continue;
                    if (currentDate > endDate) continue;

                    var dayViewModel = new DailyAttendanceViewModel
                    {
                        Date = currentDate,
                        DayName = currentDate.DayOfWeek.ToString(),
                        Classes = new List<ClassSessionViewModel>()
                    };

                    // 1. Find slots for this day (if timetable exists)
                    var daySlots = new List<TimetableSlot>();
                    if (activeTimetable != null)
                    {
                        daySlots = activeTimetable.TimetableSlots
                            .Where(s => s.DayOfWeek == (int)currentDate.DayOfWeek)
                            .OrderBy(s => s.StartTime)
                            .ToList();

                        // Filter slots by course if selected
                        if (courseId.HasValue)
                        {
                            daySlots = daySlots.Where(s => s.CourseAssignment.CourseId == courseId).ToList();
                        }

                        // Security: If Teacher, only show slots assigned to them
                        if (currentTeacherId.HasValue)
                        {
                            daySlots = daySlots.Where(s => s.CourseAssignment.TeacherId == currentTeacherId).ToList();
                        }
                    }

                    // 2. Find actual attendances for this day
                    var dayAttendances = attendances
                        .Where(a => a.Session.SessionDate == currentDate)
                        .ToList();

                    // Add classes from Slots
                    foreach (var slot in daySlots)
                    {
                        // Check if attendance exists
                        var attendance = dayAttendances.FirstOrDefault(a =>
                            a.Session.StartTime == slot.StartTime &&
                            a.Session.CourseAssignmentId == slot.CourseAssignmentId);

                        var status = "Not Marked";
                        var color = "text-gray-400 bg-gray-100 dark:bg-gray-800";

                        if (attendance != null)
                        {
                            status = attendance.Status;
                            if (status == "Present") color = "text-green-700 bg-green-50 dark:text-green-400 dark:bg-green-900/20";
                            else if (status == "Absent") color = "text-red-700 bg-red-50 dark:text-red-400 dark:bg-red-900/20";
                            else if (status == "Late") color = "text-yellow-700 bg-yellow-50 dark:text-yellow-400 dark:bg-yellow-900/20";
                            else if (status == "Excused") color = "text-blue-700 bg-blue-50 dark:text-blue-400 dark:bg-blue-900/20";
                        }
                        else if (currentDate > todayDate)
                        {
                            // Future date - upcoming class
                            status = "Upcoming";
                            color = "text-indigo-700 bg-indigo-50 dark:text-indigo-400 dark:bg-indigo-900/20";
                        }
                        else if (currentDate < todayDate)
                        {
                            // Past date with no attendance
                            status = "Pending";
                            color = "text-orange-700 bg-orange-50 dark:text-orange-400 dark:bg-orange-900/20";
                        }
                        else
                        {
                            // Today - not marked yet
                            status = "Today";
                            color = "text-blue-700 bg-blue-50 dark:text-blue-400 dark:bg-blue-900/20";
                        }

                        dayViewModel.Classes.Add(new ClassSessionViewModel
                        {
                            CourseName = slot.CourseAssignment?.Course?.CourseName ?? "Unknown",
                            TeacherName = slot.CourseAssignment?.Teacher?.FirstName + " " + slot.CourseAssignment?.Teacher?.LastName,
                            StartTime = slot.StartTime ?? default,
                            EndTime = slot.EndTime ?? default,
                            Status = status,
                            StatusColor = color
                        });
                    }

                    // Add classes from Attendance that didn't match a slot (Extra classes)
                    foreach (var att in dayAttendances)
                    {
                        bool alreadyAdded = dayViewModel.Classes.Any(c =>
                            c.StartTime == att.Session.StartTime &&
                            c.CourseName == att.Session.CourseAssignment.Course.CourseName);

                        if (!alreadyAdded)
                        {
                            var status = att.Status;
                            var color = "text-gray-400 bg-gray-100 dark:bg-gray-800";
                            if (status == "Present") color = "text-green-700 bg-green-50 dark:text-green-400 dark:bg-green-900/20";
                            else if (status == "Absent") color = "text-red-700 bg-red-50 dark:text-red-400 dark:bg-red-900/20";
                            else if (status == "Late") color = "text-yellow-700 bg-yellow-50 dark:text-yellow-400 dark:bg-yellow-900/20";
                            else if (status == "Excused") color = "text-blue-700 bg-blue-50 dark:text-blue-400 dark:bg-blue-900/20";

                            dayViewModel.Classes.Add(new ClassSessionViewModel
                            {
                                CourseName = att.Session.CourseAssignment?.Course?.CourseName ?? "Unknown",
                                TeacherName = att.Session.CourseAssignment?.Teacher?.FirstName + " " + att.Session.CourseAssignment?.Teacher?.LastName,
                                StartTime = att.Session.StartTime,
                                EndTime = att.Session.EndTime,
                                Status = status,
                                StatusColor = color
                            });
                        }
                    }

                    // Sort classes by time
                    dayViewModel.Classes = dayViewModel.Classes.OrderBy(c => c.StartTime).ToList();

                    if (dayViewModel.Classes.Any())
                    {
                        weekViewModel.Days.Add(dayViewModel);
                    }
                }

                if (weekViewModel.Days.Any())
                {
                    weeks.Add(weekViewModel);
                }

                currentWeekStart = currentWeekStart.AddDays(7);
            }

            model.Courses = grouped;
            model.Weeks = weeks;

            return View(model);
        }

        // GET: Attendance/TeacherReport
        public async Task<IActionResult> TeacherReport(int? teacherId)
        {
            // Auto-detect teacher if user is a Teacher and no teacherId provided
            if (!teacherId.HasValue && User.IsInRole("Teacher"))
            {
                var userId = User.FindFirstValue("UserId");
                if (!string.IsNullOrEmpty(userId))
                {
                    var currentTeacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == int.Parse(userId));
                    if (currentTeacher != null)
                    {
                        teacherId = currentTeacher.TeacherId;
                    }
                }
            }

            var model = new TeacherAttendanceReportViewModel
            {
                SelectedTeacherId = teacherId,
                Batches = new List<TeacherBatchAttendanceViewModel>()
            };

            // Populate Teacher List
            model.TeacherList = await _context.Teachers
                .Select(t => new SelectListItem { Value = t.TeacherId.ToString(), Text = $"{t.FirstName} {t.LastName}", Selected = t.TeacherId == teacherId })
                .ToListAsync();

            if (!teacherId.HasValue)
            {
                return View(model);
            }

            var teacher = await _context.Teachers.FindAsync(teacherId);
            if (teacher == null) return NotFound();

            model.TeacherName = teacher.FirstName + " " + teacher.LastName;

            // Get all course assignments for this teacher
            var assignments = await _context.CourseAssignments
                .Include(ca => ca.Course)
                .Include(ca => ca.Batch)
                .Include(ca => ca.Semester)
                .Where(ca => ca.TeacherId == teacherId)
                .ToListAsync();

            // Get all sessions marked by this teacher (or for these assignments)
            var sessions = await _context.Sessions
                .Include(s => s.Attendances)
                .Include(s => s.CourseAssignment)
                .ThenInclude(ca => ca.Course)
                .Where(s => s.CourseAssignment.TeacherId == teacherId)
                .OrderByDescending(s => s.SessionDate)
                .ToListAsync();

            // Group by Batch/Semester
            var groupedAssignments = assignments.GroupBy(a => new { a.BatchId, a.Batch.BatchName, a.SemesterId, a.Semester.SemesterName });

            foreach (var group in groupedAssignments)
            {
                var batchModel = new TeacherBatchAttendanceViewModel
                {
                    BatchName = group.Key.BatchName,
                    SemesterName = group.Key.SemesterName,
                    Sessions = new List<TeacherSessionViewModel>()
                };

                // 1. Add existing sessions (Marked)
                var groupSessions = sessions.Where(s =>
                    s.CourseAssignment.BatchId == group.Key.BatchId &&
                    s.CourseAssignment.SemesterId == group.Key.SemesterId).ToList();

                // Get timetable slots to find SlotIds and Pending sessions
                var timetable = await _context.Timetables
                    .Include(t => t.TimetableSlots)
                    .ThenInclude(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Course)
                    .FirstOrDefaultAsync(t => t.BatchId == group.Key.BatchId && t.SemesterId == group.Key.SemesterId);

                List<TimetableSlot> slots = new List<TimetableSlot>();
                if (timetable != null)
                {
                    slots = timetable.TimetableSlots
                        .Where(ts => ts.CourseAssignment != null && ts.CourseAssignment.TeacherId == teacherId)
                        .ToList();
                }

                // Map existing sessions
                foreach (var session in groupSessions)
                {
                    // Try to find matching slot to get SlotId
                    var matchingSlot = slots.FirstOrDefault(s =>
                        s.CourseAssignmentId == session.CourseAssignmentId &&
                        s.StartTime == session.StartTime &&
                        s.DayOfWeek == (int)session.SessionDate.DayOfWeek);

                    batchModel.Sessions.Add(new TeacherSessionViewModel
                    {
                        Date = session.SessionDate,
                        CourseName = session.CourseAssignment?.Course?.CourseName ?? "Unknown",
                        StartTime = session.StartTime,
                        EndTime = session.EndTime,
                        Status = "Marked",
                        TotalStudents = session.Attendances.Count,
                        PresentCount = session.Attendances.Count(a => a.Status == "Present"),
                        SlotId = matchingSlot?.SlotId ?? 0
                    });
                }

                // 2. Add Pending sessions from Timetable
                if (timetable != null && slots.Any())
                {
                    var startDate = group.First().Semester.StartDate;
                    var endDate = group.First().Semester.EndDate;
                    var today = DateOnly.FromDateTime(DateTime.Now);

                    // Show pending sessions up to today
                    var loopEnd = endDate < today ? endDate : today;

                    if (startDate <= loopEnd)
                    {
                        var current = startDate;
                        while (current <= loopEnd)
                        {
                            var daySlots = slots.Where(s => s.DayOfWeek == (int)current.DayOfWeek).ToList();
                            foreach (var slot in daySlots)
                            {
                                // Check if already added as Marked
                                // We check if any session in the list has the same Date and SlotId (if SlotId was found)
                                // Or same Date, StartTime and CourseAssignmentId
                                bool alreadyExists = batchModel.Sessions.Any(s =>
                                    s.Date == current &&
                                    (s.SlotId == slot.SlotId || (s.StartTime == slot.StartTime && s.CourseName == slot.CourseAssignment.Course.CourseName)));

                                if (!alreadyExists)
                                {
                                    batchModel.Sessions.Add(new TeacherSessionViewModel
                                    {
                                        Date = current,
                                        CourseName = slot.CourseAssignment.Course.CourseName,
                                        StartTime = slot.StartTime ?? default,
                                        EndTime = slot.EndTime ?? default,
                                        Status = "Pending",
                                        TotalStudents = 0,
                                        PresentCount = 0,
                                        SlotId = slot.SlotId
                                    });
                                }
                            }
                            current = current.AddDays(1);
                        }
                    }
                }

                // Sort by date descending
                batchModel.Sessions = batchModel.Sessions.OrderByDescending(s => s.Date).ThenBy(s => s.StartTime).ToList();
                model.Batches.Add(batchModel);
            }

            return View(model);
        }

        #region Student Report Export Methods

        [HttpGet]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> ExportStudentReportToExcel(int studentId, int? batchId, int? semesterId, int? courseId, string duration)
        {
            var student = await _context.Students
                .Include(s => s.Batch)
                .FirstOrDefaultAsync(s => s.StudentId == studentId);
            if (student == null) return NotFound();

            var courses = await GetStudentCourseAttendance(studentId, semesterId, courseId);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Attendance Report");

            // Title
            ws.Cell(1, 1).Value = "Student Attendance Report";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 16;
            ws.Range(1, 1, 1, 6).Merge();

            // Student Info
            ws.Cell(2, 1).Value = $"Student: {student.FirstName} {student.LastName}";
            ws.Cell(3, 1).Value = $"Roll Number: {student.RollNumber}";
            ws.Cell(4, 1).Value = $"Batch: {student.Batch?.BatchName ?? "N/A"}";
            ws.Cell(5, 1).Value = $"Generated: {DateTime.Now:MMMM dd, yyyy}";

            // Headers
            var row = 7;
            ws.Cell(row, 1).Value = "Course";
            ws.Cell(row, 2).Value = "Semester";
            ws.Cell(row, 3).Value = "Teacher";
            ws.Cell(row, 4).Value = "Total";
            ws.Cell(row, 5).Value = "Present";
            ws.Cell(row, 6).Value = "Absent";
            ws.Cell(row, 7).Value = "Percentage";
            ws.Range(row, 1, row, 7).Style.Font.Bold = true;
            ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = XLColor.LightGray;

            // Data
            row++;
            int totalClasses = 0, totalPresent = 0, totalAbsent = 0;
            foreach (var course in courses)
            {
                ws.Cell(row, 1).Value = course.CourseName;
                ws.Cell(row, 2).Value = course.SemesterName;
                ws.Cell(row, 3).Value = course.TeacherName;
                ws.Cell(row, 4).Value = course.TotalSessions;
                ws.Cell(row, 5).Value = course.PresentSessions;
                ws.Cell(row, 6).Value = course.AbsentSessions;
                ws.Cell(row, 7).Value = $"{Math.Round(course.Percentage, 1)}%";
                totalClasses += course.TotalSessions;
                totalPresent += course.PresentSessions;
                totalAbsent += course.AbsentSessions;
                row++;
            }

            // Summary
            row++;
            ws.Cell(row, 1).Value = "Total";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 4).Value = totalClasses;
            ws.Cell(row, 5).Value = totalPresent;
            ws.Cell(row, 6).Value = totalAbsent;
            ws.Cell(row, 7).Value = totalClasses > 0 ? $"{Math.Round((double)totalPresent / totalClasses * 100, 1)}%" : "0%";
            ws.Range(row, 1, row, 7).Style.Font.Bold = true;

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;
            var fileName = $"StudentAttendance_{student.RollNumber}_{DateTime.Now:yyyyMMdd}.xlsx";
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        [HttpGet]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> ExportStudentReportToPdf(int studentId, int? batchId, int? semesterId, int? courseId, string duration)
        {
            var student = await _context.Students
                .Include(s => s.Batch)
                .FirstOrDefaultAsync(s => s.StudentId == studentId);
            if (student == null) return NotFound();

            var courses = await GetStudentCourseAttendance(studentId, semesterId, courseId);
            var institution = await _institutionService.GetInstitutionInfoAsync();

            using var stream = new MemoryStream();
            var document = new Document(PageSize.A4, 40, 40, 40, 60);
            var writer = PdfWriter.GetInstance(document, stream);

            // Add page events for header/footer
            writer.PageEvent = new PdfHeaderFooter(institution, _webHostEnvironment);

            document.Open();

            // Title
            var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 18);
            var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
            var normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

            document.Add(new Paragraph("Student Attendance Report", titleFont));
            document.Add(new Paragraph(" "));
            document.Add(new Paragraph($"Student: {student.FirstName} {student.LastName}", normalFont));
            document.Add(new Paragraph($"Roll Number: {student.RollNumber}", normalFont));
            document.Add(new Paragraph($"Batch: {student.Batch?.BatchName ?? "N/A"}", normalFont));
            document.Add(new Paragraph($"Generated: {DateTime.Now:MMMM dd, yyyy}", normalFont));
            document.Add(new Paragraph(" "));

            // Table
            var table = new PdfPTable(7) { WidthPercentage = 100 };
            table.SetWidths(new float[] { 2f, 1.5f, 2f, 0.8f, 0.8f, 0.8f, 0.8f });

            // Headers
            string[] headers = { "Course", "Semester", "Teacher", "Total", "Present", "Absent", "%" };
            foreach (var header in headers)
            {
                var cell = new PdfPCell(new Phrase(header, headerFont))
                {
                    BackgroundColor = new BaseColor(229, 231, 235),
                    Padding = 5
                };
                table.AddCell(cell);
            }

            // Data
            int totalClasses = 0, totalPresent = 0, totalAbsent = 0;
            foreach (var course in courses)
            {
                table.AddCell(new PdfPCell(new Phrase(course.CourseName, normalFont)) { Padding = 4 });
                table.AddCell(new PdfPCell(new Phrase(course.SemesterName, normalFont)) { Padding = 4 });
                table.AddCell(new PdfPCell(new Phrase(course.TeacherName, normalFont)) { Padding = 4 });
                table.AddCell(new PdfPCell(new Phrase(course.TotalSessions.ToString(), normalFont)) { Padding = 4, HorizontalAlignment = Element.ALIGN_CENTER });
                table.AddCell(new PdfPCell(new Phrase(course.PresentSessions.ToString(), normalFont)) { Padding = 4, HorizontalAlignment = Element.ALIGN_CENTER });
                table.AddCell(new PdfPCell(new Phrase(course.AbsentSessions.ToString(), normalFont)) { Padding = 4, HorizontalAlignment = Element.ALIGN_CENTER });
                table.AddCell(new PdfPCell(new Phrase($"{Math.Round(course.Percentage, 1)}%", normalFont)) { Padding = 4, HorizontalAlignment = Element.ALIGN_CENTER });
                totalClasses += course.TotalSessions;
                totalPresent += course.PresentSessions;
                totalAbsent += course.AbsentSessions;
            }

            // Summary row
            table.AddCell(new PdfPCell(new Phrase("Total", headerFont)) { Padding = 4, BackgroundColor = new BaseColor(243, 244, 246) });
            table.AddCell(new PdfPCell(new Phrase("", normalFont)) { Padding = 4, BackgroundColor = new BaseColor(243, 244, 246) });
            table.AddCell(new PdfPCell(new Phrase("", normalFont)) { Padding = 4, BackgroundColor = new BaseColor(243, 244, 246) });
            table.AddCell(new PdfPCell(new Phrase(totalClasses.ToString(), headerFont)) { Padding = 4, HorizontalAlignment = Element.ALIGN_CENTER, BackgroundColor = new BaseColor(243, 244, 246) });
            table.AddCell(new PdfPCell(new Phrase(totalPresent.ToString(), headerFont)) { Padding = 4, HorizontalAlignment = Element.ALIGN_CENTER, BackgroundColor = new BaseColor(243, 244, 246) });
            table.AddCell(new PdfPCell(new Phrase(totalAbsent.ToString(), headerFont)) { Padding = 4, HorizontalAlignment = Element.ALIGN_CENTER, BackgroundColor = new BaseColor(243, 244, 246) });
            var overallPct = totalClasses > 0 ? Math.Round((double)totalPresent / totalClasses * 100, 1) : 0;
            table.AddCell(new PdfPCell(new Phrase($"{overallPct}%", headerFont)) { Padding = 4, HorizontalAlignment = Element.ALIGN_CENTER, BackgroundColor = new BaseColor(243, 244, 246) });

            document.Add(table);
            document.Close();

            var fileName = $"StudentAttendance_{student.RollNumber}_{DateTime.Now:yyyyMMdd}.pdf";
            return File(stream.ToArray(), "application/pdf", fileName);
        }

        [HttpGet]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> ExportCourseAttendanceToPdf(int studentId, int courseId, int semesterId)
        {
            var student = await _context.Students
                .Include(s => s.Batch)
                .FirstOrDefaultAsync(s => s.StudentId == studentId);
            if (student == null) return NotFound("Student not found");

            var course = await _context.Courses.FindAsync(courseId);
            if (course == null) return NotFound("Course not found");

            var semester = await _context.Semesters.FindAsync(semesterId);
            if (semester == null) return NotFound("Semester not found");

            // Get all attendance records for this student, course, and semester
            var attendanceRecords = await _context.Attendances
                .Include(a => a.Session)
                    .ThenInclude(s => s.CourseAssignment)
                        .ThenInclude(ca => ca.Teacher)
                .Where(a => a.StudentId == studentId
                         && a.Session.CourseAssignment.CourseId == courseId
                         && a.Session.CourseAssignment.SemesterId == semesterId)
                .OrderByDescending(a => a.Session.SessionDate)
                .ThenByDescending(a => a.Session.StartTime)
                .ToListAsync();

            if (!attendanceRecords.Any())
                return NotFound("No attendance records found for this course");

            var teacherName = attendanceRecords.FirstOrDefault()?.Session?.CourseAssignment?.Teacher != null
                ? $"{attendanceRecords.First().Session.CourseAssignment.Teacher.FirstName} {attendanceRecords.First().Session.CourseAssignment.Teacher.LastName}"
                : "N/A";

            var institution = await _institutionService.GetInstitutionInfoAsync();

            using var stream = new MemoryStream();
            var document = new Document(PageSize.A4, 40, 40, 70, 60);
            var writer = PdfWriter.GetInstance(document, stream);

            // Add page events for header/footer
            writer.PageEvent = new PdfHeaderFooter(institution, _webHostEnvironment);
            document.Open();

            // Fonts
            var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16);
            var subtitleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
            var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
            var normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);
            var presentFont = new Font(Font.FontFamily.HELVETICA, 10, Font.BOLD, new BaseColor(22, 163, 74)); // green
            var absentFont = new Font(Font.FontFamily.HELVETICA, 10, Font.BOLD, new BaseColor(220, 38, 38)); // red

            // Title
            document.Add(new Paragraph("Course Attendance Report", titleFont));
            document.Add(new Paragraph(" "));

            // Student Info
            document.Add(new Paragraph($"Student: {student.FirstName} {student.LastName}", normalFont));
            document.Add(new Paragraph($"Roll Number: {student.RollNumber}", normalFont));
            document.Add(new Paragraph($"Batch: {student.Batch?.BatchName ?? "N/A"}", normalFont));
            document.Add(new Paragraph(" "));

            // Course Info
            document.Add(new Paragraph($"Course: {course.CourseName}", subtitleFont));
            document.Add(new Paragraph($"Semester: {semester.SemesterName}", normalFont));
            document.Add(new Paragraph($"Teacher: {teacherName}", normalFont));
            document.Add(new Paragraph($"Generated: {DateTime.Now:MMMM dd, yyyy HH:mm}", normalFont));
            document.Add(new Paragraph(" "));

            // Summary
            int totalSessions = attendanceRecords.Count();
            int presentCount = attendanceRecords.Count(a => a.Status == "Present" || a.Status == "Late");
            int absentCount = attendanceRecords.Count(a => a.Status == "Absent");
            double percentage = totalSessions > 0 ? Math.Round((double)presentCount / totalSessions * 100, 1) : 0;

            var summaryTable = new PdfPTable(4) { WidthPercentage = 100, SpacingBefore = 10, SpacingAfter = 10 };
            summaryTable.SetWidths(new float[] { 1f, 1f, 1f, 1f });

            var summaryCell1 = new PdfPCell(new Phrase($"Total Classes: {totalSessions}", headerFont)) { Padding = 8, BackgroundColor = new BaseColor(243, 244, 246), HorizontalAlignment = Element.ALIGN_CENTER };
            var summaryCell2 = new PdfPCell(new Phrase($"Present: {presentCount}", headerFont)) { Padding = 8, BackgroundColor = new BaseColor(220, 252, 231), HorizontalAlignment = Element.ALIGN_CENTER };
            var summaryCell3 = new PdfPCell(new Phrase($"Absent: {absentCount}", headerFont)) { Padding = 8, BackgroundColor = new BaseColor(254, 226, 226), HorizontalAlignment = Element.ALIGN_CENTER };
            var summaryCell4 = new PdfPCell(new Phrase($"Attendance: {percentage}%", headerFont)) { Padding = 8, BackgroundColor = new BaseColor(243, 244, 246), HorizontalAlignment = Element.ALIGN_CENTER };

            summaryTable.AddCell(summaryCell1);
            summaryTable.AddCell(summaryCell2);
            summaryTable.AddCell(summaryCell3);
            summaryTable.AddCell(summaryCell4);

            document.Add(summaryTable);

            // Attendance Details Table
            document.Add(new Paragraph("Date-wise Attendance", subtitleFont));
            document.Add(new Paragraph(" "));

            var table = new PdfPTable(5) { WidthPercentage = 100 };
            table.SetWidths(new float[] { 1f, 1.5f, 1f, 1f, 1f });

            // Headers
            string[] headers = { "S.No", "Date", "Day", "Time", "Status" };
            foreach (var header in headers)
            {
                var cell = new PdfPCell(new Phrase(header, headerFont))
                {
                    BackgroundColor = new BaseColor(229, 231, 235),
                    Padding = 6,
                    HorizontalAlignment = Element.ALIGN_CENTER
                };
                table.AddCell(cell);
            }

            // Data rows
            int serialNo = 1;
            foreach (var record in attendanceRecords)
            {
                var session = record.Session;
                var date = session?.SessionDate ?? DateOnly.MinValue;
                var dayName = date != DateOnly.MinValue ? date.DayOfWeek.ToString() : "N/A";
                var startTime = session != null ? session.StartTime.ToString("hh:mm tt") : "N/A";
                var endTime = session != null ? session.EndTime.ToString("hh:mm tt") : "N/A";
                var timeRange = $"{startTime} - {endTime}";

                table.AddCell(new PdfPCell(new Phrase(serialNo.ToString(), normalFont)) { Padding = 5, HorizontalAlignment = Element.ALIGN_CENTER });
                table.AddCell(new PdfPCell(new Phrase(date.ToString("MMM dd, yyyy"), normalFont)) { Padding = 5, HorizontalAlignment = Element.ALIGN_CENTER });
                table.AddCell(new PdfPCell(new Phrase(dayName, normalFont)) { Padding = 5, HorizontalAlignment = Element.ALIGN_CENTER });
                table.AddCell(new PdfPCell(new Phrase(timeRange, normalFont)) { Padding = 5, HorizontalAlignment = Element.ALIGN_CENTER });

                // Status with color
                var statusFont = record.Status == "Present" || record.Status == "Late" ? presentFont : absentFont;
                var statusBgColor = record.Status == "Present" || record.Status == "Late"
                    ? new BaseColor(220, 252, 231) // green-100
                    : new BaseColor(254, 226, 226); // red-100
                var statusCell = new PdfPCell(new Phrase(record.Status ?? "N/A", statusFont))
                {
                    Padding = 5,
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    BackgroundColor = statusBgColor
                };
                table.AddCell(statusCell);

                serialNo++;
            }

            document.Add(table);
            document.Close();

            var fileName = $"CourseAttendance_{student.RollNumber}_{course.CourseName.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd}.pdf";
            return File(stream.ToArray(), "application/pdf", fileName);
        }

        private async Task<List<StudentCourseAttendanceViewModel>> GetStudentCourseAttendance(int studentId, int? semesterId, int? courseId)
        {
            var query = _context.Attendances
                .Include(a => a.Session)
                    .ThenInclude(s => s.CourseAssignment)
                        .ThenInclude(ca => ca.Course)
                .Include(a => a.Session)
                    .ThenInclude(s => s.CourseAssignment)
                        .ThenInclude(ca => ca.Semester)
                .Include(a => a.Session)
                    .ThenInclude(s => s.CourseAssignment)
                        .ThenInclude(ca => ca.Teacher)
                .Where(a => a.StudentId == studentId)
                .Where(a => a.Session.CourseAssignment.CourseId != null && a.Session.CourseAssignment.SemesterId != null);

            if (semesterId.HasValue)
                query = query.Where(a => a.Session.CourseAssignment.SemesterId == semesterId);
            if (courseId.HasValue)
                query = query.Where(a => a.Session.CourseAssignment.CourseId == courseId);

            var records = await query.ToListAsync();

            var grouped = records
                .GroupBy(a => new {
                    CourseId = a.Session.CourseAssignment.CourseId!.Value,
                    SemesterId = a.Session.CourseAssignment.SemesterId!.Value
                })
                .Select(g => new StudentCourseAttendanceViewModel
                {
                    CourseId = g.Key.CourseId,
                    SemesterId = g.Key.SemesterId,
                    CourseName = g.First().Session.CourseAssignment.Course?.CourseName ?? "Unknown Course",
                    SemesterName = g.First().Session.CourseAssignment.Semester?.SemesterName ?? "Unknown Semester",
                    TeacherName = g.First().Session.CourseAssignment.Teacher != null
                        ? $"{g.First().Session.CourseAssignment.Teacher.FirstName} {g.First().Session.CourseAssignment.Teacher.LastName}"
                        : "N/A",
                    TotalSessions = g.Count(),
                    PresentSessions = g.Count(a => a.Status == "Present" || a.Status == "Late"),
                    AbsentSessions = g.Count(a => a.Status == "Absent"),
                    LateSessions = g.Count(a => a.Status == "Late"),
                    Percentage = g.Any() ? (double)g.Count(a => a.Status == "Present" || a.Status == "Late") / g.Count() * 100 : 0,
                    History = g.Select(a => new AttendanceRecordViewModel
                    {
                        Date = a.Session.SessionDate,
                        StartTime = a.Session.StartTime,
                        EndTime = a.Session.EndTime,
                        Status = a.Status
                    }).OrderByDescending(h => h.Date).ThenByDescending(h => h.StartTime).ToList()
                })
                .ToList();

            return grouped;
        }

        #endregion

        #region Teacher Report Export Methods

        [HttpGet]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> ExportTeacherReportToExcel(int? teacherId)
        {
            int actualTeacherId;
            if (User.IsInRole("Teacher"))
            {
                var userId = User.FindFirstValue("UserId");
                var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == int.Parse(userId));
                if (teacher == null) return NotFound();
                actualTeacherId = teacher.TeacherId;
            }
            else
            {
                if (!teacherId.HasValue) return BadRequest("Teacher ID required");
                actualTeacherId = teacherId.Value;
            }

            var teacherEntity = await _context.Teachers.FirstOrDefaultAsync(t => t.TeacherId == actualTeacherId);
            if (teacherEntity == null) return NotFound();

            var batches = await GetTeacherBatchesWithSessions(actualTeacherId);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Teacher Report");

            // Title
            ws.Cell(1, 1).Value = "Teacher Attendance Report";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 16;
            ws.Range(1, 1, 1, 7).Merge();

            // Teacher Info
            ws.Cell(2, 1).Value = $"Teacher: {teacherEntity.FirstName} {teacherEntity.LastName}";
            ws.Cell(3, 1).Value = $"Generated: {DateTime.Now:MMMM dd, yyyy}";

            int row = 5;

            foreach (var batch in batches)
            {
                // Batch header
                ws.Cell(row, 1).Value = $"{batch.BatchName} - {batch.SemesterName}";
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 1).Style.Font.FontSize = 12;
                ws.Range(row, 1, row, 7).Merge();
                ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
                row++;

                // Headers
                ws.Cell(row, 1).Value = "Date";
                ws.Cell(row, 2).Value = "Day";
                ws.Cell(row, 3).Value = "Course";
                ws.Cell(row, 4).Value = "Time";
                ws.Cell(row, 5).Value = "Status";
                ws.Cell(row, 6).Value = "Present";
                ws.Cell(row, 7).Value = "Total";
                ws.Range(row, 1, row, 7).Style.Font.Bold = true;
                ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = XLColor.LightGray;
                row++;

                foreach (var session in batch.Sessions)
                {
                    ws.Cell(row, 1).Value = session.Date.ToString("MMM dd, yyyy");
                    ws.Cell(row, 2).Value = session.Date.ToString("ddd");
                    ws.Cell(row, 3).Value = session.CourseName;
                    ws.Cell(row, 4).Value = $"{session.StartTime:hh\\:mm} - {session.EndTime:hh\\:mm}";
                    ws.Cell(row, 5).Value = session.Status;
                    ws.Cell(row, 6).Value = session.PresentCount;
                    ws.Cell(row, 7).Value = session.TotalStudents;
                    row++;
                }

                // Summary
                var markedSessions = batch.Sessions.Where(s => s.Status == "Marked").ToList();
                var pendingSessions = batch.Sessions.Where(s => s.Status == "Pending").ToList();
                ws.Cell(row, 1).Value = $"Marked: {markedSessions.Count} | Pending: {pendingSessions.Count}";
                ws.Cell(row, 1).Style.Font.Italic = true;
                ws.Range(row, 1, row, 7).Merge();
                row += 2;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;
            var fileName = $"TeacherReport_{teacherEntity.FirstName}_{teacherEntity.LastName}_{DateTime.Now:yyyyMMdd}.xlsx";
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        [HttpGet]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> ExportTeacherReportToPdf(int? teacherId)
        {
            int actualTeacherId;
            if (User.IsInRole("Teacher"))
            {
                var userId = User.FindFirstValue("UserId");
                var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == int.Parse(userId));
                if (teacher == null) return NotFound();
                actualTeacherId = teacher.TeacherId;
            }
            else
            {
                if (!teacherId.HasValue) return BadRequest("Teacher ID required");
                actualTeacherId = teacherId.Value;
            }

            var teacherEntity = await _context.Teachers.FirstOrDefaultAsync(t => t.TeacherId == actualTeacherId);
            if (teacherEntity == null) return NotFound();

            var batches = await GetTeacherBatchesWithSessions(actualTeacherId);
            var institution = await _institutionService.GetInstitutionInfoAsync();

            using var stream = new MemoryStream();
            var document = new Document(PageSize.A4.Rotate(), 40, 40, 70, 60);
            var writer = PdfWriter.GetInstance(document, stream);

            // Add page events for header/footer
            writer.PageEvent = new PdfHeaderFooter(institution, _webHostEnvironment);

            document.Open();

            var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 18);
            var sectionFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
            var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9);
            var normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 9);

            document.Add(new Paragraph("Teacher Attendance Report", titleFont));
            document.Add(new Paragraph($"Teacher: {teacherEntity.FirstName} {teacherEntity.LastName}", normalFont));
            document.Add(new Paragraph($"Generated: {DateTime.Now:MMMM dd, yyyy}", normalFont));
            document.Add(new Paragraph(" "));

            foreach (var batch in batches)
            {
                document.Add(new Paragraph($"{batch.BatchName} - {batch.SemesterName}", sectionFont));
                document.Add(new Paragraph(" "));

                var table = new PdfPTable(7) { WidthPercentage = 100 };
                table.SetWidths(new float[] { 1.2f, 0.8f, 2f, 1.2f, 0.8f, 0.6f, 0.6f });

                // Headers
                string[] headers = { "Date", "Day", "Course", "Time", "Status", "Present", "Total" };
                foreach (var header in headers)
                {
                    var cell = new PdfPCell(new Phrase(header, headerFont))
                    {
                        BackgroundColor = new BaseColor(229, 231, 235),
                        Padding = 4
                    };
                    table.AddCell(cell);
                }

                foreach (var session in batch.Sessions)
                {
                    table.AddCell(new PdfPCell(new Phrase(session.Date.ToString("MMM dd"), normalFont)) { Padding = 3 });
                    table.AddCell(new PdfPCell(new Phrase(session.Date.ToString("ddd"), normalFont)) { Padding = 3 });
                    table.AddCell(new PdfPCell(new Phrase(session.CourseName, normalFont)) { Padding = 3 });
                    table.AddCell(new PdfPCell(new Phrase($"{session.StartTime:hh\\:mm}-{session.EndTime:hh\\:mm}", normalFont)) { Padding = 3 });

                    var statusColor = session.Status == "Marked" ? new BaseColor(220, 252, 231) : new BaseColor(254, 249, 195);
                    table.AddCell(new PdfPCell(new Phrase(session.Status, normalFont)) { Padding = 3, BackgroundColor = statusColor });

                    table.AddCell(new PdfPCell(new Phrase(session.PresentCount.ToString(), normalFont)) { Padding = 3, HorizontalAlignment = Element.ALIGN_CENTER });
                    table.AddCell(new PdfPCell(new Phrase(session.TotalStudents.ToString(), normalFont)) { Padding = 3, HorizontalAlignment = Element.ALIGN_CENTER });
                }

                document.Add(table);

                var markedCount = batch.Sessions.Count(s => s.Status == "Marked");
                var pendingCount = batch.Sessions.Count(s => s.Status == "Pending");
                document.Add(new Paragraph($"Marked: {markedCount} | Pending: {pendingCount}", normalFont));
                document.Add(new Paragraph(" "));
            }

            document.Close();

            var fileName = $"TeacherReport_{teacherEntity.FirstName}_{teacherEntity.LastName}_{DateTime.Now:yyyyMMdd}.pdf";
            return File(stream.ToArray(), "application/pdf", fileName);
        }

        private async Task<List<TeacherBatchAttendanceViewModel>> GetTeacherBatchesWithSessions(int teacherId)
        {
            var assignments = await _context.CourseAssignments
                .Include(ca => ca.Course)
                .Include(ca => ca.Batch)
                .Include(ca => ca.Semester)
                .Where(ca => ca.TeacherId == teacherId)
                .ToListAsync();

            var result = new List<TeacherBatchAttendanceViewModel>();

            foreach (var grp in assignments.GroupBy(a => new { a.BatchId, a.SemesterId }))
            {
                var first = grp.First();
                var batchModel = new TeacherBatchAttendanceViewModel
                {
                    BatchName = first.Batch?.BatchName ?? "Unknown",
                    SemesterName = first.Semester?.SemesterName ?? "Unknown",
                    Sessions = new List<TeacherSessionViewModel>()
                };

                // Get sessions
                var assignmentIds = grp.Select(a => a.AssignmentId).ToList();
                var sessions = await _context.Sessions
                    .Include(s => s.CourseAssignment)
                        .ThenInclude(ca => ca.Course)
                    .Include(s => s.Attendances)
                    .Where(s => s.CourseAssignmentId != null && assignmentIds.Contains(s.CourseAssignmentId.Value))
                    .ToListAsync();

                foreach (var sess in sessions)
                {
                    batchModel.Sessions.Add(new TeacherSessionViewModel
                    {
                        Date = sess.SessionDate,
                        CourseName = sess.CourseAssignment?.Course?.CourseName ?? "Unknown",
                        StartTime = sess.StartTime,
                        EndTime = sess.EndTime,
                        Status = "Marked",
                        TotalStudents = sess.Attendances.Count,
                        PresentCount = sess.Attendances.Count(a => a.Status == "Present" || a.Status == "Late"),
                        SlotId = 0
                    });
                }

                batchModel.Sessions = batchModel.Sessions.OrderByDescending(s => s.Date).ThenBy(s => s.StartTime).ToList();
                result.Add(batchModel);
            }

            return result;
        }

        #endregion

        #region Session Attendance Export (Mark Page)

        [HttpGet]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> ExportSessionAttendanceToPdf(int slotId, DateOnly date)
        {
            var slot = await _context.TimetableSlots
                .Include(ts => ts.Timetable)
                .Include(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Course)
                .Include(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Batch)
                .Include(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Semester)
                .Include(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Teacher)
                .FirstOrDefaultAsync(ts => ts.SlotId == slotId);

            if (slot == null) return NotFound("Slot not found.");

            // Security Check: If user is a Teacher, ensure they own this slot
            if (User.IsInRole("Teacher"))
            {
                var userId = User.FindFirstValue("UserId");
                var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == int.Parse(userId));
                if (teacher == null || slot.CourseAssignment.TeacherId != teacher.TeacherId)
                {
                    return Forbid();
                }
            }

            var assignment = slot.CourseAssignment;
            var slotBatchId = slot.Timetable?.BatchId ?? assignment.BatchId;

            // Get students enrolled in this course/semester using same logic as Mark action
            var enrollments = await _context.Enrollments
                .Include(e => e.Student)
                .Where(e => e.CourseId == assignment.CourseId && e.SemesterId == assignment.SemesterId && e.Status == "Active")
                .ToListAsync();

            // Filter enrollments based on the target batch (same logic as LoadAttendanceView)
            var validEnrollments = enrollments.Where(e =>
                (e.BatchId.HasValue && e.BatchId == slotBatchId) ||
                (!e.BatchId.HasValue && e.Student.BatchId == slotBatchId)
            ).ToList();

            var students = validEnrollments
                .Select(e => e.Student)
                .Where(s => s != null)
                .OrderBy(s => s.RollNumber)
                .ToList();

            var session = await _context.Sessions
                .Include(s => s.Attendances)
                .FirstOrDefaultAsync(s => s.CourseAssignmentId == slot.CourseAssignmentId && s.SessionDate == date && s.StartTime == slot.StartTime);

            var attendanceData = new List<(Student Student, string Status)>();
            foreach (var student in students)
            {
                var attendance = session?.Attendances.FirstOrDefault(a => a.StudentId == student.StudentId);
                attendanceData.Add((student, attendance?.Status ?? "Not Marked"));
            }

            var institution = await _institutionService.GetInstitutionInfoAsync();

            using var stream = new MemoryStream();
            var document = new Document(PageSize.A4, 40, 40, 70, 60);
            var writer = PdfWriter.GetInstance(document, stream);
            writer.PageEvent = new PdfHeaderFooter(institution, _webHostEnvironment);

            document.Open();

            var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16);
            var sectionFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11);
            var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
            var normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

            document.Add(new Paragraph("Class Attendance Report", titleFont));
            document.Add(new Paragraph(" "));

            // Class Info
            document.Add(new Paragraph($"Course: {slot.CourseAssignment.Course?.CourseName ?? "N/A"}", sectionFont));
            document.Add(new Paragraph($"Batch: {slot.CourseAssignment.Batch?.BatchName ?? "N/A"}", normalFont));
            document.Add(new Paragraph($"Semester: {slot.CourseAssignment.Semester?.SemesterName ?? "N/A"}", normalFont));
            document.Add(new Paragraph($"Teacher: {slot.CourseAssignment.Teacher?.FirstName} {slot.CourseAssignment.Teacher?.LastName}", normalFont));
            document.Add(new Paragraph($"Date: {date.ToString("dddd, MMMM dd, yyyy")}", normalFont));
            document.Add(new Paragraph($"Time: {slot.StartTime:hh\\:mm tt} - {slot.EndTime:hh\\:mm tt}", normalFont));
            document.Add(new Paragraph(" "));

            // Summary
            var presentCount = attendanceData.Count(a => a.Status == "Present");
            var absentCount = attendanceData.Count(a => a.Status == "Absent");
            var lateCount = attendanceData.Count(a => a.Status == "Late");
            var excusedCount = attendanceData.Count(a => a.Status == "Excused");
            var notMarkedCount = attendanceData.Count(a => a.Status == "Not Marked");

            document.Add(new Paragraph("Summary:", sectionFont));
            document.Add(new Paragraph($"Present: {presentCount} | Absent: {absentCount} | Late: {lateCount} | Excused: {excusedCount} | Not Marked: {notMarkedCount}", normalFont));
            document.Add(new Paragraph(" "));

            // Table
            var table = new PdfPTable(3) { WidthPercentage = 100 };
            table.SetWidths(new float[] { 1.5f, 3f, 1.5f });

            string[] headers = { "Roll No", "Student Name", "Status" };
            foreach (var header in headers)
            {
                var cell = new PdfPCell(new Phrase(header, headerFont))
                {
                    BackgroundColor = new BaseColor(229, 231, 235),
                    Padding = 6,
                    HorizontalAlignment = Element.ALIGN_CENTER
                };
                table.AddCell(cell);
            }

            foreach (var (student, status) in attendanceData)
            {
                table.AddCell(new PdfPCell(new Phrase(student.RollNumber, normalFont)) { Padding = 5 });
                table.AddCell(new PdfPCell(new Phrase($"{student.FirstName} {student.LastName}", normalFont)) { Padding = 5 });

                var statusColor = status switch
                {
                    "Present" => new BaseColor(220, 252, 231),
                    "Absent" => new BaseColor(254, 226, 226),
                    "Late" => new BaseColor(254, 249, 195),
                    "Excused" => new BaseColor(219, 234, 254),
                    _ => new BaseColor(243, 244, 246)
                };
                table.AddCell(new PdfPCell(new Phrase(status, normalFont)) { Padding = 5, BackgroundColor = statusColor, HorizontalAlignment = Element.ALIGN_CENTER });
            }

            document.Add(table);
            document.Close();

            var courseName = slot.CourseAssignment.Course?.CourseName?.Replace(" ", "_") ?? "Course";
            var fileName = $"Attendance_{courseName}_{date:yyyyMMdd}.pdf";
            return File(stream.ToArray(), "application/pdf", fileName);
        }

        [HttpGet]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> ExportSessionAttendanceToExcel(int slotId, DateOnly date)
        {
            var slot = await _context.TimetableSlots
                .Include(ts => ts.Timetable)
                .Include(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Course)
                .Include(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Batch)
                .Include(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Semester)
                .Include(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca.Teacher)
                .FirstOrDefaultAsync(ts => ts.SlotId == slotId);

            if (slot == null) return NotFound("Slot not found.");

            // Security Check: If user is a Teacher, ensure they own this slot
            if (User.IsInRole("Teacher"))
            {
                var userId = User.FindFirstValue("UserId");
                var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == int.Parse(userId));
                if (teacher == null || slot.CourseAssignment.TeacherId != teacher.TeacherId)
                {
                    return Forbid();
                }
            }

            var assignment = slot.CourseAssignment;
            var slotBatchId = slot.Timetable?.BatchId ?? assignment.BatchId;

            // Get students enrolled in this course/semester using same logic as Mark action
            var enrollments = await _context.Enrollments
                .Include(e => e.Student)
                .Where(e => e.CourseId == assignment.CourseId && e.SemesterId == assignment.SemesterId && e.Status == "Active")
                .ToListAsync();

            // Filter enrollments based on the target batch (same logic as LoadAttendanceView)
            var validEnrollments = enrollments.Where(e =>
                (e.BatchId.HasValue && e.BatchId == slotBatchId) ||
                (!e.BatchId.HasValue && e.Student.BatchId == slotBatchId)
            ).ToList();

            var students = validEnrollments
                .Select(e => e.Student)
                .Where(s => s != null)
                .OrderBy(s => s.RollNumber)
                .ToList();

            var session = await _context.Sessions
                .Include(s => s.Attendances)
                .FirstOrDefaultAsync(s => s.CourseAssignmentId == slot.CourseAssignmentId && s.SessionDate == date && s.StartTime == slot.StartTime);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Class Attendance");

            // Title
            ws.Cell(1, 1).Value = "Class Attendance Report";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 16;
            ws.Range(1, 1, 1, 3).Merge();

            // Class Info
            ws.Cell(3, 1).Value = $"Course: {slot.CourseAssignment.Course?.CourseName ?? "N/A"}";
            ws.Cell(4, 1).Value = $"Batch: {slot.CourseAssignment.Batch?.BatchName ?? "N/A"}";
            ws.Cell(5, 1).Value = $"Semester: {slot.CourseAssignment.Semester?.SemesterName ?? "N/A"}";
            ws.Cell(6, 1).Value = $"Teacher: {slot.CourseAssignment.Teacher?.FirstName} {slot.CourseAssignment.Teacher?.LastName}";
            ws.Cell(7, 1).Value = $"Date: {date.ToString("dddd, MMMM dd, yyyy")}";
            ws.Cell(8, 1).Value = $"Time: {slot.StartTime:hh\\:mm tt} - {slot.EndTime:hh\\:mm tt}";

            // Headers
            var row = 10;
            ws.Cell(row, 1).Value = "Roll No";
            ws.Cell(row, 2).Value = "Student Name";
            ws.Cell(row, 3).Value = "Status";
            ws.Range(row, 1, row, 3).Style.Font.Bold = true;
            ws.Range(row, 1, row, 3).Style.Fill.BackgroundColor = XLColor.LightGray;

            // Data
            row++;
            int presentCount = 0, absentCount = 0, lateCount = 0, excusedCount = 0;
            foreach (var student in students)
            {
                var attendance = session?.Attendances.FirstOrDefault(a => a.StudentId == student.StudentId);
                var status = attendance?.Status ?? "Not Marked";

                ws.Cell(row, 1).Value = student.RollNumber;
                ws.Cell(row, 2).Value = $"{student.FirstName} {student.LastName}";
                ws.Cell(row, 3).Value = status;

                // Color coding
                var color = status switch
                {
                    "Present" => XLColor.LightGreen,
                    "Absent" => XLColor.LightPink,
                    "Late" => XLColor.LightYellow,
                    "Excused" => XLColor.LightBlue,
                    _ => XLColor.LightGray
                };
                ws.Cell(row, 3).Style.Fill.BackgroundColor = color;

                if (status == "Present") presentCount++;
                else if (status == "Absent") absentCount++;
                else if (status == "Late") lateCount++;
                else if (status == "Excused") excusedCount++;

                row++;
            }

            // Summary
            row += 2;
            ws.Cell(row, 1).Value = "Summary";
            ws.Cell(row, 1).Style.Font.Bold = true;
            row++;
            ws.Cell(row, 1).Value = "Present:";
            ws.Cell(row, 2).Value = presentCount;
            row++;
            ws.Cell(row, 1).Value = "Absent:";
            ws.Cell(row, 2).Value = absentCount;
            row++;
            ws.Cell(row, 1).Value = "Late:";
            ws.Cell(row, 2).Value = lateCount;
            row++;
            ws.Cell(row, 1).Value = "Excused:";
            ws.Cell(row, 2).Value = excusedCount;
            row++;
            ws.Cell(row, 1).Value = "Total:";
            ws.Cell(row, 2).Value = students.Count;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Style.Font.Bold = true;

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;

            var courseName = slot.CourseAssignment.Course?.CourseName?.Replace(" ", "_") ?? "Course";
            var fileName = $"Attendance_{courseName}_{date:yyyyMMdd}.xlsx";
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        #endregion
    }
}
