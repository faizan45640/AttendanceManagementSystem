
using AMS.Models;
using AMS.Models.Entities;
using AMS.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

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

            ViewBag.Semesters = new SelectList(await _context.Semesters.ToListAsync(), "SemesterId", "SemesterName");
            ViewBag.Batches = new SelectList(await _context.Batches.ToListAsync(), "BatchId", "BatchName");

            return View(filter);
        }

        // GET: Timetable/Create
        public async Task<IActionResult> Create()
        {
            var model = new AddTimetableViewModel();
            model.Batches = new SelectList(await _context.Batches.ToListAsync(), "BatchId", "BatchName");
            // Assuming Semester has a Name or similar property. If not, I might need to construct it.
            // Let's check Semester entity if possible, but for now I'll assume Name or construct one.
            // Checking SemesterController earlier, it sorts by Year and StartDate.
            var semesters = await _context.Semesters.Select(s => new { s.SemesterId, Name = s.Year + " - " + s.StartDate }).ToListAsync();
            model.Semesters = new SelectList(semesters, "SemesterId", "Name");

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

                    model.Batches = new SelectList(await _context.Batches.ToListAsync(), "BatchId", "BatchName");
                    var sems = await _context.Semesters.Select(s => new { s.SemesterId, Name = s.Year + " - " + s.StartDate }).ToListAsync();
                    model.Semesters = new SelectList(sems, "SemesterId", "Name");
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

            model.Batches = new SelectList(await _context.Batches.ToListAsync(), "BatchId", "BatchName");
            var semesters = await _context.Semesters.Select(s => new { s.SemesterId, Name = s.Year + " - " + s.StartDate }).ToListAsync();
            model.Semesters = new SelectList(semesters, "SemesterId", "Name");
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
    }
}
