using System;
using System.Collections.Generic;

namespace Wasil.Data.Entities;

public partial class StoreHour : BaseEntity
{

    public int? StoreId { get; set; }

    public string? DaysOfWeek { get; set; }

    public TimeOnly? OpenHours { get; set; }

    public TimeOnly? CloseHours { get; set; }

    public virtual ICollection<Break> Breaks { get; set; } = new List<Break>();

    public virtual ICollection<Occasion> Occasions { get; set; } = new List<Occasion>();

    public virtual Store? Store { get; set; }
}
