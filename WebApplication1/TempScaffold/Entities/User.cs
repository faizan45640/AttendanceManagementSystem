using System;
using System.Collections.Generic;

namespace AMS.TempScaffold.Entities;

public partial class User
{
    public int UserId { get; set; }

    public string? Username { get; set; }

    public string? Email { get; set; }

    public string? PasswordHash { get; set; }

    public string? Role { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Admin? Admin { get; set; }

    public virtual ICollection<Attendance> Attendances { get; set; } = new List<Attendance>();

    public virtual ICollection<Session> Sessions { get; set; } = new List<Session>();

    public virtual Student? Student { get; set; }

    public virtual Teacher? Teacher { get; set; }
}
