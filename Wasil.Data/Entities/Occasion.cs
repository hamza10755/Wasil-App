using System;
using System.Collections.Generic;

namespace Wasil.Data.Entities;

public partial class Occasion
{
    public int OccasionId { get; set; }

    public string? OccasionType { get; set; }

    public TimeOnly? OpenHours { get; set; }

    public TimeOnly? CloseHours { get; set; }

    public int? HourId { get; set; }

    public virtual StoreHour? Hour { get; set; }
}
