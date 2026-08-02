using System;

namespace Wasil.Data.Entities;

public class AuditTrail : BaseEntity
{
    public string EntityName { get; set; } = null!;
    public string EntityId { get; set; } = null!;
    public string Action { get; set; } = null!;
    public string ChangesJson { get; set; } = null!;
    public DateTime TimestampUtc { get; set; }

    public int? UserId { get; set; }
}