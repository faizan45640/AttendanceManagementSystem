namespace AMS.Models.ViewModels
{
    public class StudentDashboardViewModel
    {
        public string StudentName { get; set; }
        public string RollNumber { get; set; }
        public string BatchName { get; set; }
        
        // Stats
        public double OverallAttendancePercentage { get; set; }
        public int TotalClasses { get; set; }
        public int PresentClasses { get; set; }
        public int AbsentClasses { get; set; }

        // Today's Schedule
        public List<ClassSessionViewModel> TodayClasses { get; set; } = new List<ClassSessionViewModel>();

        // Alerts (e.g. Low attendance in specific subjects)
        public List<string> Alerts { get; set; } = new List<string>();
    }
}
