using Microsoft.AspNetCore.Mvc.Rendering;

namespace AMS.Models.ViewModels
{
    /// <summary>
    /// Main view model for the student's My Courses page showing enrolled and available courses
    /// </summary>
    public class StudentEnrollmentViewModel
    {
        public string StudentName { get; set; } = string.Empty;
        public string RollNumber { get; set; } = string.Empty;
        public string BatchName { get; set; } = string.Empty;
        public int StudentId { get; set; }

        // Current semester info
        public int? CurrentSemesterId { get; set; }
        public string CurrentSemesterName { get; set; } = string.Empty;

        // Filter
        public int? SelectedSemesterId { get; set; }
        public List<SelectListItem> SemesterList { get; set; } = new();

        // Enrolled courses
        public List<EnrolledCourseViewModel> EnrolledCourses { get; set; } = new();

        // Available courses for enrollment (in current semester)
        public List<AvailableCourseViewModel> AvailableCourses { get; set; } = new();
    }

    /// <summary>
    /// Represents a course the student is currently enrolled in
    /// </summary>
    public class EnrolledCourseViewModel
    {
        public int EnrollmentId { get; set; }
        public int CourseId { get; set; }
        public string CourseCode { get; set; } = string.Empty;
        public string CourseName { get; set; } = string.Empty;
        public int CreditHours { get; set; }
        public string TeacherName { get; set; } = string.Empty;
        public string SemesterName { get; set; } = string.Empty;
        public int SemesterId { get; set; }
        public string Status { get; set; } = string.Empty;

        // Attendance summary
        public int TotalSessions { get; set; }
        public int PresentSessions { get; set; }
        public double AttendancePercentage { get; set; }

        // Timetable info
        public List<CourseScheduleSlot> Schedule { get; set; } = new();

        // Can student unenroll? (only if no attendance marked yet)
        public bool CanUnenroll { get; set; }
        public string UnenrollBlockedReason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a course available for enrollment
    /// </summary>
    public class AvailableCourseViewModel
    {
        public int CourseAssignmentId { get; set; }
        public int CourseId { get; set; }
        public string CourseCode { get; set; } = string.Empty;
        public string CourseName { get; set; } = string.Empty;
        public int CreditHours { get; set; }
        public string TeacherName { get; set; } = string.Empty;
        public string BatchName { get; set; } = string.Empty;
        public int BatchId { get; set; }
        public int SemesterId { get; set; }
        public string SemesterName { get; set; } = string.Empty;

        // Timetable info
        public List<CourseScheduleSlot> Schedule { get; set; } = new();

        // Enrollment status
        public bool CanEnroll { get; set; } = true;
        public string EnrollBlockedReason { get; set; } = string.Empty;

        // Is this from student's own batch?
        public bool IsOwnBatch { get; set; }
    }

    /// <summary>
    /// Represents a single schedule slot (day + time)
    /// </summary>
    public class CourseScheduleSlot
    {
        public string DayName { get; set; } = string.Empty;
        public int DayOfWeek { get; set; }
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
        public string DisplayText => $"{DayName} {StartTime} - {EndTime}";
    }
}
