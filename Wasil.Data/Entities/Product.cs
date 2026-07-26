using System;
using System.Collections.Generic;

namespace Wasil.Data.Entities;

public partial class Product
{
    public int ProductId { get; set; }

    public int? StoreId { get; set; }

    public string? Name { get; set; }

    public int? Price { get; set; }

    public bool Availability { get; set; }

    public virtual ICollection<OrderLine> OrderLines { get; set; } = new List<OrderLine>();

    public virtual ICollection<ProductModifier> ProductModifiers { get; set; } = new List<ProductModifier>();

    public virtual Store? Store { get; set; }
}
