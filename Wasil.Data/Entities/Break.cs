using System;
using System.Collections.Generic;

namespace Wasil.Data.Entities;

public partial class Break : BaseEntity
{

    public int? HourId { get; set; }

    public TimeOnly? OpenHours { get; set; }

    public TimeOnly? CloseHours { get; set; }

    public virtual StoreHour? Hour { get; set; }
}
