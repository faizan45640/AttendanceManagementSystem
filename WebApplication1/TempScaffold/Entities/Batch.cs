using System;
using System.Collections.Generic;

namespace AMS.TempScaffold.Entities;

public partial class Batch
{
    public int BatchId { get; set; }

    public string? BatchName { get; set; }

    public int? Year { get; set; }

    public bool? IsActive { get; set; }

    public virtual ICollection<CourseAssignment> CourseAssignments { get; set; } = new List<CourseAssignment>();

    public virtual ICollection<Student> Students { get; set; } = new List<Student>();

    public virtual ICollection<Timetable> Timetables { get; set; } = new List<Timetable>();
}
