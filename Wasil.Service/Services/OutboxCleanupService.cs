using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wasil.Data.Entities;

namespace Wasil.Service.Services;

public class OutboxCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxCleanupService> _logger;

    public OutboxCleanupService(IServiceProvider serviceProvider, ILogger<OutboxCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox Cleanup Service starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<WasilDbContext>();

                var cutoff = DateTime.UtcNow.AddDays(-7);

                int deletedCount = await dbContext.Database.ExecuteSqlRawAsync(
                    "DELETE FROM [OutboxMessage] WHERE [SentAtUtc] IS NOT NULL AND [SentAtUtc] < {0}",
                    new object[] { cutoff },
                    cancellationToken: stoppingToken
                );

                if (deletedCount > 0)
                {
                    _logger.LogInformation("Outbox Cleanup: Successfully purged {Count} sent outbox messages older than 7 days.", deletedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during Outbox Cleanup Service execution.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Outbox Cleanup Service stopping.");
    }
}
