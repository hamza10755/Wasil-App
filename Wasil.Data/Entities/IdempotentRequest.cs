using System;

namespace Wasil.Data.Entities;

public class IdempotentRequest
{
    public string IdempotencyKey { get; set; } = null!;
    public int StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
