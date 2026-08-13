using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wasil.Data.Entities;
using Wasil.Data.Enums;

namespace Wasil.Service.Services;

public class SystemHeartbeatService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SystemHeartbeatService> _logger;

    public SystemHeartbeatService(IServiceProvider serviceProvider, ILogger<SystemHeartbeatService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SystemHeartbeatService starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<WasilDbContext>();

                    var statusCounts = await dbContext.Orders
                        .Where(o => o.Status == OrderStatus.Pending ||
                                    o.Status == OrderStatus.Accepted ||
                                    o.Status == OrderStatus.Preparing ||
                                    o.Status == OrderStatus.OutForDelivery)
                        .GroupBy(o => o.Status)
                        .Select(g => new { Status = g.Key, Count = g.Count() })
                        .ToListAsync(stoppingToken);

                    var countDict = statusCounts.ToDictionary(x => x.Status, x => x.Count);

                    int pending = countDict.GetValueOrDefault(OrderStatus.Pending, 0);
                    int accepted = countDict.GetValueOrDefault(OrderStatus.Accepted, 0);
                    int preparing = countDict.GetValueOrDefault(OrderStatus.Preparing, 0);
                    int outForDelivery = countDict.GetValueOrDefault(OrderStatus.OutForDelivery, 0);

                    _logger.LogInformation(
                        "Order Status Heartbeat - Pending: {Pending}, Accepted: {Accepted}, Preparing: {Preparing}, OutForDelivery: {OutForDelivery}",
                        pending, accepted, preparing, outForDelivery);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during SystemHeartbeatService execution.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("SystemHeartbeatService stopping.");
    }
}
