using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Wasil.Service.Interfaces;

namespace Wasil.Api.Hubs;

public class SocketNotificationService : ISocketNotificationService
{
    private readonly IHubContext<OrderHub> _hubContext;

    public SocketNotificationService(IHubContext<OrderHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task SendOrderStatusUpdateAsync(long orderId, long storeId, string newStatus)
    {
        await _hubContext.Clients.Group($"Order_{orderId}")
            .SendAsync("OrderStatusChanged", new { status = newStatus });

        await _hubContext.Clients.Group($"Store_{storeId}")
            .SendAsync("OrderStatusChanged", new { orderId = orderId, status = newStatus });
    }

    public async Task SendOrderPlacedUpdateAsync(long orderId, long storeId)
    {
        await _hubContext.Clients.Group($"Store_{storeId}")
            .SendAsync("OrderPlaced", new { orderId = orderId, status = "Pending" });
    }
}
