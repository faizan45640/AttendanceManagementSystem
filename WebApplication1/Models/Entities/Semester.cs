//using AMS.TempScaffold.Entities;

namespace AMS.Models.Entities
{
    public class Semester
    {
        public int SemesterId { get; set; }

        public string SemesterName { get; set; }

        public int Year { get; set; }

        public DateOnly StartDate { get; set; }

        public DateOnly EndDate { get; set; }

        public bool IsActive { get; set; }

        public virtual ICollection<CourseAssignment> CourseAssignments { get; set; } = new List<CourseAssignment>();

        public virtual ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();

        public virtual ICollection<Timetable> Timetables { get; set; } = new List<Timetable>();
    }
}
