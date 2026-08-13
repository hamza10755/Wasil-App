using System;

namespace Wasil.Service.Messaging.Events;

public class SendOrderConfirmationMessage
{
    public Guid MessageId { get; set; } = Guid.NewGuid();
    public long OrderId { get; set; }
    public int SequenceNumber { get; set; }
}
