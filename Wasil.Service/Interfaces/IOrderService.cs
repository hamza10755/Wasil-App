using Wasil.Service.DTOs;
using Wasil.Service.DTOs.Shared;
using Wasil.Data.Entities;

namespace Wasil.Service.Interfaces;

public interface IOrderService
{
    Order PlaceOrder(CreateOrderDto dto);
    OrderDetailDto GetOrderDetails(int orderId);
    PagedResultDto<CustomerOrderHistoryDto> GetCustomerOrderHistory(int customerId, int page, int pageSize);
}