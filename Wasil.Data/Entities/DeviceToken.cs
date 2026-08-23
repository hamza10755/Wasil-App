using System;

namespace Wasil.Data.Entities;

public class DeviceToken : BaseEntity
{
    public string Token { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public virtual User User { get; set; } = null!;
    public string Platform { get; set; } = "web";
    public DateTime RegisteredAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}