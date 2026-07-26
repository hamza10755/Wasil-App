using System;
using System.Collections.Generic;

namespace Wasil.Data.Entities;

public partial class Customer: BaseEntity
{

    public string? PhoneNumber { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Gender { get; set; }

    public string? Location { get; set; }

    public string Email { get; set; } = null!;

    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();

    public virtual ICollection<Address> Addresses { get; set; } = new List<Address>();
}
