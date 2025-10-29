using System;
using System.Collections.Generic;

namespace AMS.TempScaffold.Entities;

public partial class Course
{
    public int CourseId { get; set; }

    public string? CourseCode { get; set; }

    public string? CourseName { get; set; }

    public int? CreditHours { get; set; }

    public bool? IsActive { get; set; }

    public virtual ICollection<CourseAssignment> CourseAssignments { get; set; } = new List<CourseAssignment>();

    public virtual ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
}
