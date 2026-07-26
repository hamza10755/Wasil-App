using System;
using System.Collections.Generic;

namespace Wasil.Data.Entities;

public partial class Customer
{
    public int CustomerId { get; set; }

    public string? PhoneNumber { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Gender { get; set; }

    public string? Location { get; set; }

    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
}
