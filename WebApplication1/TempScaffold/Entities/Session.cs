using System;
using System.Collections.Generic;

namespace AMS.TempScaffold.Entities;

public partial class Session
{
    public int SessionId { get; set; }

    public int? CourseAssignmentId { get; set; }

    public DateOnly? SessionDate { get; set; }

    public TimeOnly? StartTime { get; set; }

    public TimeOnly? EndTime { get; set; }

    public int? CreatedBy { get; set; }

    public virtual ICollection<Attendance> Attendances { get; set; } = new List<Attendance>();

    public virtual CourseAssignment? CourseAssignment { get; set; }

    public virtual User? CreatedByNavigation { get; set; }
}
