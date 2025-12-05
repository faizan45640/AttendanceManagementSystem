using Microsoft.AspNetCore.Mvc;
using AMS.Models;
using AMS.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
using AMS.Models.Entities;
using Microsoft.AspNetCore.Authorization;

namespace AMS.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {

        private readonly ApplicationDbContext _context;
        public AdminController(ApplicationDbContext context)
        {
            _context = context;
        }
        private bool IsAdmin()
        {
            return HttpContext.Session.GetString("Role") == "Admin";
        }
        [Authorize(Roles = "Admin")]

        public async Task<IActionResult> Dashboard()
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var todayDayOfWeek = (int)DateTime.Today.DayOfWeek;
            var sixMonthsAgo = today.AddMonths(-6);

            var viewModel = new AdminDashboardViewModel();

            // Summary Stats
            viewModel.TotalStudents = await _context.Students.CountAsync(s => s.IsActive == true);
            viewModel.TotalTeachers = await _context.Teachers.CountAsync(t => t.IsActive == true);
            viewModel.TotalCourses = await _context.Courses.CountAsync(c => c.IsActive == true);
            viewModel.TotalBatches = await _context.Batches.CountAsync(b => b.IsActive);
            viewModel.ActiveSemesters = await _context.Semesters.CountAsync(s => s.IsActive);
            viewModel.TotalEnrollments = await _context.Enrollments.CountAsync(e => e.Status == "Active");

            // Attendance Status Counts
            var allAttendances = await _context.Attendances.ToListAsync();
            viewModel.PresentCount = allAttendances.Count(a => a.Status == "Present");
            viewModel.AbsentCount = allAttendances.Count(a => a.Status == "Absent");
            viewModel.LateCount = allAttendances.Count(a => a.Status == "Late");
            viewModel.ExcusedCount = allAttendances.Count(a => a.Status == "Excused");

            var totalAttendanceRecords = allAttendances.Count;
            viewModel.OverallAttendanceRate = totalAttendanceRecords > 0
                ? Math.Round((double)(viewModel.PresentCount + viewModel.LateCount) / totalAttendanceRecords * 100, 1)
                : 0;

            viewModel.TotalSessions = await _context.Sessions.CountAsync();

            // Monthly Attendance Trend (Last 6 months)
            var monthlyRawData = await _context.Attendances
                .Include(a => a.Session)
                .Where(a => a.Session != null && a.Session.SessionDate >= sixMonthsAgo)
                .ToListAsync();

