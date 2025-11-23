using AMS.Models;
using AMS.Models.Entities;
using AMS.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
//rendering
using Microsoft.AspNetCore.Mvc.Rendering;


namespace AMS.Controllers
{
    [Authorize(Roles = "Student")]
    public class StudentPortalController : Controller
    {
        private readonly ApplicationDbContext _context;

        public StudentPortalController(ApplicationDbContext context)
        {
            _context = context;
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
                .Select(s => new SelectListItem { Value = s.SemesterId.ToString(), Text = s.SemesterName, Selected = s.SemesterId == semesterId })
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

            // Group by Course
            model.Courses = attendances.GroupBy(a => new
            {
                a.Session.CourseAssignment.Course.CourseName,
                a.Session.CourseAssignment.Semester.SemesterName,
                a.Session.CourseAssignment.Teacher.FirstName,
                a.Session.CourseAssignment.Teacher.LastName
            })
            .Select(g => new StudentCourseAttendanceViewModel
            {
                CourseName = g.Key.CourseName,
                SemesterName = g.Key.SemesterName,
                TeacherName = $"{g.Key.FirstName} {g.Key.LastName}",
                TotalSessions = g.Count(),
                PresentSessions = g.Count(a => a.Status == "Present"),
                AbsentSessions = g.Count(a => a.Status == "Absent"),
                Percentage = g.Count() > 0 ? (double)g.Count(a => a.Status == "Present") / g.Count() * 100 : 0,
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
    }
}
