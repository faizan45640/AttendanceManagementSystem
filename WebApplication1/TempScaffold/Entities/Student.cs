using System;
using System.Collections.Generic;

namespace AMS.TempScaffold.Entities;

public partial class Student
{
    public int StudentId { get; set; }

    public int? UserId { get; set; }

    public string? RollNumber { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public int? BatchId { get; set; }

    public bool? IsActive { get; set; }

    public virtual ICollection<Attendance> Attendances { get; set; } = new List<Attendance>();

    public virtual Batch? Batch { get; set; }

    public virtual ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();

    public virtual User? User { get; set; }
}
