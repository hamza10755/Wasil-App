using System.Threading.Tasks;

namespace Wasil.Service.Interfaces;

public interface INotificationEngine
{
    Task SendStoreReportAsync(int storeId, string reportSummary);
    Task SendOrderStatusUpdateAsync(long customerId, long orderId, long storeId, string newStatus, bool isMarketing = false);
}
