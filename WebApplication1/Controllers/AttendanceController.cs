using AMS.Models;
using AMS.Models.Entities;
using AMS.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AMS.Controllers
{
    public class AttendanceController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AttendanceController(ApplicationDbContext context)
        {
            _context = context;
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
                .ThenInclude(s => s.User)
                .Where(e => e.CourseId == assignment.CourseId && e.SemesterId == assignment.SemesterId && e.Status == "Active")
                .ToListAsync();

            // If CourseAssignment has a specific BatchId, filter by it
            // Also filter by Timetable's BatchId to be sure we are marking for the correct batch
            if (slot.Timetable?.BatchId != null)
            {
                enrollments = enrollments.Where(e => e.Student.BatchId == slot.Timetable.BatchId).ToList();
            }
            else if (assignment.BatchId.HasValue)
            {
                enrollments = enrollments.Where(e => e.Student.BatchId == assignment.BatchId).ToList();
            }

            foreach (var enrollment in enrollments)
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

            // Populate Dropdowns
            model.BatchList = await _context.Batches
                .Select(b => new SelectListItem { Value = b.BatchId.ToString(), Text = b.BatchName, Selected = b.BatchId == batchId })
                .ToListAsync();

            model.SemesterList = await _context.Semesters
                .Select(s => new SelectListItem { Value = s.SemesterId.ToString(), Text = s.SemesterName, Selected = s.SemesterId == semesterId })
                .ToListAsync();

            model.CourseList = await _context.Courses
                .Select(c => new SelectListItem { Value = c.CourseId.ToString(), Text = c.CourseName, Selected = c.CourseId == courseId })
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

            // Populate Student List if Batch is selected
            if (batchId.HasValue)
            {
                model.StudentList = await _context.Students
                   .Where(s => s.BatchId == batchId)
                   .Select(s => new SelectListItem { Value = s.StudentId.ToString(), Text = $"{s.FirstName} {s.LastName} ({s.RollNumber})", Selected = s.StudentId == (student != null ? student.StudentId : null) })
                   .ToListAsync();
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
                .Where(a => a.StudentId == student.StudentId);

            // Apply Filters
            if (semesterId.HasValue)
            {
                query = query.Where(a => a.Session.CourseAssignment.SemesterId == semesterId);
            }
            if (courseId.HasValue)
            {
                query = query.Where(a => a.Session.CourseAssignment.CourseId == courseId);
            }

            var attendances = await query
                .OrderByDescending(a => a.Session.SessionDate)
                .ToListAsync();

            // Group by Course/Semester
            var grouped = attendances.GroupBy(a => new
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
                TeacherName = g.Key.FirstName + " " + g.Key.LastName,
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
                        else if (currentDate < todayDate)
                        {
                            // Past date with no attendance
                            status = "Pending";
                            color = "text-yellow-700 bg-yellow-50 dark:text-yellow-400 dark:bg-yellow-900/20";
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
    }
}
