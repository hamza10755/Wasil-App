using System;

namespace Wasil.Data.Entities;

public partial class Address : BaseEntity
{
    public int CustomerId { get; set; }
    public string Street { get; set; } = null!;
    public string City { get; set; } = null!;
    public string ZipCode { get; set; } = null!;
    public bool IsDefault { get; set; }

    public virtual Customer Customer { get; set; } = null!;
}
