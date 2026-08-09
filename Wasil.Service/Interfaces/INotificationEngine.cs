using System.Threading.Tasks;

namespace Wasil.Service.Interfaces;

public interface INotificationEngine
{
    Task SendStoreReportAsync(int storeId, string reportSummary);
}
