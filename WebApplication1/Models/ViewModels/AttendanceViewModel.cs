using Microsoft.AspNetCore.Mvc.Rendering;
namespace AMS.Models.ViewModels
{
    public class MarkAttendanceViewModel
    {
        public int SlotId { get; set; }
        public int CourseAssignmentId { get; set; }
        public string CourseName { get; set; } = string.Empty;
        public string BatchName { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public List<DateOnly> ValidDates { get; set; } = new List<DateOnly>();
        public List<StudentAttendanceViewModel> Students { get; set; } = new List<StudentAttendanceViewModel>();
    }

    public class StudentAttendanceViewModel
    {
        public int StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public string RollNumber { get; set; } = string.Empty;
        public string Status { get; set; } = "Present"; // Default to Present
        public bool IsMarked { get; set; }
        public List<WeeklyAttendanceViewModel> Weeks { get; set; } = new List<WeeklyAttendanceViewModel>();
    }
    public class WeeklyAttendanceViewModel
    {
        public string WeekRange { get; set; } // e.g., "Nov 17 - Nov 23"
        public List<DailyAttendanceViewModel> Days { get; set; } = new List<DailyAttendanceViewModel>();
    }

    public class DailyAttendanceViewModel
    {
        public DateOnly Date { get; set; }
        public string DayName { get; set; }
        public List<ClassSessionViewModel> Classes { get; set; } = new List<ClassSessionViewModel>();
    }

    public class ClassSessionViewModel
    {
        public string CourseName { get; set; }
        public string TeacherName { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public string Status { get; set; } // Present, Absent, Not Marked, etc.
        public string StatusColor { get; set; } // For UI styling
    }
    public class StudentAttendanceReportViewModel
    {
        public int? SelectedBatchId { get; set; }
        public int? SelectedSemesterId { get; set; }
        public int? SelectedCourseId { get; set; }
        public int? SelectedStudentId { get; set; }
        public string SearchRollNumber { get; set; }

        public List<SelectListItem> BatchList { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> SemesterList { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> CourseList { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> StudentList { get; set; } = new List<SelectListItem>();
        public string StudentName { get; set; }
        public string RollNumber { get; set; }
        public string BatchName { get; set; }
        public string Duration { get; set; } // "week", "month", "all"

        public List<WeeklyAttendanceViewModel> Weeks { get; set; } = new List<WeeklyAttendanceViewModel>();
        public List<StudentCourseAttendanceViewModel> Courses { get; set; } = new List<StudentCourseAttendanceViewModel>();
    }

    public class StudentCourseAttendanceViewModel
    {
        public int LateSessions { get; set; }
        public int CourseId { get; set; }
        public int SemesterId { get; set; }
        public string CourseName { get; set; }
        public string SemesterName { get; set; }
        public string TeacherName { get; set; }
        public int TotalSessions { get; set; }
        public int PresentSessions { get; set; }
        public int AbsentSessions { get; set; }
        public double Percentage { get; set; }
        public List<AttendanceRecordViewModel> History { get; set; } = new List<AttendanceRecordViewModel>();
    }

    public class AttendanceRecordViewModel
    {
        public DateOnly Date { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public string Status { get; set; }
    }
    public class TeacherAttendanceReportViewModel
    {
        public int? SelectedTeacherId { get; set; }
        public List<SelectListItem> TeacherList { get; set; } = new List<SelectListItem>();
        public string TeacherName { get; set; }
        public List<TeacherBatchAttendanceViewModel> Batches { get; set; }
    }

    public class TeacherBatchAttendanceViewModel
    {
        public string BatchName { get; set; }
        public string SemesterName { get; set; }
        public List<TeacherSessionViewModel> Sessions { get; set; }
    }

    public class TeacherSessionViewModel
    {
        public DateOnly Date { get; set; }
        public string CourseName { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public string Status { get; set; } // Marked, Pending
        public int TotalStudents { get; set; }
        public int PresentCount { get; set; }
        public int SlotId { get; set; } // To link to Mark action
    }
}
