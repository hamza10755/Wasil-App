using System;
using System.Collections.Generic;

namespace Wasil.Data.Entities;

public partial class OrderLineModifier
{
    public int ModifierId { get; set; }

    public string? ModifierName { get; set; }

    public int? LineId { get; set; }

    public virtual OrderLine? Line { get; set; }
}
