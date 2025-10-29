using System;
using System.Collections.Generic;

namespace AMS.TempScaffold.Entities;

public partial class TimetableSlot
{
    public int SlotId { get; set; }

    public int? TimetableId { get; set; }

    public int? CourseAssignmentId { get; set; }

    public int? DayOfWeek { get; set; }

    public TimeOnly? StartTime { get; set; }

    public TimeOnly? EndTime { get; set; }

    public virtual CourseAssignment? CourseAssignment { get; set; }

    public virtual Timetable? Timetable { get; set; }
}
