using System;
using System.Collections.Generic;

namespace Wasil.Data.Entities;

public partial class Driver : BaseEntity
{

    public string? PhoneNumber { get; set; }

    public string? Name { get; set; }

    public string? Location { get; set; }

    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
}
