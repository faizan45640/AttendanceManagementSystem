namespace AMS.Models.ViewModels
{
    public class StudentCourseViewModel
    {
        public string CourseName { get; set; }
        public string CourseCode { get; set; }
        public string TeacherName { get; set; }
        public int TotalClasses { get; set; }
        public int PresentClasses { get; set; }
        public double AttendancePercentage { get; set; }
        public int ClassesRemaining { get; set; }
        public int TotalSemesterClasses { get; set; }

    }
}
