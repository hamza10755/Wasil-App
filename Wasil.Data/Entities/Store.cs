using System;
using System.Collections.Generic;

namespace Wasil.Data.Entities;

public partial class Store
{
    public int StoreId { get; set; }

    public string? StoreName { get; set; }

    public string? StoreLocation { get; set; }

    public bool Status { get; set; }

    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();

    public virtual ICollection<Product> Products { get; set; } = new List<Product>();

    public virtual ICollection<StoreHour> StoreHours { get; set; } = new List<StoreHour>();
}
