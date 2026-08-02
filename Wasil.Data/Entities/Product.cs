using System;
using System.Collections.Generic;

namespace Wasil.Data.Entities;

public partial class Product : BaseEntity
{

    public int StoreId { get; set; }
    public int CategoryId { get; set; }

    public string? Name { get; set; }

    public decimal Price { get; set; }
    public int StockQuantity { get; set; }

    public bool Availability { get; set; }

    public virtual ICollection<OrderLine> OrderLines { get; set; } = new List<OrderLine>();

    public virtual ICollection<ProductModifier> ProductModifiers { get; set; } = new List<ProductModifier>();

    public virtual Store Store { get; set; } = null!;
    public virtual Category Category { get; set; } = null!;
}
