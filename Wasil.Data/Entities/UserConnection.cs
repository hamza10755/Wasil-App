using System;

namespace Wasil.Data.Entities;

public class UserConnection : BaseEntity
{
    public Guid UserId { get; set; }
    public string ConnectionId { get; set; } = string.Empty;
    public Guid AppInstanceId { get; set; }
    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
}