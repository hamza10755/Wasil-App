using System;

namespace Wasil.Data.Entities;

public class ProcessedMessage
{
    public long Id { get; set; }
    public Guid MessageId { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}
