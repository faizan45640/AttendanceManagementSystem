using System;
using System.Collections.Generic;

namespace AMS.TempScaffold.Entities;

public partial class CourseAssignment
{
    public int AssignmentId { get; set; }

    public int? TeacherId { get; set; }

    public int? CourseId { get; set; }

    public int? BatchId { get; set; }

    public int? SemesterId { get; set; }

    public bool? IsActive { get; set; }

    public virtual Batch? Batch { get; set; }

    public virtual Course? Course { get; set; }

    public virtual Semester? Semester { get; set; }

    public virtual ICollection<Session> Sessions { get; set; } = new List<Session>();

    public virtual Teacher? Teacher { get; set; }

    public virtual ICollection<TimetableSlot> TimetableSlots { get; set; } = new List<TimetableSlot>();
}
