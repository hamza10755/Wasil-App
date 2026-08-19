using System;

namespace Wasil.Service.Messaging.Events;

public class OrderStatusChangedEvent
{
    public Guid MessageId { get; set; } = Guid.NewGuid();
    public string EventName { get; set; } = "OrderStatusChanged";
    public int Version { get; set; } = 1;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public long OrderId { get; set; }
    public long StoreId { get; set; }
    public long CustomerId { get; set; }
    public string NewStatus { get; set; } = null!;
}
