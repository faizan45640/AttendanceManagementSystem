namespace AMS.Models.ViewModels
{
    public class TeacherDashboardViewModel
    {
        public string TeacherName { get; set; }
        public DateOnly TodayDate { get; set; }
        public List<TeacherDashboardClassViewModel> TodayClasses { get; set; } = new();
        public int TotalActiveCourses { get; set; }
        public int TotalStudents { get; set; }
        public int TotalClassesToday => TodayClasses.Count;
        public double OverallAttendanceRate { get; set; }

        // Graph Data
        public List<string> AttendanceLabels { get; set; } = new();
        public List<double> AttendanceValues { get; set; } = new();
        public List<string> CourseLabels { get; set; } = new();
        public List<double> CourseAttendanceValues { get; set; } = new();
    }

    public class TeacherDashboardClassViewModel
    {
        public int SlotId { get; set; }
        public string CourseName { get; set; }
        public string BatchName { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public bool IsMarked { get; set; }
        public string Room { get; set; }
    }
}
