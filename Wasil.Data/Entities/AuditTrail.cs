using System;

namespace Wasil.Data.Entities;

public class AuditTrail
{
    public int Id { get; set; }
    public string EntityName { get; set; } = null!;
    public string EntityId { get; set; } = null!;
    public string Action { get; set; } = null!;
    public string Changes { get; set; } = null!;
    public DateTime TimestampUtc { get; set; }
    public string? UserId { get; set; }
}
