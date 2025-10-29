using System;
using System.Collections.Generic;

namespace AMS.TempScaffold.Entities;

public partial class Admin
{
    public int AdminId { get; set; }

    public int? UserId { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public virtual User? User { get; set; }
}
