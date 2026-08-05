using System;

namespace Wasil.Data.Entities;

public class AuditTrail : BaseEntity
{
    public string EntityName { get; set; } = String.Empty;
    public string EntityId { get; set; } = String.Empty;
    public string Action { get; set; } = String.Empty;
    public string ChangesJson { get; set; } = String.Empty;
    public DateTime TimestampUtc { get; set; }

    public Guid? UserId { get; set; }
    public User? User { get; set; }
}