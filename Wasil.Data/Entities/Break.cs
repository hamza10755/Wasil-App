using System;
using System.Collections.Generic;

namespace Wasil.Data.Entities;

public partial class Break
{
    public int BreakId { get; set; }

    public int? HourId { get; set; }

    public TimeOnly? OpenHours { get; set; }

    public TimeOnly? CloseHours { get; set; }

    public virtual StoreHour? Hour { get; set; }
}
