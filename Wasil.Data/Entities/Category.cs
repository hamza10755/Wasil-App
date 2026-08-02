using System;
using System.Collections.Generic;

namespace Wasil.Data.Entities;

public partial class Category : BaseEntity
{
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;

    public virtual ICollection<Product> Products { get; set; } = new List<Product>();
}
