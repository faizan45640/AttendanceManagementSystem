using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using AMS.Models.Entities;

namespace AMS.Models.ViewModels
{
    public class TimeTableViewModel
    {
    }
    public class AddTimetableViewModel
    {
        [Required]
        [Display(Name = "Batch")]
        public int? BatchId { get; set; }

        [Required]
        [Display(Name = "Semester")]
        public int? SemesterId { get; set; }

        [Display(Name = "Is Active")]
        public bool IsActive { get; set; } = true;

        public IEnumerable<SelectListItem>? Batches { get; set; }
        public IEnumerable<SelectListItem>? Semesters { get; set; }
    }
    public class EditTimetableViewModel
    {
        public int TimetableId { get; set; }

        [Display(Name = "Batch")]
        public string? BatchName { get; set; }

        public int? BatchYear { get; set; }

        [Display(Name = "Semester")]
        public string? SemesterName { get; set; }

        public int? SemesterYear { get; set; }

        public bool IsActive { get; set; }

        public bool CanMarkAttendance { get; set; } // Added for attendance permission

        public List<TimetableSlotViewModel> Slots { get; set; } = new List<TimetableSlotViewModel>();

        // For adding a new slot
        public AddTimetableSlotViewModel NewSlot { get; set; } = new AddTimetableSlotViewModel();

        public IEnumerable<SelectListItem>? CourseAssignments { get; set; }
    }

    public class TimetableSlotViewModel
    {
        public int SlotId { get; set; }
        public int? CourseAssignmentId { get; set; } // Added for attendance link
        public int DayOfWeek { get; set; }
        public string DayName { get; set; } = string.Empty;
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public string CourseName { get; set; } = string.Empty;
        public string TeacherName { get; set; } = string.Empty;
        public string Room { get; set; } = string.Empty;
        public string AttendanceStatus { get; set; } = "Pending"; 
    }

    public class AddTimetableSlotViewModel
    {
        public int TimetableId { get; set; }

        [Required]
        [Display(Name = "Course Assignment")]
        public int? CourseAssignmentId { get; set; }

        [Required]
        [Display(Name = "Day")]
        public int DayOfWeek { get; set; }

        [Required]
        [Display(Name = "Start Time")]
        public TimeOnly StartTime { get; set; }

        [Required]
        [Display(Name = "End Time")]
        public TimeOnly EndTime { get; set; }
    }
    public class TimetableFilterViewModel : PagedViewModel
    {

        public List<Timetable> Timetables { get; set; } = new List<Timetable>();
        public int? SemesterId { get; set; }
        public int? BatchId { get; set; }
        public string? Status { get; set; }
    }

}
