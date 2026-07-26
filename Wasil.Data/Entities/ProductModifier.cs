using System;
using System.Collections.Generic;

namespace Wasil.Data.Entities;

public partial class ProductModifier
{
    public int ModifierId { get; set; }

    public int? ProductId { get; set; }

    public string? ModifierName { get; set; }

    public int? ExtraPrice { get; set; }

    public virtual Product? Product { get; set; }
}
