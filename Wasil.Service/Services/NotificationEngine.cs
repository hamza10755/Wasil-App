using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Wasil.Service.Interfaces;

namespace Wasil.Service.Services;

public class NotificationEngine : INotificationEngine
{
    private readonly ILogger<NotificationEngine> _logger;

    public NotificationEngine(ILogger<NotificationEngine> logger)
    {
        _logger = logger;
    }

    public Task SendStoreReportAsync(int storeId, string reportSummary)
    {
        _logger.LogInformation("Notification Engine: Sent sales report to Store {StoreId}. Summary: {Summary}", storeId, reportSummary);
        return Task.CompletedTask;
    }
}