            var monthlyData = monthlyRawData
                .Where(a => a.Session != null)
                .Select(a => new { Attendance = a, Date = a.Session!.SessionDate })
                .GroupBy(x => new { Year = x.Date.Year, Month = x.Date.Month })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Total = g.Count(),
                    Present = g.Count(x => x.Attendance.Status == "Present" || x.Attendance.Status == "Late")
                })
                .OrderBy(x => x.Year).ThenBy(x => x.Month)
                .ToList();

            foreach (var data in monthlyData)
            {
                viewModel.MonthlyLabels.Add(new DateTime(data.Year, data.Month, 1).ToString("MMM"));
                viewModel.MonthlyAttendanceValues.Add(data.Total > 0 ? Math.Round((double)data.Present / data.Total * 100, 1) : 0);
            }

            // Weekly Attendance Pattern (by day of week)
            var weeklyData = await _context.Attendances
                .Include(a => a.Session)
                .Where(a => a.Session != null)
                .ToListAsync();

            var dayNames = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
            for (int i = 0; i < 7; i++)
            {
                var dayAttendances = weeklyData.Where(a => a.Session != null && (int)a.Session.SessionDate.DayOfWeek == i).ToList();
                var total = dayAttendances.Count;
                var present = dayAttendances.Count(a => a.Status == "Present" || a.Status == "Late");
                viewModel.WeeklyLabels.Add(dayNames[i]);
                viewModel.WeeklyAttendanceValues.Add(total > 0 ? Math.Round((double)present / total * 100.0, 1) : 0.0);
            }

            // Attendance by Batch
            var batchData = await _context.Batches
                .Where(b => b.IsActive)
                .Select(b => new
                {
                    BatchName = b.BatchName,
                    Students = b.Students.Where(s => s.IsActive == true).Select(s => s.StudentId).ToList()
                })
                .ToListAsync();

            foreach (var batch in batchData)
            {
                var batchAttendances = allAttendances.Where(a => batch.Students.Contains(a.StudentId ?? 0)).ToList();
                var total = batchAttendances.Count;
                var present = batchAttendances.Count(a => a.Status == "Present" || a.Status == "Late");
                viewModel.BatchLabels.Add(batch.BatchName ?? "Unknown");
                viewModel.BatchAttendanceValues.Add(total > 0 ? Math.Round((double)present / total * 100, 1) : 0);
            }

            // Attendance by Course
            var courseData = await _context.Courses
                .Where(c => c.IsActive == true)
                .Select(c => new
                {
                    CourseName = c.CourseName,
                    SessionIds = c.CourseAssignments
                        .SelectMany(ca => ca.Sessions)
                        .Select(s => s.SessionId)
                        .ToList()
                })
                .Take(6)
                .ToListAsync();

            foreach (var course in courseData)
            {
                var courseAttendances = allAttendances.Where(a => course.SessionIds.Contains(a.SessionId ?? 0)).ToList();
                var total = courseAttendances.Count;
                var present = courseAttendances.Count(a => a.Status == "Present" || a.Status == "Late");
                viewModel.CourseLabels.Add(course.CourseName ?? "Unknown");
                viewModel.CourseAttendanceValues.Add(total > 0 ? Math.Round((double)present / total * 100, 1) : 0);
            }

            // Enrollment by Batch
            var enrollmentByBatch = await _context.Batches
                .Where(b => b.IsActive)
                .Select(b => new
                {
                    BatchName = b.BatchName,
                    StudentCount = b.Students.Count(s => s.IsActive == true)
                })
                .ToListAsync();

            foreach (var batch in enrollmentByBatch)
            {
                viewModel.EnrollmentBatchLabels.Add(batch.BatchName ?? "Unknown");
                viewModel.EnrollmentBatchValues.Add(batch.StudentCount);
            }

            // Top Performing Students
            var studentAttendanceData = await _context.Students
                .Where(s => s.IsActive == true)
                .Include(s => s.Batch)
                .Include(s => s.Attendances)
                .Select(s => new
                {
                    s.StudentId,
                    StudentName = (s.FirstName ?? "") + " " + (s.LastName ?? ""),
                    s.RollNumber,
                    BatchName = s.Batch != null ? s.Batch.BatchName : "N/A",
                    TotalClasses = s.Attendances.Count,
                    PresentClasses = s.Attendances.Count(a => a.Status == "Present" || a.Status == "Late")
                })
                .Where(s => s.TotalClasses > 0)
                .ToListAsync();

            viewModel.TopStudents = studentAttendanceData
                .Select(s => new TopStudentViewModel
                {
                    StudentId = s.StudentId,
                    StudentName = s.StudentName.Trim(),
                    RollNumber = s.RollNumber ?? "",
                    BatchName = s.BatchName ?? "",
                    TotalClasses = s.TotalClasses,
                    PresentClasses = s.PresentClasses,
                    AttendancePercentage = Math.Round((double)s.PresentClasses / s.TotalClasses * 100, 1)
                })
                .OrderByDescending(s => s.AttendancePercentage)
                .Take(5)
                .ToList();

            // Low Attendance Students (below 75%)
            viewModel.LowAttendanceStudents = studentAttendanceData
                .Where(s => (double)s.PresentClasses / s.TotalClasses * 100 < 75)
                .Select(s => new LowAttendanceStudentViewModel
                {
                    StudentId = s.StudentId,
                    StudentName = s.StudentName.Trim(),
                    RollNumber = s.RollNumber ?? "",
                    BatchName = s.BatchName ?? "",
                    TotalClasses = s.TotalClasses,
                    AbsentClasses = s.TotalClasses - s.PresentClasses,
                    AttendancePercentage = Math.Round((double)s.PresentClasses / s.TotalClasses * 100, 1)
                })
                .OrderBy(s => s.AttendancePercentage)
                .Take(5)
                .ToList();

            // Recent Attendance Records
            viewModel.RecentAttendances = await _context.Attendances
                .Include(a => a.Student)
                    .ThenInclude(s => s!.Batch)
                .Include(a => a.Session)
                    .ThenInclude(s => s!.CourseAssignment)
                        .ThenInclude(ca => ca!.Course)
                .Include(a => a.MarkedByNavigation)
                .OrderByDescending(a => a.Session!.SessionDate)
                .ThenByDescending(a => a.Session!.StartTime)
                .Take(10)
                .Select(a => new RecentAttendanceViewModel
                {
                    AttendanceId = a.AttendanceId,
                    StudentName = (a.Student!.FirstName ?? "") + " " + (a.Student.LastName ?? ""),
                    CourseName = a.Session!.CourseAssignment!.Course!.CourseName ?? "N/A",
                    BatchName = a.Student.Batch != null ? a.Student.Batch.BatchName ?? "N/A" : "N/A",
                    Status = a.Status ?? "Unknown",
                    SessionDate = a.Session.SessionDate,
                    SessionTime = a.Session.StartTime,
                    MarkedBy = a.MarkedByNavigation != null ? a.MarkedByNavigation.Username ?? "System" : "System"
                })
                .ToListAsync();

            // Today's Scheduled Classes
            viewModel.TodayClasses = await _context.TimetableSlots
                .Include(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca!.Course)
                .Include(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca!.Teacher)
                .Include(ts => ts.CourseAssignment)
                    .ThenInclude(ca => ca!.Batch)
                .Include(ts => ts.Timetable)
                .Where(ts => ts.DayOfWeek == todayDayOfWeek && ts.Timetable!.IsActive == true)
                .OrderBy(ts => ts.StartTime)
                .Select(ts => new TodayClassViewModel
                {
                    SlotId = ts.SlotId,
                    CourseName = ts.CourseAssignment!.Course!.CourseName ?? "N/A",
                    CourseCode = ts.CourseAssignment.Course.CourseCode ?? "N/A",
                    TeacherName = (ts.CourseAssignment.Teacher!.FirstName ?? "") + " " + (ts.CourseAssignment.Teacher.LastName ?? ""),
                    BatchName = ts.CourseAssignment.Batch!.BatchName ?? "N/A",
                    StartTime = ts.StartTime,
                    EndTime = ts.EndTime,
                    HasSession = ts.CourseAssignment.Sessions.Any(s => s.SessionDate == today)
                })
                .ToListAsync();

            // Teacher Performance
            viewModel.TeacherPerformances = await _context.Teachers
                .Where(t => t.IsActive == true)
                .Include(t => t.CourseAssignments)
                    .ThenInclude(ca => ca.Sessions)
                        .ThenInclude(s => s.Attendances)
                .Include(t => t.CourseAssignments)
                    .ThenInclude(ca => ca.Batch)
                        .ThenInclude(b => b!.Students)
                .Select(t => new TeacherPerformanceViewModel
                {
                    TeacherId = t.TeacherId,
                    TeacherName = (t.FirstName ?? "") + " " + (t.LastName ?? ""),
                    TotalCourses = t.CourseAssignments.Count(ca => ca.IsActive == true),
                    TotalSessions = t.CourseAssignments.SelectMany(ca => ca.Sessions).Count(),
                    TotalStudents = t.CourseAssignments
                        .Where(ca => ca.Batch != null)
                        .SelectMany(ca => ca.Batch!.Students)
                        .Where(s => s.IsActive == true)
                        .Select(s => s.StudentId)
                        .Distinct()
                        .Count(),
                    AverageAttendance = t.CourseAssignments
                        .SelectMany(ca => ca.Sessions)
                        .SelectMany(s => s.Attendances)
                        .Count() > 0
                        ? Math.Round(
                            (double)t.CourseAssignments
                                .SelectMany(ca => ca.Sessions)
                                .SelectMany(s => s.Attendances)
                                .Count(a => a.Status == "Present" || a.Status == "Late")
                            / t.CourseAssignments
                                .SelectMany(ca => ca.Sessions)
                                .SelectMany(s => s.Attendances)
                                .Count() * 100, 1)
                        : 0
                })
                .OrderByDescending(t => t.AverageAttendance)
                .Take(5)
                .ToListAsync();

            return View(viewModel);
        }
        public async Task<IActionResult> Users()
        {

            var users = await _context.Users.ToListAsync();
            return View(users);
        }


        //=======================================Manage Admin Controllers ========================================//
        //see admins
        public async Task<IActionResult> Admins(DateTime? fromDate, DateTime? toDate, string? status, string? search)
        {

            var query = _context.Admins
        .Include(a => a.User)
        .AsQueryable();

            // Apply date filter
            if (fromDate.HasValue)
            {
                query = query.Where(a => a.User.CreatedAt >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                // Include the entire day
                var endDate = toDate.Value.AddDays(1);
                query = query.Where(a => a.User.CreatedAt < endDate);
            }
            if (!string.IsNullOrEmpty(status))
            {
                bool isActive = status.ToLower() == "active";
                query = query.Where(a => a.User.IsActive == isActive);
            }

            // Apply search filter for username, email, first name, or last name
            if (!string.IsNullOrEmpty(search))
            {
                search = search.ToLower();
                query = query.Where(a =>
                    a.User.Username.ToLower().Contains(search) ||
                    a.User.Email.ToLower().Contains(search) ||
                    (a.FirstName != null && a.FirstName.ToLower().Contains(search)) ||
                    (a.LastName != null && a.LastName.ToLower().Contains(search))
                );
            }

            var admins = await query.OrderByDescending(a => a.User.CreatedAt).ToListAsync();

            var viewModel = new AdminFilterViewModel
            {
                FromDate = fromDate,
                ToDate = toDate,
                Status = status,
                Search = search,
                Admins = admins
            };

            return View(viewModel);
            // Apply status filter

        }
        //add admin 
        [HttpGet]
        public IActionResult AddAdmin()
        {


            return View();
        }
        [HttpPost]
        public async Task<IActionResult> AddAdmin(AddUserViewModel model)
        {

            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray()
                    );
                return BadRequest(new { success = false, message = "Invalid data submitted.", errors });
            }
            //check duplicate email
            var existingEmail = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == model.Email);
            if (existingEmail != null)
            {
                return Conflict(new { success = false, message = "Email already in use." });
            }
            //check duplicate username
            var existingUsername = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == model.Username);
            if (existingUsername != null)
            {
                return Conflict(new { success = false, message = "Username already in use." });
            }

            var user = new User
            {
                Username = model.Username,
                Email = model.Email,
                PasswordHash = model.Password,
                Role = "Admin",
                IsActive = true,
                CreatedAt = DateTime.UtcNow

            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var admin = new Admin
            {
                UserId = user.UserId,   // foreign key link
                FirstName = model.FirstName,
                LastName = model.LastName
            };

            _context.Admins.Add(admin);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Admin added successfully." });
        }





        [HttpGet]
        //return only the form
        public IActionResult EditAdmin(int UserId)
        {

            if (UserId <= 0)
            {
                TempData["Error"] = "Invalid User Id";
                return View();
            }
            //create admin and return in model

            var admin = _context.Admins
                .Include(a => a.User)
                .FirstOrDefault(a => a.UserId == UserId);


            if (admin == null || admin.User == null)
            {
                TempData["Error"] = "Admin not found";
                return View();
            }




            var viewModel = new AddUserViewModel
            {
                UserId = admin.UserId,
                Username = admin.User.Username,
                Email = admin.User.Email,
                FirstName = admin.FirstName,
                LastName = admin.LastName,
                Role = "Admin"
            };

            return View(viewModel);

        }

        [HttpPost]
        public IActionResult EditAdmin(AddUserViewModel model)
        {
            //remove password for 
            if (string.IsNullOrWhiteSpace(model.Password))
            {
                ModelState.Remove("Password");
            }
            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray()
                    );
                return BadRequest(new { success = false, message = "Invalid data submitted.", errors });
            }

            var admin = _context.Admins
                .Include(a => a.User)
                .FirstOrDefault(a => a.UserId == model.UserId);

            if (admin == null || admin.User == null)
            {
                return NotFound(new { success = false, message = "Admin not found." });
            }

            //check for email existing for another user
            var existingUser = _context.Users
                .FirstOrDefault(u => u.Email == model.Email && u.UserId != model.UserId);
            if (existingUser != null)
            {
                return Conflict(new { success = false, message = "Email already in use by another user." });
            }

            //check for username existing for another user
            var existingUsername = _context.Users
                .FirstOrDefault(u => u.Username == model.Username && u.UserId != model.UserId);
            if (existingUsername != null)
            {
                return Conflict(new { success = false, message = "Username already in use by another user." });
            }

            //update user details
            admin.User.Username = model.Username;
            admin.User.Email = model.Email;
            if (!string.IsNullOrEmpty(model.Password))
            {
                admin.User.PasswordHash = model.Password;
            }
            //update admin details
            admin.FirstName = model.FirstName;
            admin.LastName = model.LastName;

            _context.SaveChanges();

            return Ok(new { success = true, message = "Admin details updated successfully." });
        }

        //Delete admin
        [HttpPost]
        public IActionResult DeleteAdmin(int UserId)
        {
            var admin = _context.Admins
                .Include(a => a.User)
                .FirstOrDefault(a => a.UserId == UserId);

            if (admin == null || admin.User == null)
            {
                return NotFound(new { success = false, message = "Admin not found." });
            }

            // Remove admin and associated user
            _context.Users.Remove(admin.User);
            _context.Admins.Remove(admin);
            _context.SaveChanges();

            return Ok(new { success = true, message = "Admin deleted successfully." });
        }




        [HttpGet]
        public IActionResult AddUser()
        {


            return View();
        }

        [HttpPost]
        public async Task<IActionResult> AddUser(AddUserViewModel model)
        {

            if (!ModelState.IsValid)
            {
                return View(model);
            }
            //check duplicate email
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == model.Email);
            if (existingUser != null)
            {
                ModelState.AddModelError("Email", "Email already in use");
                return View(model);
            }
            var user = new User
            {
                Username = model.Username,
                Email = model.Email,
                PasswordHash = model.Password,
                Role = model.Role,
                IsActive = true,
                CreatedAt = DateTime.UtcNow

            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            ViewBag.Success = "User added successfully";
            return RedirectToAction("Users");
        }


    }
}
