using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wasil.Data.Entities;
using Wasil.Service.Messaging;
using Wasil.Service.Messaging.Events;

namespace Wasil.Service.Services;

public class OutboxRelayService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly RabbitMqEventPublisher _eventPublisher;
    private readonly ILogger<OutboxRelayService> _logger;

    private readonly string _relayId = $"Relay-{Guid.NewGuid():N}";

    public OutboxRelayService(
        IServiceProvider serviceProvider,
        RabbitMqEventPublisher eventPublisher,
        ILogger<OutboxRelayService> logger)
    {
        _serviceProvider = serviceProvider;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox Relay Service started (Instance ID: {RelayId}).", _relayId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int processedCount = await ProcessOutboxMessagesAsync(stoppingToken);

                if (processedCount == 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during outbox relay processing cycle.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task<int> ProcessOutboxMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WasilDbContext>();

        var utcNow = DateTime.UtcNow;
        var lockExpiry = utcNow.AddSeconds(30);

        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                var messages = await dbContext.OutboxMessages
                    .FromSqlRaw(
                        "SELECT TOP 50 * FROM OutboxMessage WITH (UPDLOCK, ROWLOCK) " +
                        "WHERE SentAtUtc IS NULL AND RetryCount < 5 AND (LockedBy IS NULL OR LockExpiresAtUtc < {0}) " +
                        "ORDER BY Id", utcNow)
                    .ToListAsync(cancellationToken);

                if (messages.Count == 0)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return 0;
                }

                foreach (var message in messages)
                {
                    message.LockedBy = _relayId;
                    message.LockExpiresAtUtc = lockExpiry;
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation("Claimed {Count} outbox messages for processing.", messages.Count);

                foreach (var message in messages)
                {
                    try
                    {
                        if (message.EventType == "OrderPlaced")
                        {
                            var orderPlacedEvent = JsonSerializer.Deserialize<OrderPlacedEvent>(message.Payload);
                            if (orderPlacedEvent != null)
                            {
                                _eventPublisher.Publish(message.MessageId, orderPlacedEvent);
                            }
                        }
                        else if (message.EventType == "OrderStatusChanged")
                        {
                            var orderStatusChangedEvent = JsonSerializer.Deserialize<OrderStatusChangedEvent>(message.Payload);
                            if (orderStatusChangedEvent != null)
                            {
                                _eventPublisher.Publish(message.MessageId, orderStatusChangedEvent);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Unknown event type: {EventType} for OutboxMessage {Id}", message.EventType, message.Id);
                        }

                        message.SentAtUtc = DateTime.UtcNow;
                        message.LockedBy = null;
                        message.LockExpiresAtUtc = null;
                        message.LastError = null;
                    }
                    catch (Exception ex)
                    {
                        message.RetryCount++;
                        message.LastError = ex.Message;
                        message.LockedBy = null;
                        message.LockExpiresAtUtc = null;

                        _logger.LogError(ex, "Failed to publish outbox message {Id} (Attempt: {Attempt}).", message.Id, message.RetryCount);
                    }
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                return messages.Count;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Failed to complete transaction during outbox claiming process.");
                throw;
            }
        });
    }
}
