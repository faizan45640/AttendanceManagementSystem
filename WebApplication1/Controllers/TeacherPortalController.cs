using AMS.Models;
using AMS.Models.Entities;
using AMS.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AMS.Controllers
{
    [Authorize(Roles = "Teacher")]
    public class TeacherPortalController : Controller
    {
        private readonly ApplicationDbContext _context;

        public TeacherPortalController(ApplicationDbContext context)
        {
            _context = context;
        }

        private async Task<Teacher?> GetCurrentTeacherAsync()
        {
            var userId = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userId)) return null;
            return await _context.Teachers
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.UserId == int.Parse(userId));
        }

        public async Task<IActionResult> Dashboard()
        {
            var teacher = await GetCurrentTeacherAsync();
            if (teacher == null) return RedirectToAction("Login", "Auth");

            var today = DateOnly.FromDateTime(DateTime.Now);
            var dayOfWeek = (int)today.DayOfWeek;

            // Get today's classes from active timetables
            var todaySlots = await _context.TimetableSlots
                .Include(ts => ts.Timetable)
                .Include(ts => ts.CourseAssignment)
                .ThenInclude(ca => ca.Course)
                .Include(ts => ts.CourseAssignment)
                .ThenInclude(ca => ca.Batch)
                .Include(ts => ts.CourseAssignment)
                .ThenInclude(ca => ca.Semester)
                .Where(ts => ts.CourseAssignment.TeacherId == teacher.TeacherId
                             && ts.DayOfWeek == dayOfWeek
                             && ts.Timetable.IsActive == true)
                .OrderBy(ts => ts.StartTime)
                .ToListAsync();

            // Auto-redirect: If within first 10 mins of a class and not marked, go to Mark Attendance
            var currentTime = TimeOnly.FromDateTime(DateTime.Now);
            foreach (var slot in todaySlots)
            {
                if (slot.StartTime.HasValue)
                {
                    var start = slot.StartTime.Value;
                    var endWindow = start.AddMinutes(10);

                    if (currentTime >= start && currentTime <= endWindow)
                    {
                        // Check if already marked
                        var isMarked = await _context.Sessions
                            .AnyAsync(s => s.CourseAssignmentId == slot.CourseAssignmentId
                                           && s.SessionDate == today
                                           && s.StartTime == slot.StartTime);

                        if (!isMarked)
                        {
                            return RedirectToAction("Mark", "Attendance", new { slotId = slot.SlotId, date = today });
                        }
                    }
                }
            }

            // Check status for each slot (Marked/Pending)
            var dashboardModel = new TeacherDashboardViewModel
            {
                TeacherName = $"{teacher.FirstName} {teacher.LastName}",
                TodayDate = today,
                TodayClasses = new List<TeacherDashboardClassViewModel>()
            };

            foreach (var slot in todaySlots)
            {
                // Check if attendance is marked for today
                var isMarked = await _context.Sessions
                    .AnyAsync(s => s.CourseAssignmentId == slot.CourseAssignmentId
                                   && s.SessionDate == today
                                   && s.StartTime == slot.StartTime);

                dashboardModel.TodayClasses.Add(new TeacherDashboardClassViewModel
                {
                    SlotId = slot.SlotId,
                    CourseName = slot.CourseAssignment.Course.CourseName,
                    BatchName = slot.CourseAssignment.Batch.BatchName,
                    StartTime = slot.StartTime ?? default,
                    EndTime = slot.EndTime ?? default,
                    IsMarked = isMarked,
                   // Room = slot.Room
                });
            }

            // --- Stats Calculation ---

            // 1. Total Active Courses
            var activeAssignments = await _context.CourseAssignments
                .Include(ca => ca.Course)
                .Where(ca => ca.TeacherId == teacher.TeacherId && ca.IsActive == true)
                .ToListAsync();

            dashboardModel.TotalActiveCourses = activeAssignments.Count;

            // 2. Total Students (Unique students in these courses)
            var courseIds = activeAssignments.Select(ca => ca.CourseId).ToList();
            var batchIds = activeAssignments.Select(ca => ca.BatchId).ToList();

            // This is an approximation. Ideally we check enrollments for specific course/semester.
            // Assuming enrollments track active students in courses.
            var totalStudents = await _context.Enrollments
                .Where(e => courseIds.Contains(e.CourseId) && e.Status == "Active")
                // Filter by batch if needed, but courseId might be enough if unique per batch
                // But a student can be in multiple courses. We want unique students.
                .Select(e => e.StudentId)
                .Distinct()
                .CountAsync();

            dashboardModel.TotalStudents = totalStudents;

            // 3. Overall Attendance Rate & Graph Data
            // Get all sessions for this teacher
            var sessions = await _context.Sessions
                .Include(s => s.Attendances)
                .Include(s => s.CourseAssignment)
                .ThenInclude(ca => ca.Course)
                .Where(s => s.CourseAssignment.TeacherId == teacher.TeacherId)
                .ToListAsync();

            if (sessions.Any())
            {
                // Overall Rate
                double totalPresent = sessions.Sum(s => s.Attendances.Count(a => a.Status == "Present"));
                double totalRecords = sessions.Sum(s => s.Attendances.Count);
                dashboardModel.OverallAttendanceRate = totalRecords > 0 ? Math.Round((totalPresent / totalRecords) * 100, 1) : 0;

                // Graph 1: Attendance Trend (Last 7 Days with data)
                var recentSessions = sessions
                    .Where(s => s.SessionDate <= today)
                    .OrderByDescending(s => s.SessionDate)
                    .Take(30) // Take last 30 sessions to find last 7 days
                    .GroupBy(s => s.SessionDate)
                    .OrderBy(g => g.Key)
                    .TakeLast(7)
                    .ToList();

                foreach (var dayGroup in recentSessions)
                {
                    double dayPresent = dayGroup.Sum(s => s.Attendances.Count(a => a.Status == "Present"));
                    double dayTotal = dayGroup.Sum(s => s.Attendances.Count);
                    double dayRate = dayTotal > 0 ? Math.Round((dayPresent / dayTotal) * 100, 1) : 0;

                    dashboardModel.AttendanceLabels.Add(dayGroup.Key.ToString("MMM dd"));
                    dashboardModel.AttendanceValues.Add(dayRate);
                }

                // Graph 2: Attendance by Course
                var courseGroups = sessions
                    .GroupBy(s => s.CourseAssignment.Course.CourseName)
                    .ToList();

                foreach (var courseGroup in courseGroups)
                {
                    double coursePresent = courseGroup.Sum(s => s.Attendances.Count(a => a.Status == "Present"));
                    double courseTotal = courseGroup.Sum(s => s.Attendances.Count);
                    double courseRate = courseTotal > 0 ? Math.Round((coursePresent / courseTotal) * 100, 1) : 0;

                    dashboardModel.CourseLabels.Add(courseGroup.Key);
                    dashboardModel.CourseAttendanceValues.Add(courseRate);
                }
            }

            return View(dashboardModel);
        }

        public async Task<IActionResult> MyReport()
        {
            var teacher = await GetCurrentTeacherAsync();
            if (teacher == null) return RedirectToAction("Login", "Auth");

            // Redirect to the shared Attendance Controller but with the teacher's ID
            // We can reuse the logic by calling the method directly or redirecting
            // Redirecting is cleaner as it keeps the URL logic consistent, 
            // BUT we need to secure AttendanceController to not allow viewing others.
            // For now, let's redirect to a new action in AttendanceController that handles "My Report" securely
            return RedirectToAction("MyTeacherReport", "Attendance");
        }

        public async Task<IActionResult> MyCourses()
        {
            var teacher = await GetCurrentTeacherAsync();
            if (teacher == null) return RedirectToAction("Login", "Auth");

            var assignments = await _context.CourseAssignments
                .Include(ca => ca.Course)
                .Include(ca => ca.Batch)
                .Include(ca => ca.Semester)
                .Include(ca => ca.Sessions)
                .Where(ca => ca.TeacherId == teacher.TeacherId && ca.IsActive == true)
                .OrderByDescending(ca => ca.Semester.StartDate)
                .ThenBy(ca => ca.Course.CourseName)
                .ToListAsync();

            return View(assignments);
        }
    }
}
