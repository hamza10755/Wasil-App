using Wasil.Service.DTOs;
using Wasil.Data.Entities;

namespace Wasil.Service.Interfaces;

public interface IOrderService
{
    Order PlaceOrder(CreateOrderDto dto);
}