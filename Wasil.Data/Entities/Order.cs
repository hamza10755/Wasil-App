using System.Collections.Generic;
using Wasil.Data.Enums;

namespace Wasil.Data.Entities;

public partial class Order : BaseEntity
{
    public string OrderCode { get; set; } = null!;

    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public PaymentMethod PaymentMethod { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal Total { get; set; }

    public int StoreId { get; set; }
    public int CustomerId { get; set; }
    public int? DriverId { get; set; }

    public virtual Customer Customer { get; set; } = null!;
    public virtual Store Store { get; set; } = null!;
    public virtual Driver? Driver { get; set; }

    public virtual ICollection<OrderLine> OrderLines { get; set; } = new List<OrderLine>();
    
    public virtual ICollection<OrderStatusHistory> StatusHistories { get; set; } = new List<OrderStatusHistory>();
}