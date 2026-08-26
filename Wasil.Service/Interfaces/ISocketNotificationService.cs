using System.Threading.Tasks;

namespace Wasil.Service.Interfaces;

public interface ISocketNotificationService
{
    Task SendOrderStatusUpdateAsync(long orderId, long storeId, string newStatus);
    Task SendOrderPlacedUpdateAsync(long orderId, long storeId);
}
