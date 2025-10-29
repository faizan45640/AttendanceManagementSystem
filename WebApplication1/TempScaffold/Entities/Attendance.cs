using System;
using System.Collections.Generic;

namespace AMS.TempScaffold.Entities;

public partial class Attendance
{
    public int AttendanceId { get; set; }

    public int? SessionId { get; set; }

    public int? StudentId { get; set; }

    public string? Status { get; set; }

    public int? MarkedBy { get; set; }

    public virtual User? MarkedByNavigation { get; set; }

    public virtual Session? Session { get; set; }

    public virtual Student? Student { get; set; }
}
