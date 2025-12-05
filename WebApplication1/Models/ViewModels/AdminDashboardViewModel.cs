namespace AMS.Models.ViewModels
{
    public class AdminDashboardViewModel
    {
        // Summary Stats
        public int TotalStudents { get; set; }
        public int TotalTeachers { get; set; }
        public int TotalCourses { get; set; }
        public int TotalBatches { get; set; }
        public int ActiveSemesters { get; set; }
        public int TotalEnrollments { get; set; }

        // Overall Attendance
        public double OverallAttendanceRate { get; set; }
        public int TotalSessions { get; set; }

        // Attendance by Status for Pie Chart
        public int PresentCount { get; set; }
        public int AbsentCount { get; set; }
        public int LateCount { get; set; }
        public int ExcusedCount { get; set; }

        // Monthly Attendance Trend (Line Chart) - Last 6 months
        public List<string> MonthlyLabels { get; set; } = new();
        public List<double> MonthlyAttendanceValues { get; set; } = new();

        // Weekly Attendance Trend (Bar Chart) - By day of week
        public List<string> WeeklyLabels { get; set; } = new();
        public List<double> WeeklyAttendanceValues { get; set; } = new();

        // Attendance by Batch (Bar Chart)
        public List<string> BatchLabels { get; set; } = new();
        public List<double> BatchAttendanceValues { get; set; } = new();

        // Attendance by Course (Horizontal Bar Chart)
        public List<string> CourseLabels { get; set; } = new();
        public List<double> CourseAttendanceValues { get; set; } = new();

        // Top Performing Students (by attendance)
        public List<TopStudentViewModel> TopStudents { get; set; } = new();

        // Students with Low Attendance
        public List<LowAttendanceStudentViewModel> LowAttendanceStudents { get; set; } = new();

        // Recent Attendance Records
        public List<RecentAttendanceViewModel> RecentAttendances { get; set; } = new();

        // Today's Scheduled Classes
        public List<TodayClassViewModel> TodayClasses { get; set; } = new();

        // Teacher Performance
        public List<TeacherPerformanceViewModel> TeacherPerformances { get; set; } = new();

        // Enrollment by Batch (Doughnut Chart)
        public List<string> EnrollmentBatchLabels { get; set; } = new();
        public List<int> EnrollmentBatchValues { get; set; } = new();
    }

    public class TopStudentViewModel
    {
        public int StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public string RollNumber { get; set; } = string.Empty;
        public string BatchName { get; set; } = string.Empty;
        public double AttendancePercentage { get; set; }
        public int TotalClasses { get; set; }
        public int PresentClasses { get; set; }
    }

    public class LowAttendanceStudentViewModel
    {
        public int StudentId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public string RollNumber { get; set; } = string.Empty;
        public string BatchName { get; set; } = string.Empty;
        public double AttendancePercentage { get; set; }
        public int AbsentClasses { get; set; }
        public int TotalClasses { get; set; }
    }

    public class RecentAttendanceViewModel
    {
        public int AttendanceId { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public string CourseName { get; set; } = string.Empty;
        public string BatchName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateOnly? SessionDate { get; set; }
        public TimeOnly? SessionTime { get; set; }
        public string MarkedBy { get; set; } = string.Empty;
    }

    public class TodayClassViewModel
    {
        public int SlotId { get; set; }
        public string CourseName { get; set; } = string.Empty;
        public string CourseCode { get; set; } = string.Empty;
        public string TeacherName { get; set; } = string.Empty;
        public string BatchName { get; set; } = string.Empty;
        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }
        public bool HasSession { get; set; }
    }

    public class TeacherPerformanceViewModel
    {
        public int TeacherId { get; set; }
        public string TeacherName { get; set; } = string.Empty;
        public int TotalCourses { get; set; }
        public int TotalSessions { get; set; }
        public int TotalStudents { get; set; }
        public double AverageAttendance { get; set; }
    }
}
