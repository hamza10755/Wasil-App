using Wasil.Data.Enums;

namespace Wasil.Service.DTOs;

public class UpdateOrderStatusRequest
{
    public OrderStatus Status { get; set; }
}
