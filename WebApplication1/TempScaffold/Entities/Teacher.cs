using System;
using System.Collections.Generic;

namespace AMS.TempScaffold.Entities;

public partial class Teacher
{
    public int TeacherId { get; set; }

    public int? UserId { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public bool? IsActive { get; set; }

    public virtual ICollection<CourseAssignment> CourseAssignments { get; set; } = new List<CourseAssignment>();

    public virtual User? User { get; set; }
}
