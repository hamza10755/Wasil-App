using Wasil.Service.DTOs;
using Wasil.Service.DTOs.Shared;
using Wasil.Data.Entities;
using Wasil.Data.Enums;

namespace Wasil.Service.Interfaces;

public interface IOrderService
{
    Order PlaceOrder(CreateOrderDto dto);
    OrderDetailDto GetOrderDetails(int orderId);
    PagedResultDto<CustomerOrderHistoryDto> GetCustomerOrderHistory(int customerId, int page, int pageSize);
    void UpdateOrderStatus(int orderId, OrderStatus newStatus);
    StoreDashboardDto GetStoreDashboard(int storeId);
}