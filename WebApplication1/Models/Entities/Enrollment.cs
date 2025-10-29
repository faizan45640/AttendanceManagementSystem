//using AMS.TempScaffold.Entities;

namespace AMS.Models.Entities
{
    public class Enrollment
    {
        public int EnrollmentId { get; set; }

        public int? StudentId { get; set; }

        public int? CourseId { get; set; }

        public int? SemesterId { get; set; }

        public string? Status { get; set; }

        public virtual Course? Course { get; set; }

        public virtual Semester? Semester { get; set; }

        public virtual Student? Student { get; set; }
    }
}
