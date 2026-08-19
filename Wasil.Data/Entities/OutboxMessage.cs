using System;

namespace Wasil.Data.Entities;

public class OutboxMessage
{
    public long Id { get; set; }
    public Guid MessageId { get; set; }
    public string EventType { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    
    public string? LockedBy { get; set; }
    public DateTime? LockExpiresAtUtc { get; set; }
}
